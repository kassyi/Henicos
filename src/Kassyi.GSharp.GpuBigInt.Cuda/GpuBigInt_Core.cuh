#pragma once
#include "GpuBigInt.h"
#include <cuda_runtime.h>
#include <cufft.h>
#include <stdio.h>
#include <math.h>

// -------------------------------------------------------------------------
// Global State & Context
// -------------------------------------------------------------------------
extern uint32_t* d_twiddle1_fwd;
extern uint32_t* d_twiddle1_inv;
extern uint32_t* d_twiddle2_fwd;
extern uint32_t* d_twiddle2_inv;
extern unsigned int* d_data;
extern uint32_t* d_ntt_buf1;
extern uint32_t* d_ntt_buf2;
extern uint32_t* d_ntt_tmp;

extern int g_M;          // NTT Size (must be a power of 2 for optimal performance)

extern long long* d_genericCarryBuffer;
extern int g_genericMaxChunks;

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
