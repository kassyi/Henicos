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

extern "C" {
    EXPORT_API bool GpuBigInt_DynamicFermatLoop(int E, unsigned int* out_data, void(*jitLaunch)(unsigned int*, unsigned int*, unsigned int*, unsigned int*, unsigned int*, int, int, long long*, int, uint32_t*, uint32_t*, uint32_t*, uint32_t*, uint32_t*, uint32_t*, uint32_t*, int, int)) {
        int numWords = (2 * E + 1 + 31) / 32;
        int outNumWords = numWords * 2 + 16; 
        
        if (!InitializeGPU_Internal(E)) return false;

        unsigned int *d_T, *d_Q, *d_R, *d_MaskR, *d_T_tmp;
        cudaMalloc((void**)&d_T, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_Q, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_R, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_MaskR, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_T_tmp, sizeof(unsigned int) * outNumWords);
        
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
        
        // Calculate Grid and Block sizes
        int maxGrid = 1;
        int maxBlock = 256;
        
        if (g_M <= 512) {
            maxBlock = g_M / 2;
            int pointGrid = (g_M + 255) / 256;
            maxGrid = max(maxGrid, pointGrid);
        } else {
            maxBlock = 256; 
            int N2 = 1;
            while (N2 * N2 < g_M) N2 *= 2;
            int N1 = g_M / N2;
            
            maxGrid = max(maxGrid, N1);
            maxGrid = max(maxGrid, N2);
            
            int pointGrid = (g_M + 255) / 256;
            maxGrid = max(maxGrid, pointGrid);
            
            int gridT1 = ((N2 + 31) / 32) * ((N1 + 31) / 32);
            int gridT2 = ((N1 + 31) / 32) * ((N2 + 31) / 32);
            maxGrid = max(maxGrid, gridT1);
            maxGrid = max(maxGrid, gridT2);
        }
        
        int numChunks = (outNumWords + 255) / 256;
        maxGrid = max(maxGrid, numChunks);
        
        printf("Launching DynamicFermatLoop: Grid=%d, Block=%d\n", maxGrid, maxBlock);
        
        jitLaunch(d_T, d_Q, d_R, d_MaskR, d_T_tmp, numWords, E, d_genericCarryBuffer, g_M, d_ntt_buf1, d_ntt_buf2, d_ntt_tmp, d_twiddle1_fwd, d_twiddle2_fwd, d_twiddle1_inv, d_twiddle2_inv, maxGrid, maxBlock);
        
        cudaMemcpy(out_data, d_T, sizeof(unsigned int) * outNumWords, cudaMemcpyDeviceToHost);
        
        cudaFree(d_T); cudaFree(d_Q); cudaFree(d_R); cudaFree(d_MaskR); cudaFree(d_T_tmp);
        return true;
    }

    EXPORT_API bool GpuBigInt_StaticFermatLoop(int E, unsigned int* out_data) {
        int numWords = (2 * E + 1 + 31) / 32;
        int outNumWords = numWords * 2 + 16; 
        
        if (!InitializeGPU_Internal(E)) return false;

        unsigned int *d_T, *d_Q, *d_R, *d_MaskR, *d_T_tmp;
        cudaMalloc((void**)&d_T, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_Q, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_R, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_MaskR, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_T_tmp, sizeof(unsigned int) * outNumWords);
        
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
        
        // Calculate Grid and Block sizes
        int maxGrid = 1;
        int maxBlock = 256;
        
        if (g_M <= 512) {
            maxBlock = g_M / 2;
            int pointGrid = (g_M + 255) / 256;
            maxGrid = max(maxGrid, pointGrid);
        } else {
            maxBlock = 256; 
            int N2 = 1;
            while (N2 * N2 < g_M) N2 *= 2;
            int N1 = g_M / N2;
            
            maxGrid = max(maxGrid, N1);
            maxGrid = max(maxGrid, N2);
            
            int pointGrid = (g_M + 255) / 256;
            maxGrid = max(maxGrid, pointGrid);
            
            int gridT1 = ((N2 + 31) / 32) * ((N1 + 31) / 32);
            int gridT2 = ((N1 + 31) / 32) * ((N2 + 31) / 32);
            maxGrid = max(maxGrid, gridT1);
            maxGrid = max(maxGrid, gridT2);
        }
        
        // Add/Sub/Shift limits
        int numChunks = (outNumWords + 255) / 256;
        maxGrid = max(maxGrid, numChunks);
        
        // Calculate Shared Memory (max of Transpose tile and Row NTT buffer)
        int sharedSize = 0;
        if (g_M <= 512) {
            sharedSize = g_M * sizeof(uint32_t);
        } else {
            int N2 = 1;
            while (N2 * N2 < g_M) N2 *= 2;
            int N1 = g_M / N2;
            int rowBuf = max(N1, N2) * sizeof(uint32_t);
            int tileBuf = 32 * 33 * sizeof(uint32_t);
            sharedSize = max(rowBuf, tileBuf);
        }
        
        printf("Launching StaticFermatLoop: Grid=%d, Block=%d, Shared=%d\n", maxGrid, maxBlock, sharedSize);
        
        void* kernelArgs[] = {
            (void*)&d_T,
            (void*)&d_Q,
            (void*)&d_R,
            (void*)&d_MaskR,
            (void*)&d_T_tmp,
            (void*)&numWords,
            (void*)&E,
            (void*)&d_genericCarryBuffer,
            (void*)&g_M,
            (void*)&d_ntt_buf1,
            (void*)&d_ntt_buf2,
            (void*)&d_ntt_tmp,
            (void*)&d_twiddle1_fwd,
            (void*)&d_twiddle2_fwd,
            (void*)&d_twiddle1_inv,
            (void*)&d_twiddle2_inv
        };
        
        cudaError_t err = cudaLaunchCooperativeKernel((const void*)Kernel_StaticFermatLoop, maxGrid, maxBlock, kernelArgs, sharedSize, 0);
        if (err != cudaSuccess) {
            printf("Cooperative Kernel Launch Failed: %s\n", cudaGetErrorString(err));
            fflush(stdout);
            return false;
        }
        
        cudaError_t syncErr = cudaDeviceSynchronize();
        if (syncErr != cudaSuccess) {
            printf("Kernel Execution Failed: %s\n", cudaGetErrorString(syncErr));
            fflush(stdout);
            return false;
        }
        
        cudaMemcpy(out_data, d_T, sizeof(unsigned int) * outNumWords, cudaMemcpyDeviceToHost);
        
        cudaFree(d_T); cudaFree(d_Q); cudaFree(d_R); cudaFree(d_MaskR); cudaFree(d_T_tmp);
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

    EXPORT_API bool GpuBigInt_RunJitFermatLoop(int E, unsigned int* out_data, JitLaunchCallback callback) {
        int numWords = (2 * E + 1 + 31) / 32;
        int outNumWords = numWords * 2 + 16; 
        
        if (!InitializeGPU_Internal(E)) return false;

        unsigned int *d_T, *d_Q, *d_R, *d_MaskR, *d_T_tmp;
        cudaMalloc((void**)&d_T, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_Q, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_R, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_MaskR, sizeof(unsigned int) * outNumWords);
        cudaMalloc((void**)&d_T_tmp, sizeof(unsigned int) * outNumWords);
        
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
        
        int maxGrid = 1;
        int maxBlock = 256;
        if (g_M <= 512) {
            maxBlock = g_M / 2;
            int pointGrid = (g_M + 255) / 256;
            maxGrid = max(maxGrid, pointGrid);
        } else {
            maxBlock = 256; 
            int N2 = 1;
            while (N2 * N2 < g_M) N2 *= 2;
            int N1 = g_M / N2;
            maxGrid = max(maxGrid, N1);
            maxGrid = max(maxGrid, N2);
            int pointGrid = (g_M + 255) / 256;
            maxGrid = max(maxGrid, pointGrid);
            int gridT1 = ((N2 + 31) / 32) * ((N1 + 31) / 32);
            int gridT2 = ((N1 + 31) / 32) * ((N2 + 31) / 32);
            maxGrid = max(maxGrid, gridT1);
            maxGrid = max(maxGrid, gridT2);
        }
        int numChunks = (outNumWords + 255) / 256;
        maxGrid = max(maxGrid, numChunks);
        
        printf("Executing JIT Callback with Grid=%d, Block=%d\n", maxGrid, maxBlock);
        
        // Invoke the JIT-compiled function via callback
        callback(
            d_T, d_Q, d_R, d_MaskR, d_T_tmp, 
            numWords, E, d_genericCarryBuffer, 
            g_M, d_ntt_buf1, d_ntt_buf2, d_ntt_tmp,
            d_twiddle1_fwd, d_twiddle2_fwd, d_twiddle1_inv, d_twiddle2_inv,
            maxGrid, maxBlock
        );
        
        cudaError_t syncErr = cudaDeviceSynchronize();
        if (syncErr != cudaSuccess) {
            printf("JIT Kernel Execution Failed: %s\n", cudaGetErrorString(syncErr));
        }
        
        cudaMemcpy(out_data, d_T, sizeof(unsigned int) * outNumWords, cudaMemcpyDeviceToHost);
        
        cudaFree(d_T); cudaFree(d_Q); cudaFree(d_R); cudaFree(d_MaskR); cudaFree(d_T_tmp);
        return true;
    }
}


// -------------------------------------------------------------------------
// Global State Implementation
// -------------------------------------------------------------------------
uint32_t* d_twiddle1_fwd = nullptr;
uint32_t* d_twiddle1_inv = nullptr;
uint32_t* d_twiddle2_fwd = nullptr;
uint32_t* d_twiddle2_inv = nullptr;
unsigned int* d_data = nullptr;
uint32_t* d_ntt_buf1 = nullptr;
uint32_t* d_ntt_buf2 = nullptr;
uint32_t* d_ntt_tmp = nullptr;

int g_M = 0;

long long* d_genericCarryBuffer = nullptr;
int g_genericMaxChunks = 0;

// -------------------------------------------------------------------------
// Core Memory Management
// -------------------------------------------------------------------------
void CleanupGPU_Internal() {
    if (d_twiddle1_fwd) { cudaFree(d_twiddle1_fwd); d_twiddle1_fwd = nullptr; }
    if (d_twiddle1_inv) { cudaFree(d_twiddle1_inv); d_twiddle1_inv = nullptr; }
    if (d_twiddle2_fwd) { cudaFree(d_twiddle2_fwd); d_twiddle2_fwd = nullptr; }
    if (d_twiddle2_inv) { cudaFree(d_twiddle2_inv); d_twiddle2_inv = nullptr; }
    if (d_ntt_buf1) { cudaFree(d_ntt_buf1); d_ntt_buf1 = nullptr; }
    if (d_ntt_buf2) { cudaFree(d_ntt_buf2); d_ntt_buf2 = nullptr; }
    if (d_ntt_tmp) { cudaFree(d_ntt_tmp); d_ntt_tmp = nullptr; }
    if (d_data) { cudaFree(d_data); d_data = nullptr; }
    g_M = 0;
}

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

bool InitializeGPU_Internal(int E) {
    CleanupGPU_Internal();

    int requiredElements = (4 * E + 2 + 15) / 16;
    g_M = NextPowerOf2(requiredElements);
    
    printf("Initializing NTT: E=%d, Required Radix-16 Elements=%d, NTT Size=%d\n", E, requiredElements, g_M);
    
    cudaMalloc((void**)&d_twiddle1_fwd, sizeof(uint32_t) * g_M);
    cudaMalloc((void**)&d_twiddle1_inv, sizeof(uint32_t) * g_M);
    cudaMalloc((void**)&d_twiddle2_fwd, sizeof(uint32_t) * g_M);
    cudaMalloc((void**)&d_twiddle2_inv, sizeof(uint32_t) * g_M);
    
    uint32_t* h_t1_fwd = new uint32_t[g_M];
    uint32_t* h_t1_inv = new uint32_t[g_M];
    uint32_t* h_t2_fwd = new uint32_t[g_M];
    uint32_t* h_t2_inv = new uint32_t[g_M];
    
    // Calculate principal root of unity for length g_M
    uint32_t p1 = 4253024257;
    uint32_t p2 = 4246732801;
    uint32_t w1 = CpuModPow(5, (p1 - 1) / g_M, p1);
    uint32_t w2 = CpuModPow(2, (p2 - 1) / g_M, p2);
    
    uint32_t iw1 = CpuModPow(w1, p1 - 2, p1);
    uint32_t iw2 = CpuModPow(w2, p2 - 2, p2);
    
    uint64_t curr_w1 = 1, curr_iw1 = 1;
    uint64_t curr_w2 = 1, curr_iw2 = 1;
    
    for (int i = 0; i < g_M; i++) {
        h_t1_fwd[i] = (uint32_t)curr_w1;
        h_t1_inv[i] = (uint32_t)curr_iw1;
        h_t2_fwd[i] = (uint32_t)curr_w2;
        h_t2_inv[i] = (uint32_t)curr_iw2;
        
        curr_w1 = (curr_w1 * w1) % p1;
        curr_iw1 = (curr_iw1 * iw1) % p1;
        curr_w2 = (curr_w2 * w2) % p2;
        curr_iw2 = (curr_iw2 * iw2) % p2;
    }
    
    cudaMemcpy(d_twiddle1_fwd, h_t1_fwd, sizeof(uint32_t) * g_M, cudaMemcpyHostToDevice);
    cudaMemcpy(d_twiddle1_inv, h_t1_inv, sizeof(uint32_t) * g_M, cudaMemcpyHostToDevice);
    cudaMemcpy(d_twiddle2_fwd, h_t2_fwd, sizeof(uint32_t) * g_M, cudaMemcpyHostToDevice);
    cudaMemcpy(d_twiddle2_inv, h_t2_inv, sizeof(uint32_t) * g_M, cudaMemcpyHostToDevice);
    
    delete[] h_t1_fwd; delete[] h_t1_inv;
    delete[] h_t2_fwd; delete[] h_t2_inv;

    cudaMalloc((void**)&d_ntt_buf1, sizeof(uint32_t) * g_M);
    cudaMalloc((void**)&d_ntt_buf2, sizeof(uint32_t) * g_M);
    cudaMalloc((void**)&d_ntt_tmp, sizeof(uint32_t) * g_M);

    int maxUintWords = (g_M * 16 + 31) / 32;
    cudaMalloc((void**)&d_data, sizeof(unsigned int) * maxUintWords);
    
    return true;
}

// -------------------------------------------------------------------------
// Exported Initialization API
// -------------------------------------------------------------------------
extern "C" {
    EXPORT_API bool GpuBigInt_Init(int maxWords) {
        int simulatedE = (maxWords * 32) / 2; 
        bool res = InitializeGPU_Internal(simulatedE);
        
        int chunkSize = 256;
        g_genericMaxChunks = (maxWords + chunkSize - 1) / chunkSize;
        cudaMalloc((void**)&d_genericCarryBuffer, sizeof(long long) * (g_genericMaxChunks + 1));
        
        return res;
    }
    
    EXPORT_API void GpuBigInt_Cleanup() { 
        CleanupGPU_Internal(); 
        if (d_genericCarryBuffer) { cudaFree(d_genericCarryBuffer); d_genericCarryBuffer = nullptr; }
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
    EXPORT_API bool GpuBigInt_ExecuteIL(int E, const char* ilScript, unsigned int* out_data) {
        int numWords = (2 * E + 1 + 31) / 32;
        int outNumWords = numWords * 2 + 16; 
        
        if (!InitializeGPU_Internal(E)) return false;

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
                    GpuBigInt_Square(vars[dest], numWords + 1, stream);
                }
                else if (op == "SHR") {
                    std::string dest, src; int bits; iss >> dest >> src >> bits;
                    GpuBigInt_ShiftRight(vars[src], bits, vars[dest], outNumWords, stream);
                }
                else if (op == "SHL") {
                    std::string dest, src; int bits; iss >> dest >> src >> bits;
                    GpuBigInt_ShiftLeft(vars[src], bits, vars[dest], outNumWords, stream);
                }
                else if (op == "AND") {
                    std::string dest, src, mask; iss >> dest >> src >> mask;
                    GpuBigInt_BitwiseAnd(vars[src], vars[mask], vars[dest], outNumWords, stream);
                }
                else if (op == "ADD") {
                    std::string dest, a, b; iss >> dest >> a >> b;
                    GpuBigInt_Add(vars[a], vars[b], vars[dest], outNumWords, stream);
                }
                else if (op == "SUB") {
                    std::string dest, a, b; iss >> dest >> a >> b;
                    GpuBigInt_Sub(vars[a], vars[b], vars[dest], outNumWords, stream);
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
