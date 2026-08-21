#pragma once
#include "GpuBigInt_Core.cuh"

// -------------------------------------------------------------------------
// FFT Convolution Kernels
// -------------------------------------------------------------------------

__global__ void UnpackKernel16(const unsigned int* A, double* B, int M, int numWords) {
    int k = blockIdx.x * blockDim.x + threadIdx.x;
    if (k < M) {
        double val = 0.0;
        int wordIdx = k / 2;
        if (wordIdx < numWords) {
             unsigned int w = A[wordIdx];
             if (k % 2 == 0) {
                 val = (double)(w & 0xFFFF);
             } else {
                 val = (double)(w >> 16);
             }
        }
        B[k] = val;
    }
}

__global__ void PointwiseSquareKernel(cufftDoubleComplex* data, int numElements, double scale) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < numElements) {
        cufftDoubleComplex v = data[i];
        cufftDoubleComplex res;
        res.x = (v.x * v.x - v.y * v.y) * scale;
        res.y = (v.x * v.y + v.y * v.x) * scale;
        data[i] = res;
    }
}

__global__ void ResolveCarryAndPackKernel16(double* d_real, unsigned int* out_uint, int M, int outNumWords) {
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    if (tid == 0) {
        for(int i=0; i<outNumWords; i++) out_uint[i] = 0;
        unsigned long long carry = 0;
        for (int k = 0; k < M; k++) {
            double raw = d_real[k];
            unsigned long long val = (unsigned long long)(raw + 0.5) + carry;
            unsigned int lower16 = (unsigned int)(val & 0xFFFF);
            carry = val >> 16;
            int wordIdx = k / 2;
            if (wordIdx < outNumWords) {
                if (k % 2 == 0) out_uint[wordIdx] = lower16;
                else out_uint[wordIdx] |= (lower16 << 16);
            }
        }
        int k = M;
        while (carry > 0) {
            int wordIdx = k / 2;
            if (wordIdx >= outNumWords) break;
            unsigned int lower16 = (unsigned int)(carry & 0xFFFF);
            carry >>= 16;
            if (k % 2 == 0) out_uint[wordIdx] = lower16;
            else out_uint[wordIdx] |= (lower16 << 16);
            k++;
        }
    }
}

// -------------------------------------------------------------------------
// Addition / Subtraction Kernels
// -------------------------------------------------------------------------

__global__ void ChunkAddKernel(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, long long* carryBuffer, int numWords, int chunkSize) {
    int chunkIdx = blockIdx.x * blockDim.x + threadIdx.x;
    int start = chunkIdx * chunkSize;
    if (start >= numWords) return;
    int end = start + chunkSize;
    if (end > numWords) end = numWords;
    
    unsigned long long localCarry = 0;
    for (int i = start; i < end; i++) {
        unsigned long long current = (unsigned long long)d_a[i] + (unsigned long long)d_b[i] + localCarry;
        d_result[i] = (unsigned int)(current & 0xFFFFFFFF);
        localCarry = current >> 32;
    }
    carryBuffer[chunkIdx + 1] = localCarry;
}

__global__ void ApplyAddCarryKernel(unsigned int* d_result, long long* carryBuffer, int numChunks, int numWords, int chunkSize) {
    if (threadIdx.x == 0 && blockIdx.x == 0) {
        unsigned long long rippleCarry = 0;
        for (int chunkIdx = 0; chunkIdx < numChunks; chunkIdx++) {
            int start = chunkIdx * chunkSize;
            int end = start + chunkSize;
            if (end > numWords) end = numWords;
            rippleCarry += carryBuffer[chunkIdx];
            if (rippleCarry == 0) continue;
            for (int i = start; i < end; i++) {
                unsigned long long current = (unsigned long long)d_result[i] + rippleCarry;
                d_result[i] = (unsigned int)(current & 0xFFFFFFFF);
                rippleCarry = current >> 32;
                if (rippleCarry == 0) break;
            }
        }
    }
}

__global__ void ChunkSubKernel(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, long long* carryBuffer, int numWords, int chunkSize) {
    int chunkIdx = blockIdx.x * blockDim.x + threadIdx.x;
    int start = chunkIdx * chunkSize;
    if (start >= numWords) return;
    int end = start + chunkSize;
    if (end > numWords) end = numWords;
    
    long long localBorrow = 0;
    for (int i = start; i < end; i++) {
        long long current = (long long)d_a[i] - (long long)d_b[i] - localBorrow;
        if (current < 0) {
            current += 0x100000000LL;
            localBorrow = 1;
        } else {
            localBorrow = 0;
        }
        d_result[i] = (unsigned int)(current & 0xFFFFFFFF);
    }
    carryBuffer[chunkIdx + 1] = localBorrow;
}

__global__ void ApplySubBorrowKernel(unsigned int* d_result, long long* carryBuffer, int numChunks, int numWords, int chunkSize) {
    if (threadIdx.x == 0 && blockIdx.x == 0) {
        long long rippleBorrow = 0;
        for (int chunkIdx = 0; chunkIdx < numChunks; chunkIdx++) {
            int start = chunkIdx * chunkSize;
            int end = start + chunkSize;
            if (end > numWords) end = numWords;
            rippleBorrow += carryBuffer[chunkIdx];
            if (rippleBorrow == 0) continue;
            for (int i = start; i < end; i++) {
                long long current = (long long)d_result[i] - rippleBorrow;
                if (current < 0) {
                    current += 0x100000000LL;
                    rippleBorrow = 1;
                } else {
                    rippleBorrow = 0;
                }
                d_result[i] = (unsigned int)(current & 0xFFFFFFFF);
                if (rippleBorrow == 0) break;
            }
        }
    }
}

// -------------------------------------------------------------------------
// Bitwise and Shift Kernels
// -------------------------------------------------------------------------

__global__ void ShiftLeftKernel(const unsigned int* d_a, int wordShift, int bitShift, unsigned int* d_result, int numWords) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < numWords) {
        int srcIdx0 = i - wordShift;
        int srcIdx1 = srcIdx0 - 1;
        unsigned int w0 = (srcIdx0 >= 0 && srcIdx0 < numWords) ? d_a[srcIdx0] : 0;
        unsigned int w1 = (srcIdx1 >= 0 && srcIdx1 < numWords) ? d_a[srcIdx1] : 0;
        if (bitShift == 0) d_result[i] = w0;
        else d_result[i] = (w0 << bitShift) | (w1 >> (32 - bitShift));
    }
}

__global__ void ShiftRightKernel(const unsigned int* d_a, int wordShift, int bitShift, unsigned int* d_result, int numWords) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < numWords) {
        int srcIdx0 = i + wordShift;
        int srcIdx1 = srcIdx0 + 1;
        unsigned int w0 = (srcIdx0 >= 0 && srcIdx0 < numWords) ? d_a[srcIdx0] : 0;
        unsigned int w1 = (srcIdx1 >= 0 && srcIdx1 < numWords) ? d_a[srcIdx1] : 0;
        if (bitShift == 0) d_result[i] = w0;
        else d_result[i] = (w0 >> bitShift) | (w1 << (32 - bitShift));
    }
}

__global__ void BitwiseAndKernel(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < numWords) {
        d_result[i] = d_a[i] & d_b[i];
    }
}
