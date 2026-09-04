#include "GpuBigInt.h"
#include <cuda_runtime.h>
#include <cufft.h>
#include <stdio.h>
#include <string>
#include <sstream>
#include <vector>
#include <unordered_map>

#include "GpuBigInt_Core.cuh"
#include "GpuBigInt_Kernels.cuh"
#include "GpuBigInt_Math.cuh"
#include "GpuBigInt_StaticLoop.cuh"

// -------------------------------------------------------------------------
// Phase 9: Static Device-Side Launcher
// -------------------------------------------------------------------------

struct FermatSetup {
    unsigned int *d_T, *d_Q, *d_R, *d_MaskR, *d_T_tmp;
    int numWords, outNumWords, maxGrid, maxBlock, sharedSize;
    
    bool init(GpuWorkspace* ws, int E) {
        numWords = (2 * E + 1 + 31) / 32;
        outNumWords = numWords * 2 + 16; 
        d_T = d_Q = d_R = d_MaskR = d_T_tmp = nullptr;
        
        if (cudaMalloc((void**)&d_T, sizeof(unsigned int) * outNumWords) != cudaSuccess) return false;
        if (cudaMalloc((void**)&d_Q, sizeof(unsigned int) * outNumWords) != cudaSuccess) return false;
        if (cudaMalloc((void**)&d_R, sizeof(unsigned int) * outNumWords) != cudaSuccess) return false;
        if (cudaMalloc((void**)&d_MaskR, sizeof(unsigned int) * outNumWords) != cudaSuccess) return false;
        if (cudaMalloc((void**)&d_T_tmp, sizeof(unsigned int) * outNumWords) != cudaSuccess) return false;
        
        cudaMemset(d_T, 0, sizeof(unsigned int) * outNumWords);
        cudaMemset(d_Q, 0, sizeof(unsigned int) * outNumWords);
        cudaMemset(d_R, 0, sizeof(unsigned int) * outNumWords);
        cudaMemset(d_MaskR, 0, sizeof(unsigned int) * outNumWords);
        cudaMemset(d_T_tmp, 0, sizeof(unsigned int) * outNumWords);
        
        unsigned int val2 = 2;
        cudaMemcpy(d_T, &val2, sizeof(unsigned int), cudaMemcpyHostToDevice);
        
        int bit2E1 = 2 * E + 1;
        int word2E1 = bit2E1 / 32;
        int rem2E1 = bit2E1 % 32;
        unsigned int* h_mask = new unsigned int[outNumWords]();
        for (int i = 0; i < word2E1; i++) h_mask[i] = 0xFFFFFFFF;
        if (rem2E1 > 0) h_mask[word2E1] = (1U << rem2E1) - 1;
        cudaMemcpy(d_MaskR, h_mask, sizeof(unsigned int) * outNumWords, cudaMemcpyHostToDevice);
        delete[] h_mask;
        
        maxGrid = 1;
        maxBlock = 256;
        if (ws->g_M <= 512) {
            maxBlock = ws->g_M / 2;
            int pointGrid = (ws->g_M + 255) / 256;
            maxGrid = max(maxGrid, pointGrid);
        } else {
            maxBlock = 256; 
            int N2 = 1;
            while (N2 * N2 < ws->g_M) N2 *= 2;
            int N1 = ws->g_M / N2;
            maxGrid = max(maxGrid, N1);
            maxGrid = max(maxGrid, N2);
            int pointGrid = (ws->g_M + 255) / 256;
            maxGrid = max(maxGrid, pointGrid);
            int gridT1 = ((N2 + 31) / 32) * ((N1 + 31) / 32);
            int gridT2 = ((N1 + 31) / 32) * ((N2 + 31) / 32);
            maxGrid = max(maxGrid, gridT1);
            maxGrid = max(maxGrid, gridT2);
        }
        int numChunks = (outNumWords + 255) / 256;
        maxGrid = max(maxGrid, numChunks);
        
        sharedSize = 0;
        if (ws->g_M <= 512) {
            sharedSize = ws->g_M * sizeof(uint32_t);
        } else {
            int N2 = 1;
            while (N2 * N2 < ws->g_M) N2 *= 2;
            int N1 = ws->g_M / N2;
            int rowBuf = max(N1, N2) * sizeof(uint32_t);
            int tileBuf = 32 * 33 * sizeof(uint32_t);
            sharedSize = max(rowBuf, tileBuf);
        }
        return true;
    }
    
    void cleanup() {
        if (d_T) cudaFree(d_T);
        if (d_Q) cudaFree(d_Q);
        if (d_R) cudaFree(d_R);
        if (d_MaskR) cudaFree(d_MaskR);
        if (d_T_tmp) cudaFree(d_T_tmp);
    }
};

extern "C" {
    EXPORT_API bool GpuBigInt_DynamicFermatLoop(void* workspace, int E, unsigned int* out_data, void(*jitLaunch)(unsigned int*, unsigned int*, unsigned int*, unsigned int*, unsigned int*, int, int, long long*, int, uint32_t*, uint32_t*, uint32_t*, uint32_t*, uint32_t*, uint32_t*, uint32_t*, int, int)) {
        GpuWorkspace* ws = (GpuWorkspace*)workspace;
        if (!ws) return false;
        
        FermatSetup s;
        if (!s.init(ws, E)) {
            s.cleanup();
            return false;
        }
        
        printf("Launching DynamicFermatLoop: Grid=%d, Block=%d\n", s.maxGrid, s.maxBlock);
        
        jitLaunch(s.d_T, s.d_Q, s.d_R, s.d_MaskR, s.d_T_tmp, s.numWords, E, ws->d_genericCarryBuffer, ws->g_M, ws->d_ntt_buf1, ws->d_ntt_buf2, ws->d_ntt_tmp, ws->d_twiddle1_fwd, ws->d_twiddle2_fwd, ws->d_twiddle1_inv, ws->d_twiddle2_inv, s.maxGrid, s.maxBlock);
        
        cudaError_t syncErr = cudaDeviceSynchronize();
        bool success = (syncErr == cudaSuccess);
        if (!success) {
            printf("Kernel Execution Failed: %s\n", cudaGetErrorString(syncErr));
        } else {
            cudaMemcpy(out_data, s.d_T, sizeof(unsigned int) * s.outNumWords, cudaMemcpyDeviceToHost);
        }
        
        s.cleanup();
        return success;
    }

    EXPORT_API bool GpuBigInt_StaticFermatLoop(void* workspace, int E, unsigned int* out_data) {
        GpuWorkspace* ws = (GpuWorkspace*)workspace;
        if (!ws) return false;

        FermatSetup s;
        if (!s.init(ws, E)) {
            s.cleanup();
            return false;
        }
        
        printf("Launching StaticFermatLoop: Grid=%d, Block=%d, Shared=%d\n", s.maxGrid, s.maxBlock, s.sharedSize);
        
        void* kernelArgs[] = {
            (void*)&s.d_T,
            (void*)&s.d_Q,
            (void*)&s.d_R,
            (void*)&s.d_MaskR,
            (void*)&s.d_T_tmp,
            (void*)&s.numWords,
            (void*)&E,
            (void*)&ws->d_genericCarryBuffer,
            (void*)&ws->g_M,
            (void*)&ws->d_ntt_buf1,
            (void*)&ws->d_ntt_buf2,
            (void*)&ws->d_ntt_tmp,
            (void*)&ws->d_twiddle1_fwd,
            (void*)&ws->d_twiddle2_fwd,
            (void*)&ws->d_twiddle1_inv,
            (void*)&ws->d_twiddle2_inv
        };
        
        cudaError_t err = cudaLaunchCooperativeKernel((const void*)Kernel_StaticFermatLoop, s.maxGrid, s.maxBlock, kernelArgs, s.sharedSize, 0);
        if (err != cudaSuccess) {
            printf("Cooperative Kernel Launch Failed: %s\n", cudaGetErrorString(err));
            fflush(stdout);
            s.cleanup();
            return false;
        }
        
        cudaError_t syncErr = cudaDeviceSynchronize();
        if (syncErr != cudaSuccess) {
            printf("Kernel Execution Failed: %s\n", cudaGetErrorString(syncErr));
            fflush(stdout);
            s.cleanup();
            return false;
        }
        
        cudaMemcpy(out_data, s.d_T, sizeof(unsigned int) * s.outNumWords, cudaMemcpyDeviceToHost);
        
        s.cleanup();
        return true;
    }

    EXPORT_API bool GpuBigInt_GraphFermatLoop(void* workspace, int E, unsigned int* out_data) {
        GpuWorkspace* ws = (GpuWorkspace*)workspace;
        if (!ws) return false;

        FermatSetup s;
        if (!s.init(ws, E)) {
            s.cleanup();
            return false;
        }

        cudaStream_t stream;
        cudaStreamCreate(&stream);

        int words = s.outNumWords;
        int origWords = s.numWords;

        auto squareAndReduce = [&]() {
            GpuBigInt_Square(workspace, s.d_T, origWords, stream);
            for (int j = 0; j < 3; j++) {
                GpuBigInt_ShiftRight(workspace, s.d_T, 2 * E + 1, s.d_Q, words, stream);
                GpuBigInt_BitwiseAnd(workspace, s.d_T, s.d_MaskR, s.d_R, words, stream);
                GpuBigInt_ShiftLeft(workspace, s.d_Q, E, s.d_T_tmp, words, stream);
                GpuBigInt_Add(workspace, s.d_R, s.d_T_tmp, s.d_R, words, stream);
                GpuBigInt_ShiftLeft(workspace, s.d_Q, 1, s.d_T_tmp, words, stream);
                GpuBigInt_Sub(workspace, s.d_R, s.d_Q, s.d_R, words, stream);
                GpuBigInt_Sub(workspace, s.d_R, s.d_T_tmp, s.d_R, words, stream);
                GpuBigInt_ShiftLeft(workspace, s.d_Q, 3, s.d_T_tmp, words, stream);
                GpuBigInt_Sub(workspace, s.d_R, s.d_T_tmp, s.d_T, words, stream);
            }
        };

        auto multiplyBy2AndReduce = [&]() {
            GpuBigInt_ShiftLeft(workspace, s.d_T, 1, s.d_T_tmp, words, stream);
            cudaMemcpyAsync(s.d_T, s.d_T_tmp, sizeof(unsigned int) * words, cudaMemcpyDeviceToDevice, stream);
            for (int j = 0; j < 3; j++) {
                GpuBigInt_ShiftRight(workspace, s.d_T, 2 * E + 1, s.d_Q, words, stream);
                GpuBigInt_BitwiseAnd(workspace, s.d_T, s.d_MaskR, s.d_R, words, stream);
                GpuBigInt_ShiftLeft(workspace, s.d_Q, E, s.d_T_tmp, words, stream);
                GpuBigInt_Add(workspace, s.d_R, s.d_T_tmp, s.d_R, words, stream);
                GpuBigInt_ShiftLeft(workspace, s.d_Q, 1, s.d_T_tmp, words, stream);
                GpuBigInt_Sub(workspace, s.d_R, s.d_Q, s.d_R, words, stream);
                GpuBigInt_Sub(workspace, s.d_R, s.d_T_tmp, s.d_R, words, stream);
                GpuBigInt_ShiftLeft(workspace, s.d_Q, 3, s.d_T_tmp, words, stream);
                GpuBigInt_Sub(workspace, s.d_R, s.d_T_tmp, s.d_T, words, stream);
            }
        };

        printf("Building CUDA Graphs for E=%d...\n", E);
        fflush(stdout);
        
        // Graph A: Square and MultiplyBy2
        cudaGraph_t graphA;
        cudaStreamBeginCapture(stream, cudaStreamCaptureModeGlobal);
        squareAndReduce();
        multiplyBy2AndReduce();
        cudaError_t errA1 = cudaStreamEndCapture(stream, &graphA);
        if (errA1 != cudaSuccess) { printf("Graph A Capture failed: %s\n", cudaGetErrorString(errA1)); fflush(stdout); return false; }
        
        cudaGraphExec_t execA;
        cudaError_t errA2 = cudaGraphInstantiate(&execA, graphA, NULL, NULL, 0);
        if (errA2 != cudaSuccess) { printf("Graph A Instantiate failed: %s\n", cudaGetErrorString(errA2)); fflush(stdout); return false; }
        printf("Graph A instantiated.\n"); fflush(stdout);

        // Graph B: Square only
        cudaGraph_t graphB;
        cudaStreamBeginCapture(stream, cudaStreamCaptureModeGlobal);
        squareAndReduce();
        cudaError_t errB1 = cudaStreamEndCapture(stream, &graphB);
        if (errB1 != cudaSuccess) { printf("Graph B Capture failed: %s\n", cudaGetErrorString(errB1)); fflush(stdout); return false; }
        
        cudaGraphExec_t execB;
        cudaError_t errB2 = cudaGraphInstantiate(&execB, graphB, NULL, NULL, 0);
        if (errB2 != cudaSuccess) { printf("Graph B Instantiate failed: %s\n", cudaGetErrorString(errB2)); fflush(stdout); return false; }
        printf("Graph B instantiated.\n"); fflush(stdout);


        // Graph C: Final bits 1010
        cudaGraph_t graphC;
        cudaStreamBeginCapture(stream, cudaStreamCaptureModeGlobal);
        squareAndReduce(); multiplyBy2AndReduce(); // 1
        squareAndReduce(); // 0
        squareAndReduce(); multiplyBy2AndReduce(); // 1
        squareAndReduce(); // 0
        cudaError_t errC1 = cudaStreamEndCapture(stream, &graphC);
        if (errC1 != cudaSuccess) { printf("Graph C Capture failed: %s\n", cudaGetErrorString(errC1)); fflush(stdout); return false; }
        
        cudaGraphExec_t execC;
        cudaError_t errC2 = cudaGraphInstantiate(&execC, graphC, NULL, NULL, 0);
        if (errC2 != cudaSuccess) { printf("Graph C Instantiate failed: %s\n", cudaGetErrorString(errC2)); fflush(stdout); return false; }
        printf("Graph C instantiated.\n"); fflush(stdout);

        printf("Running Graph A (E times)...\n"); fflush(stdout);
        for (int i = 0; i < E; i++) {
            cudaGraphLaunch(execA, stream);
        }

        printf("Running Graph B (E-4 times)...\n");
        for (int i = 0; i < E - 4; i++) {
            cudaGraphLaunch(execB, stream);
        }

        printf("Running Graph C (Final 4 bits)...\n");
        cudaGraphLaunch(execC, stream);

        cudaStreamSynchronize(stream);

        cudaMemcpy(out_data, s.d_T, sizeof(unsigned int) * s.outNumWords, cudaMemcpyDeviceToHost);

        cudaGraphExecDestroy(execA); cudaGraphDestroy(graphA);
        cudaGraphExecDestroy(execB); cudaGraphDestroy(graphB);
        cudaGraphExecDestroy(execC); cudaGraphDestroy(graphC);
        cudaStreamDestroy(stream);
        s.cleanup();
        
        return true;
    }


    typedef void (*JitLaunchCallback)(
        unsigned int* T, unsigned int* Q, unsigned int* R, unsigned int* MaskR,
        unsigned int* T_tmp,
        int numWords, int E_val, long long* carryBuffer,
        int M, uint32_t* ntt_buf1, uint32_t* ntt_buf2, uint32_t* ntt_tmp,
        const uint32_t* twiddle1_fwd, const uint32_t* twiddle2_fwd,
        const uint32_t* twiddle1_inv, const uint32_t* twiddle2_inv,
        int gridDim, int blockDim
    );

    EXPORT_API bool GpuBigInt_RunJitFermatLoop(void* workspace, int E, unsigned int* out_data, JitLaunchCallback callback) {
        GpuWorkspace* ws = (GpuWorkspace*)workspace;
        if (!ws) return false;

        FermatSetup s;
        if (!s.init(ws, E)) {
            s.cleanup();
            return false;
        }
        
        printf("Executing JIT Callback with Grid=%d, Block=%d\n", s.maxGrid, s.maxBlock);
        
        // Invoke the JIT-compiled function via callback
        callback(
            s.d_T, s.d_Q, s.d_R, s.d_MaskR, s.d_T_tmp, 
            s.numWords, E, ws->d_genericCarryBuffer, 
            ws->g_M, ws->d_ntt_buf1, ws->d_ntt_buf2, ws->d_ntt_tmp,
            ws->d_twiddle1_fwd, ws->d_twiddle2_fwd, ws->d_twiddle1_inv, ws->d_twiddle2_inv,
            s.maxGrid, s.maxBlock
        );
        
        cudaError_t syncErr = cudaDeviceSynchronize();
        if (syncErr != cudaSuccess) {
            printf("JIT Kernel Execution Failed: %s\n", cudaGetErrorString(syncErr));
            s.cleanup();
            return false;
        }
        
        cudaMemcpy(out_data, s.d_T, sizeof(unsigned int) * s.outNumWords, cudaMemcpyDeviceToHost);
        
        s.cleanup();
        return true;
    }
}


// -------------------------------------------------------------------------
// Helper Functions
// -------------------------------------------------------------------------
uint32_t CpuModPow(uint64_t base, uint64_t exp, uint32_t p) {
    uint64_t res = 1;
    base %= p;
    while (exp > 0) {
        if (exp & 1) res = (res * base) % p;
        base = (base * base) % p;
        exp >>= 1;
    }
    return (uint32_t)res;
}

// -------------------------------------------------------------------------
// Exported Initialization API
// -------------------------------------------------------------------------
extern "C" {
    EXPORT_API void* GpuBigInt_Init(int maxWords) {
        int simulatedE = (maxWords * 32) / 2; 
        int E = simulatedE;
        GpuWorkspace* ws = new GpuWorkspace();
        
        int requiredElements = (4 * E + 2 + 15) / 16;
        ws->g_M = NextPowerOf2(requiredElements);
        
        cudaMalloc((void**)&ws->d_twiddle1_fwd, sizeof(uint32_t) * ws->g_M);
        cudaMalloc((void**)&ws->d_twiddle1_inv, sizeof(uint32_t) * ws->g_M);
        cudaMalloc((void**)&ws->d_twiddle2_fwd, sizeof(uint32_t) * ws->g_M);
        cudaMalloc((void**)&ws->d_twiddle2_inv, sizeof(uint32_t) * ws->g_M);
        
        uint32_t* h_t1_fwd = new uint32_t[ws->g_M];
        uint32_t* h_t1_inv = new uint32_t[ws->g_M];
        uint32_t* h_t2_fwd = new uint32_t[ws->g_M];
        uint32_t* h_t2_inv = new uint32_t[ws->g_M];
        
        uint32_t p1 = 4253024257;
        uint32_t p2 = 4246732801;
        uint32_t w1 = CpuModPow(5, (p1 - 1) / ws->g_M, p1);
        uint32_t w2 = CpuModPow(2, (p2 - 1) / ws->g_M, p2);
        
        uint32_t iw1 = CpuModPow(w1, p1 - 2, p1);
        uint32_t iw2 = CpuModPow(w2, p2 - 2, p2);
        
        uint64_t curr_w1 = 1, curr_iw1 = 1;
        uint64_t curr_w2 = 1, curr_iw2 = 1;
        
        for (int i = 0; i < ws->g_M; i++) {
            h_t1_fwd[i] = (uint32_t)curr_w1;
            h_t1_inv[i] = (uint32_t)curr_iw1;
            h_t2_fwd[i] = (uint32_t)curr_w2;
            h_t2_inv[i] = (uint32_t)curr_iw2;
            
            curr_w1 = (curr_w1 * w1) % p1;
            curr_iw1 = (curr_iw1 * iw1) % p1;
            curr_w2 = (curr_w2 * w2) % p2;
            curr_iw2 = (curr_iw2 * iw2) % p2;
        }
        
        cudaMemcpy(ws->d_twiddle1_fwd, h_t1_fwd, sizeof(uint32_t) * ws->g_M, cudaMemcpyHostToDevice);
        cudaMemcpy(ws->d_twiddle1_inv, h_t1_inv, sizeof(uint32_t) * ws->g_M, cudaMemcpyHostToDevice);
        cudaMemcpy(ws->d_twiddle2_fwd, h_t2_fwd, sizeof(uint32_t) * ws->g_M, cudaMemcpyHostToDevice);
        cudaMemcpy(ws->d_twiddle2_inv, h_t2_inv, sizeof(uint32_t) * ws->g_M, cudaMemcpyHostToDevice);
        
        delete[] h_t1_fwd; delete[] h_t1_inv;
        delete[] h_t2_fwd; delete[] h_t2_inv;

        cudaMalloc((void**)&ws->d_ntt_buf1, sizeof(uint32_t) * ws->g_M);
        cudaMalloc((void**)&ws->d_ntt_buf2, sizeof(uint32_t) * ws->g_M);
        cudaMalloc((void**)&ws->d_ntt_tmp, sizeof(uint32_t) * ws->g_M);

        int maxUintWords = (ws->g_M * 16 + 31) / 32;
        cudaMalloc((void**)&ws->d_data, sizeof(unsigned int) * maxUintWords);
        
        int chunkSize = 256;
        ws->g_genericMaxChunks = (maxWords + chunkSize - 1) / chunkSize;
        cudaMalloc((void**)&ws->d_genericCarryBuffer, sizeof(long long) * (ws->g_genericMaxChunks + 1));
        
        return (void*)ws;
    }
    
    EXPORT_API void GpuBigInt_Cleanup(void* workspace) { 
        GpuWorkspace* ws = (GpuWorkspace*)workspace;
        if (!ws) return;
        if (ws->d_twiddle1_fwd) cudaFree(ws->d_twiddle1_fwd);
        if (ws->d_twiddle1_inv) cudaFree(ws->d_twiddle1_inv);
        if (ws->d_twiddle2_fwd) cudaFree(ws->d_twiddle2_fwd);
        if (ws->d_twiddle2_inv) cudaFree(ws->d_twiddle2_inv);
        if (ws->d_ntt_buf1) cudaFree(ws->d_ntt_buf1);
        if (ws->d_ntt_buf2) cudaFree(ws->d_ntt_buf2);
        if (ws->d_ntt_tmp) cudaFree(ws->d_ntt_tmp);
        if (ws->d_data) cudaFree(ws->d_data);
        if (ws->d_genericCarryBuffer) cudaFree(ws->d_genericCarryBuffer);
        delete ws;
    }
    
    EXPORT_API unsigned int* GpuBigInt_Alloc(int numWords) {
        unsigned int* ptr = nullptr;
        cudaMalloc((void**)&ptr, sizeof(unsigned int) * numWords);
        return ptr;
    }

    EXPORT_API void GpuBigInt_Free(unsigned int* ptr) {
        if (ptr) cudaFree(ptr);
    }

    EXPORT_API bool GpuBigInt_CopyHostToDevice(unsigned int* d_dst, const unsigned int* h_src, int numWords, cudaStream_t stream) {
        return cudaMemcpyAsync(d_dst, h_src, sizeof(unsigned int) * numWords, cudaMemcpyHostToDevice, stream) == cudaSuccess;
    }

    EXPORT_API bool GpuBigInt_CopyDeviceToHost(unsigned int* h_dst, const unsigned int* d_src, int numWords, cudaStream_t stream) {
        return cudaMemcpyAsync(h_dst, d_src, sizeof(unsigned int) * numWords, cudaMemcpyDeviceToHost, stream) == cudaSuccess;
    }

    EXPORT_API bool GpuBigInt_CopyDeviceToDevice(unsigned int* d_dst, const unsigned int* d_src, int numWords, cudaStream_t stream) {
        return cudaMemcpyAsync(d_dst, d_src, sizeof(unsigned int) * numWords, cudaMemcpyDeviceToDevice, stream) == cudaSuccess;
    }

// -------------------------------------------------------------------------
// IL Executor Engine
// -------------------------------------------------------------------------
    EXPORT_API bool GpuBigInt_ExecuteIL(void* workspace, int E, const char* ilScript, unsigned int* out_data) {
        GpuWorkspace* ws = (GpuWorkspace*)workspace;
        if (!ws) return false;
        
        int numWords = (2 * E + 1 + 31) / 32;
        int outNumWords = numWords * 2 + 16; 

        std::unordered_map<std::string, unsigned int*> vars;
        
        std::istringstream stream_prescan(ilScript);
        std::string pre_line;
        while (std::getline(stream_prescan, pre_line)) {
            if (pre_line.empty() || pre_line[0] == '\r') continue;
            std::istringstream iss(pre_line);
            std::string op;
            iss >> op;
            if (op == "SQUARE" || op == "COPY") {
                std::string dest, src; iss >> dest >> src;
                vars[dest] = nullptr; vars[src] = nullptr;
            } else if (op == "SHR" || op == "SHL") {
                std::string dest, src; int bits; iss >> dest >> src >> bits;
                vars[dest] = nullptr; vars[src] = nullptr;
            } else if (op == "ADD" || op == "SUB" || op == "AND") {
                std::string dest, a, b; iss >> dest >> a >> b;
                vars[dest] = nullptr; vars[a] = nullptr; vars[b] = nullptr;
            }
        }
        
        vars["T"] = nullptr;
        vars["MaskR"] = nullptr;
        vars["N"] = nullptr;

        for (auto& pair : vars) {
            cudaMalloc((void**)&pair.second, sizeof(unsigned int) * outNumWords * 2);
            cudaMemsetAsync(pair.second, 0, sizeof(unsigned int) * outNumWords * 2);
        }

        unsigned int val2 = 2;
        cudaMemcpy(vars["T"], &val2, sizeof(unsigned int), cudaMemcpyHostToDevice);

        int bit2E1 = 2 * E + 1;
        int word2E1 = bit2E1 / 32;
        int rem2E1 = bit2E1 % 32;
        unsigned int* h_mask = new unsigned int[outNumWords]();
        for (int i = 0; i < word2E1; i++) h_mask[i] = 0xFFFFFFFF;
        if (rem2E1 > 0) h_mask[word2E1] = (1U << rem2E1) - 1;
        cudaMemcpy(vars["MaskR"], h_mask, sizeof(unsigned int) * outNumWords, cudaMemcpyHostToDevice);

        unsigned int* h_N = new unsigned int[outNumWords]();
        h_N[0] = 11;
        h_N[word2E1] |= (1U << rem2E1);
        int wordE = E / 32;
        int remE = E % 32;
        
        unsigned int borrow = 0;
        unsigned int subVal = (1U << remE);
        for (int i = 0; i < outNumWords; i++) {
            unsigned int s = (i == wordE) ? subVal : 0;
            long long diff = (long long)h_N[i] - s - borrow;
            if (diff < 0) {
                diff += 0x100000000LL;
                borrow = 1;
            } else {
                borrow = 0;
            }
            h_N[i] = (unsigned int)diff;
        }
        cudaMemcpy(vars["N"], h_N, sizeof(unsigned int) * outNumWords, cudaMemcpyHostToDevice);
        delete[] h_N;

        cudaStream_t stream;
        cudaStreamCreate(&stream);

        auto runSnippet = [&](const std::string& snippet) {
            std::istringstream stream_il(snippet);
            std::string line;
            while (std::getline(stream_il, line)) {
                if (line.empty() || line[0] == '\r') continue;
                std::istringstream iss(line);
                std::string op;
                iss >> op;
                if (op == "SQUARE") {
                    std::string dest, src; iss >> dest >> src;
                    if (dest != src) {
                        cudaMemcpyAsync(vars[dest], vars[src], sizeof(unsigned int) * outNumWords, cudaMemcpyDeviceToDevice, stream);
                    }
                    GpuBigInt_Square(workspace, vars[dest], numWords + 1, stream);
                }
                else if (op == "SHR") {
                    std::string dest, src; int bits; iss >> dest >> src >> bits;
                    GpuBigInt_ShiftRight(workspace, vars[src], bits, vars[dest], outNumWords, stream);
                }
                else if (op == "SHL") {
                    std::string dest, src; int bits; iss >> dest >> src >> bits;
                    GpuBigInt_ShiftLeft(workspace, vars[src], bits, vars[dest], outNumWords, stream);
                }
                else if (op == "AND") {
                    std::string dest, src, mask; iss >> dest >> src >> mask;
                    GpuBigInt_BitwiseAnd(workspace, vars[src], vars[mask], vars[dest], outNumWords, stream);
                }
                else if (op == "ADD") {
                    std::string dest, a, b; iss >> dest >> a >> b;
                    GpuBigInt_Add(workspace, vars[a], vars[b], vars[dest], outNumWords, stream);
                }
                else if (op == "SUB") {
                    std::string dest, a, b; iss >> dest >> a >> b;
                    GpuBigInt_Sub(workspace, vars[a], vars[b], vars[dest], outNumWords, stream);
                }
                else if (op == "COPY") {
                    std::string dest, src; iss >> dest >> src;
                    cudaMemcpyAsync(vars[dest], vars[src], sizeof(unsigned int) * outNumWords, cudaMemcpyDeviceToDevice, stream);
                }
            }
        };

        std::vector<std::string> segments;
        std::istringstream full_il(ilScript);
        std::string line;
        std::string currentSegment = "";
        while (std::getline(full_il, line)) {
            if (line.find("LOOP_E") != std::string::npos && line.find("END") == std::string::npos && line.find("MINUS") == std::string::npos) {
                segments.push_back(currentSegment); currentSegment = "";
            }
            else if (line.find("LOOP_E_MINUS_4") != std::string::npos) {
                segments.push_back(currentSegment); currentSegment = "";
            }
            else if (line.find("END_LOOP") != std::string::npos) {
                segments.push_back(currentSegment); currentSegment = "";
            }
            else {
                currentSegment += line + "\n";
            }
        }
        segments.push_back(currentSegment);

        auto captureAndExecute = [&](const std::string& snippet, int iterations) {
            if (iterations <= 0) return;
            cudaGraph_t graph;
            
            cudaStreamBeginCapture(stream, cudaStreamCaptureModeGlobal);
            
            runSnippet(snippet);
            
            cudaStreamEndCapture(stream, &graph);
            
            cudaGraphExec_t instance;
            cudaGraphInstantiate(&instance, graph, NULL, NULL, 0);
            
            for (int i = 0; i < iterations; i++) {
                cudaGraphLaunch(instance, stream);
            }
            
            cudaStreamSynchronize(stream);
            cudaGraphExecDestroy(instance);
            cudaGraphDestroy(graph);
        };
        
        runSnippet(segments[0]); 
        cudaStreamSynchronize(stream);
        
        captureAndExecute(segments[1], E); 
        
        runSnippet(segments[2]);
        cudaStreamSynchronize(stream);
        
        captureAndExecute(segments[3], E - 4); 
        
        runSnippet(segments[4]);
        cudaStreamSynchronize(stream);

        cudaMemcpy(out_data, vars["T"], sizeof(unsigned int) * outNumWords, cudaMemcpyDeviceToHost);

        for (auto& pair : vars) {
            cudaFree(pair.second);
        }
        delete[] h_mask;
        cudaStreamDestroy(stream);

        return true;
    }
}
