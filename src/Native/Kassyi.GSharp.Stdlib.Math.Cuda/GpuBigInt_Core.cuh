#pragma once
#include "GpuBigInt.h"
#include <cuda_runtime.h>
#include <cufft.h>
#include <stdio.h>
#include <math.h>

// -------------------------------------------------------------------------
// Global State & Context
// -------------------------------------------------------------------------
struct GpuWorkspace {
    uint32_t* d_twiddle1_fwd;
    uint32_t* d_twiddle1_inv;
    uint32_t* d_twiddle2_fwd;
    uint32_t* d_twiddle2_inv;
    unsigned int* d_data;
    uint32_t* d_ntt_buf1;
    uint32_t* d_ntt_buf2;
    uint32_t* d_ntt_tmp;

    int g_M;          // NTT Size (must be a power of 2 for optimal performance)

    long long* d_genericCarryBuffer;
    int g_genericMaxChunks;
};

// -------------------------------------------------------------------------
// Utility Functions
// -------------------------------------------------------------------------
inline int NextPowerOf2(int n) {
    n--;
    n |= n >> 1;
    n |= n >> 2;
    n |= n >> 4;
    n |= n >> 8;
    n |= n >> 16;
    n++;
    return n;
}

// -------------------------------------------------------------------------
// Core Initialization & Cleanup
// -------------------------------------------------------------------------
void CleanupGPU_Internal();
bool InitializeGPU_Internal(int E);
