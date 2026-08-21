#pragma once
#include "GpuBigInt_Core.cuh"

// -------------------------------------------------------------------------
// FFT Convolution Kernels
// -------------------------------------------------------------------------

#include "GpuBigInt_NTT.cuh"

// -------------------------------------------------------------------------
// NTT Convolution Kernels
// -------------------------------------------------------------------------

__global__ void Kernel_UnpackAndBitReverse16(const unsigned int* d_src, uint32_t* d_dst1, uint32_t* d_dst2, int N, int log2N, int numWords) {
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    if (tid < N) {
        int rev = 0;
        for (int i = 0; i < log2N; i++) {
            rev = (rev << 1) | ((tid >> i) & 1);
        }
        
        uint32_t val = 0;
        int wordIdx = tid / 2;
        if (wordIdx < numWords) {
             unsigned int w = d_src[wordIdx];
             if (tid % 2 == 0) {
                 val = (w & 0xFFFF);
             } else {
                 val = (w >> 16);
             }
        }
        d_dst1[rev] = val;
        d_dst2[rev] = val;
    }
}

__global__ void Kernel_BitReverse(const uint32_t* d_src, uint32_t* d_dst, int N, int log2N) {
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    if (tid < N) {
        int rev = 0;
        for (int i = 0; i < log2N; i++) {
            rev = (rev << 1) | ((tid >> i) & 1);
        }
        d_dst[rev] = d_src[tid];
    }
}

__global__ void Kernel_NTT_Stage(uint32_t* d_data, const uint32_t* d_twiddle, int N, int len, uint32_t p, uint64_t m) {
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    if (tid >= N / 2) return;
    
    int block = tid / (len / 2);
    int idx = tid % (len / 2);
    
    int pos = block * len + idx;
    
    // Twiddle factor index
    int twiddle_idx = idx * (N / len);
    uint32_t w = d_twiddle[twiddle_idx];
    
    uint32_t u = d_data[pos];
    uint32_t v = ModMulBarrett(d_data[pos + len / 2], w, p, m);
    
    d_data[pos] = ModAdd(u, v, p);
    d_data[pos + len / 2] = ModSub(u, v, p);
}

__global__ void Kernel_NTT_PointwiseSquare(uint32_t* d_data1, uint32_t* d_data2, int N) {
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    if (tid < N) {
        d_data1[tid] = ModMulBarrett(d_data1[tid], d_data1[tid], NTT_P1, NTT_M1);
        d_data2[tid] = ModMulBarrett(d_data2[tid], d_data2[tid], NTT_P2, NTT_M2);
    }
}

__global__ void Kernel_CRT_ResolveCarry(const uint32_t* d_res1, const uint32_t* d_res2, unsigned int* out_uint, int N, int outNumWords) {
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    // We must run this sequentially due to ripple carry for now, 
    // or we can use a parallel prefix sum for carries later.
    // For now, doing it sequentially on thread 0 to ensure correctness.
    if (tid == 0) {
        for(int i=0; i<outNumWords; i++) out_uint[i] = 0;
        
        uint64_t carry = 0;
        
        // p1 = 2013265921, p2 = 2113929217
        // M = p1 * p2 = 4255904944883302417
        uint64_t p1 = NTT_P1;
        uint64_t p2 = NTT_P2;
        uint64_t p1_inv_p2 = 21; // invMod(p1, p2)
        
        // Calculate invN for inverse NTT
        uint32_t invN1 = ModPow(N, NTT_P1 - 2, NTT_P1, NTT_M1);
        uint32_t invN2 = ModPow(N, NTT_P2 - 2, NTT_P2, NTT_M2);
        
        for (int k = 0; k < N; k++) {
            // Apply 1/N scaling
            uint64_t v1 = ModMulBarrett(d_res1[k], invN1, NTT_P1, NTT_M1);
            uint64_t v2 = ModMulBarrett(d_res2[k], invN2, NTT_P2, NTT_M2);
            
            // Garner's algorithm for CRT
            // x = v1 + p1 * [ (v2 - v1) * (p1^-1 mod p2) mod p2 ]
            uint64_t diff = v2 >= v1 ? v2 - v1 : v2 + p2 - v1;
            uint64_t h = (diff * p1_inv_p2) % p2;
            uint64_t val = v1 + p1 * h + carry;
            
            unsigned int lower16 = (unsigned int)(val & 0xFFFF);
            carry = val >> 16;
            
            int wordIdx = k / 2;
            if (wordIdx < outNumWords) {
                if (k % 2 == 0) out_uint[wordIdx] = lower16;
                else out_uint[wordIdx] |= (lower16 << 16);
            }
        }
        
        int k = N;
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
