#pragma once
#include <cuda_runtime.h>
#include <cstdint>

// NTT Primes and primitive roots
// We use two 32-bit primes for CRT to support convolution values up to 2^55
constexpr uint32_t NTT_P1 = 4253024257; 
constexpr uint32_t NTT_G1 = 5;

constexpr uint32_t NTT_P2 = 4246732801; 
constexpr uint32_t NTT_G2 = 2;

// Barrett Reduction Constants
// m = floor(2^64 / P)
constexpr uint64_t NTT_M1 = 4337323974ULL; // for P1
constexpr uint64_t NTT_M2 = 4343749639ULL; // for P2

// Device function for 32-bit modular addition
__device__ __forceinline__ uint32_t ModAdd(uint32_t a, uint32_t b, uint32_t p) {
    uint32_t res = a + b;
    if (res >= p || res < a) res -= p;
    return res;
}

// Device function for 32-bit modular subtraction
__device__ __forceinline__ uint32_t ModSub(uint32_t a, uint32_t b, uint32_t p) {
    uint32_t res = a - b;
    if (a < b) res += p;
    return res;
}

// Device function for 32x32 -> 32-bit modular multiplication using Barrett Reduction
__device__ __forceinline__ uint32_t ModMulBarrett(uint32_t a, uint32_t b, uint32_t p, uint64_t m) {
    // 64-bit multiplication using PTX intrinsic (much faster than full 64-bit mul)
    uint64_t prod;
    asm("mul.wide.u32 %0, %1, %2;" : "=l"(prod) : "r"(a), "r"(b));
    
    // Barrett reduction: q = (prod * m) >> 64
    uint64_t q;
    asm("mul.hi.u64 %0, %1, %2;" : "=l"(q) : "l"(prod), "l"(m));
    
    // r = prod - q * p
    uint32_t r = (uint32_t)prod - (uint32_t)q * p;
    
    // Correction step
    if (r >= p) r -= p;
    return r;
}

// Convenience overloads for P1 and P2
__device__ __forceinline__ uint32_t ModMul1(uint32_t a, uint32_t b) {
    return ModMulBarrett(a, b, NTT_P1, NTT_M1);
}

__device__ __forceinline__ uint32_t ModMul2(uint32_t a, uint32_t b) {
    return ModMulBarrett(a, b, NTT_P2, NTT_M2);
}

__device__ __forceinline__ uint32_t ModPow(uint32_t base, uint32_t exp, uint32_t p, uint64_t m) {
    uint32_t res = 1;
    base = base % p;
    while (exp > 0) {
        if (exp & 1) res = ModMulBarrett(res, base, p, m);
        base = ModMulBarrett(base, base, p, m);
        exp >>= 1;
    }
    return res;
}
