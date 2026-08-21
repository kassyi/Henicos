#pragma once
#include "GpuBigInt.h"
#include <cuda_runtime.h>
#include <cufft.h>
#include <stdio.h>
#include <math.h>

// -------------------------------------------------------------------------
// Global State & Context
// -------------------------------------------------------------------------
extern cufftHandle planR2C;
extern cufftHandle planC2R;
extern double* d_real;
extern cufftDoubleComplex* d_complex;
extern unsigned int* d_data;
extern void* d_cufftWorkspace;

extern int g_M;          // FFT Size (must be a power of 2 for optimal performance)
extern int g_ComplexM;   // Size of the complex array after Real-to-Complex FFT (M/2 + 1)

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
