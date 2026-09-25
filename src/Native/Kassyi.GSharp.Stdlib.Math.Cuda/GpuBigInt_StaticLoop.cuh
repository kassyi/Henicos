#pragma once
#define CCCL_IGNORE_MSVC_TRADITIONAL_PREPROCESSOR_WARNING
#define CCCL_IGNORE_DEPRECATED_CPP_DIALECT
#include <cooperative_groups.h>
#include "GpuBigInt_Core.cuh"
#include "GpuBigInt_Kernels.cuh"

namespace cg = cooperative_groups;

// -------------------------------------------------------------------------
// Phase 9: Static Device-Side Operations
// -------------------------------------------------------------------------

__device__ void DeviceUnpack16(const unsigned int* d_src, uint32_t* d_dst1, uint32_t* d_dst2, int N, int numWords) {
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    for (int i = tid; i < N; i += gridDim.x * blockDim.x) {
        uint32_t val = 0;
        int wordIdx = i / 2;
        if (wordIdx < numWords) {
            unsigned int w = d_src[wordIdx];
            if (i % 2 == 0) val = (w & 0xFFFF);
            else val = (w >> 16);
        }
        d_dst1[i] = val;
        d_dst2[i] = val;
    }
}

__device__ void DeviceTranspose32x32(const uint32_t* idata, uint32_t* odata, int width, int height) {
    extern __shared__ uint32_t s_mem[]; // Size: at least 32 * 33 * 4 = 4224 bytes
    
    int gridX = (width + 31) / 32;
    int gridY = (height + 31) / 32;
    if (blockIdx.x >= gridX * gridY) return;
    
    int bx = blockIdx.x % gridX;
    int by = blockIdx.x / gridX;
    
    if (threadIdx.x >= 256) return;
    int tx = threadIdx.x % 32;
    int ty = threadIdx.x / 32;

    int xIndex = bx * 32 + tx;
    int yIndex = by * 32 + ty;

    for (int i = 0; i < 32; i += 8) {
        if (xIndex < width && yIndex + i < height) {
            s_mem[(ty + i) * 33 + tx] = idata[(yIndex + i) * width + xIndex];
        }
    }
    
    __syncthreads();

    xIndex = by * 32 + tx;
    yIndex = bx * 32 + ty;

    for (int i = 0; i < 32; i += 8) {
        if (xIndex < height && yIndex + i < width) {
            odata[(yIndex + i) * height + xIndex] = s_mem[tx * 33 + (ty + i)];
        }
    }
}

__device__ void Device_NTT_Shared_Row(uint32_t* d_data, const uint32_t* d_twiddle, int N, int L, uint32_t p, uint64_t m, bool applyTwiddleBefore, bool applyTwiddleAfter, int max_blocks) {
    if (blockIdx.x >= max_blocks) return;
    int threads_needed = L / 2;
    if (threadIdx.x >= threads_needed) return;
    
    extern __shared__ uint32_t s_row[];
    
    int row = blockIdx.x;
    int tid = threadIdx.x;
    int offset = row * L;
    
    int log2L = 0;
    while ((1 << log2L) < L) log2L++;
    
    int rev_tid1 = 0, rev_tid2 = 0;
    for (int i = 0; i < log2L; i++) {
        rev_tid1 = (rev_tid1 << 1) | ((tid >> i) & 1);
        rev_tid2 = (rev_tid2 << 1) | (((tid + L/2) >> i) & 1);
    }
    
    uint32_t val1 = d_data[offset + tid];
    uint32_t val2 = d_data[offset + tid + L/2];
    
    if (applyTwiddleBefore) {
        uint64_t k1 = ((uint64_t)row * tid) % N;
        uint64_t k2 = ((uint64_t)row * (tid + L/2)) % N;
        val1 = ModMulBarrett(val1, d_twiddle[k1], p, m);
        val2 = ModMulBarrett(val2, d_twiddle[k2], p, m);
    }
    
    s_row[rev_tid1] = val1;
    s_row[rev_tid2] = val2;
    __syncthreads();
    
    for (int s = 0; s < log2L; s++) {
        int len = 2 << s;
        int half_len = len / 2;
        int block_idx = tid / half_len;
        int idx = tid % half_len;
        int pos = block_idx * len + idx;
        
        int twiddle_idx = idx * (N / len);
        uint32_t w = d_twiddle[twiddle_idx];
        
        uint32_t u = s_row[pos];
        uint32_t v = ModMulBarrett(s_row[pos + half_len], w, p, m);
        
        s_row[pos] = ModAdd(u, v, p);
        s_row[pos + half_len] = ModSub(u, v, p);
        __syncthreads();
    }
    
    if (applyTwiddleAfter) {
        uint64_t k1 = ((uint64_t)row * tid) % N;
        uint64_t k2 = ((uint64_t)row * (tid + L/2)) % N;
        s_row[tid] = ModMulBarrett(s_row[tid], d_twiddle[k1], p, m);
        s_row[tid + L/2] = ModMulBarrett(s_row[tid + L/2], d_twiddle[k2], p, m);
    }
    
    d_data[offset + tid] = s_row[tid];
    d_data[offset + tid + L/2] = s_row[tid + L/2];
}

__device__ void Device_NTT_PointwiseSquare(uint32_t* d_data1, uint32_t* d_data2, int N) {
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    for (int i = tid; i < N; i += gridDim.x * blockDim.x) {
        d_data1[i] = ModMulBarrett(d_data1[i], d_data1[i], NTT_P1, NTT_M1);
        d_data2[i] = ModMulBarrett(d_data2[i], d_data2[i], NTT_P2, NTT_M2);
    }
}

__device__ void Device_CRT_ParallelAtomic(const uint32_t* d_res1, const uint32_t* d_res2, unsigned int* out_uint, int N, int outNumWords) {
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    for (int k = tid; k < N; k += gridDim.x * blockDim.x) {
        uint64_t p1 = NTT_P1;
        uint64_t p2 = NTT_P2;
        uint64_t p1_inv_p2 = 4246732126ULL;
        
        uint32_t invN1 = ModPow(N, NTT_P1 - 2, NTT_P1, NTT_M1);
        uint32_t invN2 = ModPow(N, NTT_P2 - 2, NTT_P2, NTT_M2);
        
        uint64_t v1 = ModMulBarrett(d_res1[k], invN1, NTT_P1, NTT_M1);
        uint64_t v2 = ModMulBarrett(d_res2[k], invN2, NTT_P2, NTT_M2);
        
        uint64_t diff = v2 >= v1 ? v2 - v1 : v2 + p2 - v1;
        uint64_t h = (diff * p1_inv_p2) % p2;
        uint64_t val = v1 + p1 * h;
        
        if (val > 0) {
            int wordIdx = k / 2;
            int bitOffset = (k % 2) * 16;
            
            uint32_t low32 = 0;
            uint32_t mid32 = 0;
            uint32_t high32 = 0;
            
            if (bitOffset == 0) {
                low32 = (uint32_t)(val & 0xFFFFFFFF);
                mid32 = (uint32_t)(val >> 32);
            } else {
                low32 = (uint32_t)((val << 16) & 0xFFFFFFFF);
                mid32 = (uint32_t)((val >> 16) & 0xFFFFFFFF);
                high32 = (uint32_t)(val >> 48);
            }
            
            if (low32) AtomicAddWithCarry(out_uint, wordIdx, low32, outNumWords);
            if (mid32) AtomicAddWithCarry(out_uint, wordIdx + 1, mid32, outNumWords);
            if (high32) AtomicAddWithCarry(out_uint, wordIdx + 2, high32, outNumWords);
        }
    }
}

__device__ void Device_MultiplySquare(unsigned int* data, int numWords, cg::grid_group& grid,
    int M, uint32_t* ntt_buf1, uint32_t* ntt_buf2, uint32_t* ntt_tmp,
    const uint32_t* twiddle1_fwd, const uint32_t* twiddle2_fwd,
    const uint32_t* twiddle1_inv, const uint32_t* twiddle2_inv) {
    
    DeviceUnpack16(data, ntt_buf1, ntt_buf2, M, numWords);
    grid.sync();
    
    int N2 = 1;
    while (N2 * N2 < M) N2 *= 2;
    int N1 = M / N2;
    
    auto do_forward_ntt = [&](uint32_t* buf, const uint32_t* twiddle, uint32_t p, uint64_t m) {
        if (M <= 512) {
            Device_NTT_Shared_Row(buf, twiddle, M, M, p, m, false, false, 1);
            grid.sync();
        } else {
            DeviceTranspose32x32(buf, ntt_tmp, N2, N1);
            grid.sync();
            Device_NTT_Shared_Row(ntt_tmp, twiddle, M, N1, p, m, false, true, N2);
            grid.sync();
            DeviceTranspose32x32(ntt_tmp, buf, N1, N2);
            grid.sync();
            Device_NTT_Shared_Row(buf, twiddle, M, N2, p, m, false, false, N1);
            grid.sync();
        }
    };
    
    do_forward_ntt(ntt_buf1, twiddle1_fwd, NTT_P1, NTT_M1);
    do_forward_ntt(ntt_buf2, twiddle2_fwd, NTT_P2, NTT_M2);
    
    Device_NTT_PointwiseSquare(ntt_buf1, ntt_buf2, M);
    grid.sync();
    
    auto do_inverse_ntt = [&](uint32_t* buf, const uint32_t* twiddle, uint32_t p, uint64_t m) {
        if (M <= 512) {
            Device_NTT_Shared_Row(buf, twiddle, M, M, p, m, false, false, 1);
            grid.sync();
        } else {
            Device_NTT_Shared_Row(buf, twiddle, M, N2, p, m, false, true, N1);
            grid.sync();
            DeviceTranspose32x32(buf, ntt_tmp, N2, N1);
            grid.sync();
            Device_NTT_Shared_Row(ntt_tmp, twiddle, M, N1, p, m, false, false, N2);
            grid.sync();
            DeviceTranspose32x32(ntt_tmp, buf, N1, N2);
            grid.sync();
        }
    };
    
    do_inverse_ntt(ntt_buf1, twiddle1_inv, NTT_P1, NTT_M1);
    do_inverse_ntt(ntt_buf2, twiddle2_inv, NTT_P2, NTT_M2);
    
    int actualOutNumWords = numWords * 2 + 16;
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    for (int i = tid; i < actualOutNumWords; i += gridDim.x * blockDim.x) {
        data[i] = 0;
    }
    grid.sync();
    
    Device_CRT_ParallelAtomic(ntt_buf1, ntt_buf2, data, M, numWords * 2);
    grid.sync();
}

// -------------------------------------------------------------------------
// Device-Side BigInt Arithmetic
// -------------------------------------------------------------------------

__device__ void Device_Add(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords, long long* carryBuffer, cg::grid_group& grid) {
    if (blockIdx.x == 0 && threadIdx.x == 0) carryBuffer[0] = 0;
    grid.sync();
    
    int chunkSize = 256;
    int chunkIdx = blockIdx.x;
    int start = chunkIdx * chunkSize;
    
    if (start < numWords && threadIdx.x < chunkSize) {
        int end = start + chunkSize;
        if (end > numWords) end = numWords;
        
        if (threadIdx.x == 0) {
            unsigned long long localCarry = 0;
            for (int i = start; i < end; i++) {
                unsigned long long current = (unsigned long long)d_a[i] + (unsigned long long)d_b[i] + localCarry;
                d_result[i] = (unsigned int)(current & 0xFFFFFFFF);
                localCarry = current >> 32;
            }
            carryBuffer[chunkIdx + 1] = localCarry;
        }
    }
    grid.sync();
    
    if (blockIdx.x == 0 && threadIdx.x == 0) {
        int numChunks = (numWords + chunkSize - 1) / chunkSize;
        unsigned long long rippleCarry = 0;
        for (int cIdx = 0; cIdx < numChunks; cIdx++) {
            int s = cIdx * chunkSize;
            int e = s + chunkSize;
            if (e > numWords) e = numWords;
            rippleCarry += carryBuffer[cIdx];
            if (rippleCarry != 0) {
                for (int i = s; i < e; i++) {
                    unsigned long long current = (unsigned long long)d_result[i] + rippleCarry;
                    d_result[i] = (unsigned int)(current & 0xFFFFFFFF);
                    rippleCarry = current >> 32;
                    if (rippleCarry == 0) break;
                }
            }
        }
    }
    grid.sync();
}

__device__ void Device_Sub(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords, long long* carryBuffer, cg::grid_group& grid) {
    if (blockIdx.x == 0 && threadIdx.x == 0) carryBuffer[0] = 0;
    grid.sync();
    
    int chunkSize = 256;
    int chunkIdx = blockIdx.x;
    int start = chunkIdx * chunkSize;
    
    if (start < numWords && threadIdx.x < chunkSize) {
        int end = start + chunkSize;
        if (end > numWords) end = numWords;
        
        if (threadIdx.x == 0) {
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
    }
    grid.sync();
    
    if (blockIdx.x == 0 && threadIdx.x == 0) {
        int numChunks = (numWords + chunkSize - 1) / chunkSize;
        long long rippleBorrow = 0;
        for (int cIdx = 0; cIdx < numChunks; cIdx++) {
            int s = cIdx * chunkSize;
            int e = s + chunkSize;
            if (e > numWords) e = numWords;
            rippleBorrow += carryBuffer[cIdx];
            if (rippleBorrow != 0) {
                for (int i = s; i < e; i++) {
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
    grid.sync();
}

__device__ void Device_ShiftLeft(const unsigned int* d_a, int shiftBits, unsigned int* d_result, int numWords, cg::grid_group& grid) {
    int wordShift = shiftBits / 32;
    int bitShift = shiftBits % 32;
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < numWords) {
        int srcIdx0 = i - wordShift;
        int srcIdx1 = srcIdx0 - 1;
        unsigned int w0 = (srcIdx0 >= 0 && srcIdx0 < numWords) ? d_a[srcIdx0] : 0;
        unsigned int w1 = (srcIdx1 >= 0 && srcIdx1 < numWords) ? d_a[srcIdx1] : 0;
        if (bitShift == 0) d_result[i] = w0;
        else d_result[i] = (w0 << bitShift) | (w1 >> (32 - bitShift));
    }
    grid.sync();
}

__device__ void Device_ShiftRight(const unsigned int* d_a, int shiftBits, unsigned int* d_result, int numWords, cg::grid_group& grid) {
    int wordShift = shiftBits / 32;
    int bitShift = shiftBits % 32;
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < numWords) {
        int srcIdx0 = i + wordShift;
        int srcIdx1 = srcIdx0 + 1;
        unsigned int w0 = (srcIdx0 >= 0 && srcIdx0 < numWords) ? d_a[srcIdx0] : 0;
        unsigned int w1 = (srcIdx1 >= 0 && srcIdx1 < numWords) ? d_a[srcIdx1] : 0;
        if (bitShift == 0) d_result[i] = w0;
        else d_result[i] = (w0 >> bitShift) | (w1 << (32 - bitShift));
    }
    grid.sync();
}

__device__ void Device_BitwiseAnd(const unsigned int* d_a, const unsigned int* d_b, unsigned int* d_result, int numWords, cg::grid_group& grid) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < numWords) {
        d_result[i] = d_a[i] & d_b[i];
    }
    grid.sync();
}

__device__ void Device_Copy(const unsigned int* d_src, unsigned int* d_dst, int numWords, cg::grid_group& grid) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    for (int tid = i; tid < numWords; tid += gridDim.x * blockDim.x) {
        d_dst[tid] = d_src[tid];
    }
    grid.sync();
}

__device__ __forceinline__ void Device_FermatReduceStep(
    unsigned int* T, unsigned int* Q, unsigned int* R, unsigned int* MaskR,
    unsigned int* T_tmp, int numWordsExtended, int E, int bit2E1, long long* carryBuffer, cg::grid_group& grid)
{
    Device_ShiftRight(T, bit2E1, Q, numWordsExtended, grid);
    Device_BitwiseAnd(T, MaskR, R, numWordsExtended, grid);
    Device_ShiftLeft(Q, E, T_tmp, numWordsExtended, grid);
    Device_Add(R, T_tmp, R, numWordsExtended, carryBuffer, grid);
    Device_ShiftLeft(Q, 1, T_tmp, numWordsExtended, grid);
    Device_Sub(R, Q, R, numWordsExtended, carryBuffer, grid);
    Device_Sub(R, T_tmp, R, numWordsExtended, carryBuffer, grid);
    Device_ShiftLeft(Q, 3, T_tmp, numWordsExtended, grid);
    Device_Sub(R, T_tmp, T, numWordsExtended, carryBuffer, grid);
}

__global__ void Kernel_StaticFermatLoop(
    unsigned int* T, unsigned int* Q, unsigned int* R, unsigned int* MaskR,
    unsigned int* T_tmp,
    int numWords, int E, long long* carryBuffer,
    int M, uint32_t* ntt_buf1, uint32_t* ntt_buf2, uint32_t* ntt_tmp,
    const uint32_t* twiddle1_fwd, const uint32_t* twiddle2_fwd,
    const uint32_t* twiddle1_inv, const uint32_t* twiddle2_inv) 
{
    cg::grid_group grid = cg::this_grid();
    int bit2E1 = 2 * E + 1;
    
    // Remaining exponent bits: 1010 ... 
    // Wait, the standard Fermat PRP sequence iterates E times.
    // E.g., square and multiply-by-2, etc.
    
    for (int iter = 0; iter < E; iter++) {
        // --- SquareAndReduce(T) ---
        Device_MultiplySquare(T, numWords, grid, M, ntt_buf1, ntt_buf2, ntt_tmp, twiddle1_fwd, twiddle2_fwd, twiddle1_inv, twiddle2_inv);
        
        for (int j = 0; j < 3; j++) {
            Device_FermatReduceStep(T, Q, R, MaskR, T_tmp, numWords * 2 + 16, E, bit2E1, carryBuffer, grid);
        }
        
        // --- MultiplyBy2AndReduce(T) ---
        Device_ShiftLeft(T, 1, T_tmp, numWords * 2 + 16, grid);
        Device_Copy(T_tmp, T, numWords * 2 + 16, grid);
        
        for (int j = 0; j < 3; j++) {
            Device_FermatReduceStep(T, Q, R, MaskR, T_tmp, numWords * 2 + 16, E, bit2E1, carryBuffer, grid);
        }
    }
    
    // Remaining E-4 iterations: SquareAndReduce only
    for (int iter = 0; iter < E - 4; iter++) {
        Device_MultiplySquare(T, numWords, grid, M, ntt_buf1, ntt_buf2, ntt_tmp, twiddle1_fwd, twiddle2_fwd, twiddle1_inv, twiddle2_inv);
        
        for (int j = 0; j < 3; j++) {
            Device_FermatReduceStep(T, Q, R, MaskR, T_tmp, numWords * 2 + 16, E, bit2E1, carryBuffer, grid);
        }
    }
    
    // Final specific bits (1010): Square/Mul2/Square/Mul2
    
    // 1 (bit 3)
    Device_MultiplySquare(T, numWords, grid, M, ntt_buf1, ntt_buf2, ntt_tmp, twiddle1_fwd, twiddle2_fwd, twiddle1_inv, twiddle2_inv);
    for (int j = 0; j < 3; j++) {
        for (int k = 0; k < 3; k++) {
            Device_FermatReduceStep(T, Q, R, MaskR, T_tmp, numWords * 2 + 16, E, bit2E1, carryBuffer, grid);
        }
    }
    Device_ShiftLeft(T, 1, T_tmp, numWords * 2 + 16, grid);
    Device_Copy(T_tmp, T, numWords * 2 + 16, grid);
    Device_FermatReduceStep(T, Q, R, MaskR, T_tmp, numWords * 2 + 16, E, bit2E1, carryBuffer, grid);
    
    // 0 (bit 2)
    Device_MultiplySquare(T, numWords, grid, M, ntt_buf1, ntt_buf2, ntt_tmp, twiddle1_fwd, twiddle2_fwd, twiddle1_inv, twiddle2_inv);
    for (int j = 0; j < 3; j++) {
        for (int k = 0; k < 3; k++) {
            Device_FermatReduceStep(T, Q, R, MaskR, T_tmp, numWords * 2 + 16, E, bit2E1, carryBuffer, grid);
        }
    }
    for (int iter = 0; iter < E - 4; iter++) {
        // --- SquareAndReduce(T) ---
        Device_MultiplySquare(T, numWords, grid, M, ntt_buf1, ntt_buf2, ntt_tmp, twiddle1_fwd, twiddle2_fwd, twiddle1_inv, twiddle2_inv);
        for (int j = 0; j < 3; j++) {
            Device_FermatReduceStep(T, Q, R, MaskR, T_tmp, numWords * 2 + 16, E, bit2E1, carryBuffer, grid);
        }
    }
    
    // bit 3 (1)
    Device_MultiplySquare(T, numWords, grid, M, ntt_buf1, ntt_buf2, ntt_tmp, twiddle1_fwd, twiddle2_fwd, twiddle1_inv, twiddle2_inv);
    for (int j = 0; j < 3; j++) {
        for (int k = 0; k < 3; k++) {
            Device_FermatReduceStep(T, Q, R, MaskR, T_tmp, numWords * 2 + 16, E, bit2E1, carryBuffer, grid);
        }
    }
    Device_ShiftLeft(T, 1, T_tmp, numWords * 2 + 16, grid);
    Device_Copy(T_tmp, T, numWords * 2 + 16, grid);
    Device_FermatReduceStep(T, Q, R, MaskR, T_tmp, numWords * 2 + 16, E, bit2E1, carryBuffer, grid);

    // bit 2 (0)
    Device_MultiplySquare(T, numWords, grid, M, ntt_buf1, ntt_buf2, ntt_tmp, twiddle1_fwd, twiddle2_fwd, twiddle1_inv, twiddle2_inv);
    for (int j = 0; j < 3; j++) {
        for (int k = 0; k < 3; k++) {
            Device_FermatReduceStep(T, Q, R, MaskR, T_tmp, numWords * 2 + 16, E, bit2E1, carryBuffer, grid);
        }
    }

    // bit 1 (1)
    Device_MultiplySquare(T, numWords, grid, M, ntt_buf1, ntt_buf2, ntt_tmp, twiddle1_fwd, twiddle2_fwd, twiddle1_inv, twiddle2_inv);
    for (int j = 0; j < 3; j++) {
        for (int k = 0; k < 3; k++) {
            Device_FermatReduceStep(T, Q, R, MaskR, T_tmp, numWords * 2 + 16, E, bit2E1, carryBuffer, grid);
        }
    }
    Device_ShiftLeft(T, 1, T_tmp, numWords * 2 + 16, grid);
    Device_Copy(T_tmp, T, numWords * 2 + 16, grid);
    Device_FermatReduceStep(T, Q, R, MaskR, T_tmp, numWords * 2 + 16, E, bit2E1, carryBuffer, grid);

    // bit 0 (0)
    Device_MultiplySquare(T, numWords, grid, M, ntt_buf1, ntt_buf2, ntt_tmp, twiddle1_fwd, twiddle2_fwd, twiddle1_inv, twiddle2_inv);
    for (int j = 0; j < 3; j++) {
        for (int k = 0; k < 3; k++) {
            Device_FermatReduceStep(T, Q, R, MaskR, T_tmp, numWords * 2 + 16, E, bit2E1, carryBuffer, grid);
        }
    }
}
