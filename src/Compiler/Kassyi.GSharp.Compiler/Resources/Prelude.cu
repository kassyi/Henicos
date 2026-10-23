#include <cooperative_groups.h>
namespace cg = cooperative_groups;

// --- Standard Types ---
template <typename T>
struct System_Nullable {
    bool HasValue;
    T Value;
    __device__ inline System_Nullable() : HasValue(false), Value() {}
    __device__ inline System_Nullable(T val) : HasValue(true), Value(val) {}
};

// The scope of a C# using: disposes the resource when control leaves the enclosing block (end, return, break, continue,
// goto). Guards of one block are destroyed in reverse order, as C# disposes nested usings.
template <typename T>
struct __gsharp_using_scope {
    T& resource;
    __device__ inline ~__gsharp_using_scope() { resource.__gsharp_dispose(); }
};

template <typename T>
struct GSharpArray {
    T* __restrict__ data;
    int length;

    __device__ inline GSharpArray() : data(nullptr), length(0) {}
    __device__ inline GSharpArray(T* d, int l) : data(d), length(l) {}
    
    // Allows implicit conversion from GSharpArray<U> to GSharpArray<T> if U* is convertible to T* (e.g. T to const T)
    template <typename U>
    __device__ inline GSharpArray(const GSharpArray<U>& other) : data(other.data), length(other.length) {}

    __device__ inline T* begin() { return data; }
    __device__ inline T* end() { return data + length; }
    __device__ inline const T* begin() const { return data; }
    __device__ inline const T* end() const { return data + length; }
    __device__ inline int size() const { return length; }
    __device__ inline T& operator[](int index) { return data[index]; }
    __device__ inline const T& operator[](int index) const { return data[index]; }
    __device__ inline operator T*() { return data; }
    __device__ inline operator const T*() const { return data; }
};



// --- Arithmetic & SIMD ---
__device__ __forceinline__ unsigned int __gsharp_add_cc(unsigned int a, unsigned int b, unsigned long long* carry) {
    unsigned int res;
    unsigned int c_in = (unsigned int)*carry;
    unsigned int c_out;
    asm volatile(
        "add.cc.u32 %0, %2, -1;\n\t"
        "addc.cc.u32 %0, %3, %4;\n\t"
        "addc.u32 %1, 0, 0;"
        : "=r"(res), "=r"(c_out)
        : "r"(c_in), "r"(a), "r"(b)
    );
    *carry = c_out;
    return res;
}

__device__ __forceinline__ unsigned int __gsharp_sub_cc(unsigned int a, unsigned int b, long long* borrow) {
    unsigned int res;
    unsigned int dummy;
    unsigned int b_in = (unsigned int)*borrow;
    unsigned int b_out;
    asm volatile(
        "sub.cc.u32 %1, 0, %3;\n\t"
        "subc.cc.u32 %0, %4, %5;\n\t"
        "subc.u32 %2, 0, 0;"
        : "=r"(res), "=r"(dummy), "=r"(b_out)
        : "r"(b_in), "r"(a), "r"(b)
    );
    *borrow = b_out & 1;
    return res;
}

__device__ __forceinline__ unsigned long long __gsharp_mul_wide(unsigned int a, unsigned int b) {
    unsigned long long res;
    asm volatile("mul.wide.u32 %0, %1, %2;" : "=l"(res) : "r"(a), "r"(b));
    return res;
}

__device__ __forceinline__ unsigned long long __gsharp_mad_wide(unsigned int a, unsigned int b, unsigned long long c) {
    unsigned long long res;
    asm volatile("mad.wide.u32 %0, %1, %2, %3;" : "=l"(res) : "r"(a), "r"(b), "l"(c));
    return res;
}

__device__ __forceinline__ long long __gsharp_mad_wide_s(int a, int b, long long c) {
    long long res;
    asm volatile("mad.wide.s32 %0, %1, %2, %3;" : "=l"(res) : "r"(a), "r"(b), "l"(c));
    return res;
}

// (a * b) + c + carry-in; *carry holds 0 or 1 on entry and the carry-out of the 64-bit sum on exit.
// The product is at most 2^64 - 2^33 + 1, so the two additions cannot both wrap.
__device__ __forceinline__ unsigned long long __gsharp_madc_cc(unsigned int a, unsigned int b, unsigned long long c, unsigned long long* carry) {
    unsigned long long product = __gsharp_mad_wide(a, b, 0ull);
    unsigned long long partial = product + c;
    unsigned long long carryOut = partial < product ? 1ull : 0ull;
    unsigned long long res = partial + (*carry & 1ull);
    carryOut |= res < partial ? 1ull : 0ull;
    *carry = carryOut;
    return res;
}

// mbarrier (sm_80+). The object lives in shared memory; the generic pointer is converted to a shared address.
__device__ __forceinline__ void __gsharp_mbarrier_init(unsigned long long* barrier, unsigned int expectedCount) {
    unsigned int addr = (unsigned int)__cvta_generic_to_shared(barrier);
    asm volatile("mbarrier.init.shared.b64 [%0], %1;" :: "r"(addr), "r"(expectedCount) : "memory");
}

__device__ __forceinline__ void __gsharp_mbarrier_arrive(unsigned long long* barrier) {
    unsigned int addr = (unsigned int)__cvta_generic_to_shared(barrier);
    unsigned long long state;
    asm volatile("mbarrier.arrive.shared.b64 %0, [%1];" : "=l"(state) : "r"(addr) : "memory");
    (void)state;
}

// Spins on mbarrier.test_wait.parity until the phase with the given parity has completed.
__device__ __forceinline__ void __gsharp_mbarrier_wait(unsigned long long* barrier, unsigned int phase) {
    unsigned int addr = (unsigned int)__cvta_generic_to_shared(barrier);
    unsigned int done = 0;
    while (!done) {
        asm volatile(
            "{\n\t"
            ".reg .pred p;\n\t"
            "mbarrier.test_wait.parity.shared.b64 p, [%1], %2;\n\t"
            "selp.u32 %0, 1, 0, p;\n\t"
            "}"
            : "=r"(done) : "r"(addr), "r"(phase) : "memory");
    }
}

__device__ __forceinline__ unsigned int __gsharp_vabsdiff4(unsigned int a, unsigned int b, unsigned int c) {
    unsigned int res;
    asm("vabsdiff4.u32.u32.u32.add %0, %1, %2, %3;" : "=r"(res) : "r"(a), "r"(b), "r"(c));
    return res;
}

// --- Math & Range ---
__forceinline__ __device__ int __gsharp_clamp(int value, int min_val, int max_val) { return value < min_val ? min_val : (value > max_val ? max_val : value); }
__forceinline__ __device__ unsigned int __gsharp_clamp(unsigned int value, unsigned int min_val, unsigned int max_val) { return value < min_val ? min_val : (value > max_val ? max_val : value); }
__forceinline__ __device__ float __gsharp_clamp(float value, float min_val, float max_val) { return value < min_val ? min_val : (value > max_val ? max_val : value); }
__forceinline__ __device__ double __gsharp_clamp(double value, double min_val, double max_val) { return value < min_val ? min_val : (value > max_val ? max_val : value); }

// --- Bit Manipulation ---
__device__ __forceinline__ unsigned int __gsharp_rotl32(unsigned int value, int offset) {
    offset &= 31;
    return __funnelshift_l(value, value, offset);
}

__device__ __forceinline__ unsigned int __gsharp_rotr32(unsigned int value, int offset) {
    offset &= 31;
    return __funnelshift_r(value, value, offset);
}

__device__ __forceinline__ unsigned long long __gsharp_rotl64(unsigned long long value, int offset) {
    offset &= 63;
    return offset ? (value << offset) | (value >> (64 - offset)) : value;
}

__device__ __forceinline__ unsigned long long __gsharp_rotr64(unsigned long long value, int offset) {
    offset &= 63;
    return offset ? (value >> offset) | (value << (64 - offset)) : value;
}

__device__ __forceinline__ int __gsharp_tzc_32(unsigned int value) {
    return value ? __ffs(value) - 1 : 32;
}

__device__ __forceinline__ int __gsharp_tzc_64(unsigned long long value) {
    return value ? __ffsll(value) - 1 : 64;
}

__device__ __forceinline__ int __gsharp_log2_32(unsigned int value) {
    return value ? 31 - __clz(value) : 0;
}

__device__ __forceinline__ int __gsharp_log2_64(unsigned long long value) {
    return value ? 63 - __clzll(value) : 0;
}

__device__ __forceinline__ int __gsharp_lzc_32(unsigned int value) {
    return value ? __clz(value) : 32;
}

__device__ __forceinline__ int __gsharp_lzc_64(unsigned long long value) {
    return value ? __clzll(value) : 64;
}

__device__ __forceinline__ unsigned int __gsharp_bfe(unsigned int a, int pos, int len) {
    unsigned int res;
    asm("bfe.u32 %0, %1, %2, %3;" : "=r"(res) : "r"(a), "r"(pos), "r"(len));
    return res;
}

__device__ __forceinline__ unsigned long long __gsharp_bfe64(unsigned long long a, int pos, int len) {
    unsigned long long res;
    asm("bfe.u64 %0, %1, %2, %3;" : "=l"(res) : "l"(a), "r"(pos), "r"(len));
    return res;
}

__device__ __forceinline__ unsigned int __gsharp_bfi(unsigned int a, unsigned int b, int pos, int len) {
    unsigned int res;
    asm("bfi.b32 %0, %1, %2, %3, %4;" : "=r"(res) : "r"(a), "r"(b), "r"(pos), "r"(len));
    return res;
}

__device__ __forceinline__ unsigned long long __gsharp_bfi64(unsigned long long a, unsigned long long b, int pos, int len) {
    unsigned long long res;
    asm("bfi.b64 %0, %1, %2, %3, %4;" : "=l"(res) : "l"(a), "l"(b), "r"(pos), "r"(len));
    return res;
}

#define __gsharp_lop3(a, b, c, immLut) ([&]() -> unsigned int { \
    unsigned int __res; \
    asm("lop3.b32 %0, %1, %2, %3, %4;" : "=r"(__res) : "r"(a), "r"(b), "r"(c), "n"(immLut)); \
    return __res; \
}())

// --- Atomics ---
__forceinline__ __device__ int __gsharp_atomicCAS(int* address, int val, int compare) { return atomicCAS(address, compare, val); }
__forceinline__ __device__ unsigned int __gsharp_atomicCAS(unsigned int* address, unsigned int val, unsigned int compare) { return atomicCAS(address, compare, val); }
__forceinline__ __device__ unsigned long long __gsharp_atomicCAS(unsigned long long* address, unsigned long long val, unsigned long long compare) { return atomicCAS(address, compare, val); }

__forceinline__ __device__ int __gsharp_atomicAdd(int* address, int val) { return atomicAdd(address, val) + val; }
__forceinline__ __device__ unsigned int __gsharp_atomicAdd(unsigned int* address, unsigned int val) { return atomicAdd(address, val) + val; }
__forceinline__ __device__ unsigned long long __gsharp_atomicAdd(unsigned long long* address, unsigned long long val) { return atomicAdd(address, val) + val; }

// --- ZeroAlloc.Linq (StackLinq) ---
#include "StackLinq.cuh"

