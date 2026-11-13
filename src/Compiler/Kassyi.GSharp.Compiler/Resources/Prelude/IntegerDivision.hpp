// --- C# integer division and remainder (both dialects; IntegerSemantics.cs decides where the transpiler calls them) ---

// x / y and x % y of an integer type throw DivideByZeroException in C# when y is 0 (C# spec §12.10.3, §12.10.4). C++
// leaves it undefined: x86 div and idiv raise a hardware exception that ends the native process, and PTX gives an
// unspecified value. The native CPU build tests the divisor every time; the CUDA build tests it in the checked build
// only (GSHARP_CHECKED), as the semantics charter lets the release CUDA build leave its checks out.
// MinValue / -1 and MinValue % -1 are implementation-defined in an unchecked C# context (§12.10.3); G# follows the GPU,
// which gives MinValue and 0 without an error. x86 idiv raises a hardware exception for them, so the native build tests
// -1 as well; nvcc's division gives that result as it is.
// The transpiler calls these only where the divisor may be 0 (or -1, for a dividend that may be MinValue). The place is
// the site of the kernel's error site table (unsigned int) or, in a device function, a message that names it.
#if defined(__CUDACC__) && !defined(GSHARP_CHECKED)
#define __GSHARP_DIVISOR_CHECK(divisor, site) ((void)(site))
#else
#define __GSHARP_DIVISOR_CHECK(divisor, site) \
    do { if ((divisor) == 0) { __gsharp_trap_abort(GSharpErrorCode::DivideByZero, site); } } while (0)
#endif

template <typename S>
__device__ __forceinline__ int __gsharp_div_i32(int a, int b, S site) {
    __GSHARP_DIVISOR_CHECK(b, site);
#if !defined(__CUDACC__)
    if (b == -1) { return __gsharp_neg_i32(a); }
#endif
    return a / b;
}
template <typename S>
__device__ __forceinline__ int __gsharp_rem_i32(int a, int b, S site) {
    __GSHARP_DIVISOR_CHECK(b, site);
#if !defined(__CUDACC__)
    if (b == -1) { return 0; }
#endif
    return a % b;
}
template <typename S>
__device__ __forceinline__ long long __gsharp_div_i64(long long a, long long b, S site) {
    __GSHARP_DIVISOR_CHECK(b, site);
#if !defined(__CUDACC__)
    if (b == -1) { return __gsharp_neg_i64(a); }
#endif
    return a / b;
}
template <typename S>
__device__ __forceinline__ long long __gsharp_rem_i64(long long a, long long b, S site) {
    __GSHARP_DIVISOR_CHECK(b, site);
#if !defined(__CUDACC__)
    if (b == -1) { return 0; }
#endif
    return a % b;
}
template <typename S>
__device__ __forceinline__ unsigned int __gsharp_div_u32(unsigned int a, unsigned int b, S site) {
    __GSHARP_DIVISOR_CHECK(b, site);
    return a / b;
}
template <typename S>
__device__ __forceinline__ unsigned int __gsharp_rem_u32(unsigned int a, unsigned int b, S site) {
    __GSHARP_DIVISOR_CHECK(b, site);
    return a % b;
}
template <typename S>
__device__ __forceinline__ unsigned long long __gsharp_div_u64(unsigned long long a, unsigned long long b, S site) {
    __GSHARP_DIVISOR_CHECK(b, site);
    return a / b;
}
template <typename S>
__device__ __forceinline__ unsigned long long __gsharp_rem_u64(unsigned long long a, unsigned long long b, S site) {
    __GSHARP_DIVISOR_CHECK(b, site);
    return a % b;
}

#undef __GSHARP_DIVISOR_CHECK
