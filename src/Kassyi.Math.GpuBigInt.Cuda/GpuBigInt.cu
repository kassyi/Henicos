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

// -------------------------------------------------------------------------
// Global State Implementation
// -------------------------------------------------------------------------
cufftHandle planR2C = 0;
cufftHandle planC2R = 0;
double* d_real = nullptr;
cufftDoubleComplex* d_complex = nullptr;
unsigned int* d_data = nullptr;
void* d_cufftWorkspace = nullptr;

int g_M = 0;
int g_ComplexM = 0;

long long* d_genericCarryBuffer = nullptr;
int g_genericMaxChunks = 0;

// -------------------------------------------------------------------------
// Core Memory Management
// -------------------------------------------------------------------------
void CleanupGPU_Internal() {
    if (d_real) { cudaFree(d_real); d_real = nullptr; }
    if (d_complex) { cudaFree(d_complex); d_complex = nullptr; }
    if (d_data) { cudaFree(d_data); d_data = nullptr; }
    if (g_M > 0) {
        if (d_cufftWorkspace) { cudaFree(d_cufftWorkspace); d_cufftWorkspace = nullptr; }
        if (planR2C) { cufftDestroy(planR2C); planR2C = 0; }
        if (planC2R) { cufftDestroy(planC2R); planC2R = 0; }
        g_M = 0;
    }
}

bool InitializeGPU_Internal(int E) {
    CleanupGPU_Internal();

    int requiredElements = (4 * E + 2 + 15) / 16;
    g_M = NextPowerOf2(requiredElements);
    g_ComplexM = g_M / 2 + 1;
    
    printf("Initializing cuFFT: E=%d, Required Radix-16 Elements=%d, FFT Size=%d\n", E, requiredElements, g_M);
    
    cufftCreate(&planR2C);
    cufftSetAutoAllocation(planR2C, 0);
    size_t workSizeR2C;
    cufftMakePlan1d(planR2C, g_M, CUFFT_D2Z, 1, &workSizeR2C);
    
    cufftCreate(&planC2R);
    cufftSetAutoAllocation(planC2R, 0);
    size_t workSizeC2R;
    cufftMakePlan1d(planC2R, g_M, CUFFT_Z2D, 1, &workSizeC2R);
    
    size_t maxWorkSize = (workSizeR2C > workSizeC2R) ? workSizeR2C : workSizeC2R;
    cudaMalloc(&d_cufftWorkspace, maxWorkSize);
    cufftSetWorkArea(planR2C, d_cufftWorkspace);
    cufftSetWorkArea(planC2R, d_cufftWorkspace);
    
    cudaMalloc((void**)&d_real, sizeof(double) * g_M);
    cudaMalloc((void**)&d_complex, sizeof(cufftDoubleComplex) * g_ComplexM);
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
            
            cufftSetStream(planR2C, stream);
            cufftSetStream(planC2R, stream);
            
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
        cufftSetStream(planR2C, 0);
        cufftSetStream(planC2R, 0);

        return true;
    }
}
