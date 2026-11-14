// --- C# integer division and remainder (both dialects; IntegerSemantics.cs decides where the transpiler calls them) ---

// x / y and x % y of an integer type throw DivideByZeroException in C# when y is 0 (C# spec §12.10.3, §12.10.4). C++
// leaves it undefined: x86 div and idiv raise a hardware exception that ends the native process, and PTX gives an
// unspecified value. Every build tests the divisor, the release CUDA build included (semantics charter §2, §4); the
// failing thread stops (the native step ends; a CUDA thread records the error and exits on sm_70 and later).
// MinValue / -1 and MinValue % -1 are implementation-defined in an unchecked C# context (§12.10.3); G# follows the GPU,
// which gives MinValue and 0 without an error. x86 idiv raises a hardware exception for them, so the native build tests
// -1 as well; nvcc's division gives that result as it is.
// The transpiler calls these only where the divisor may be 0 (or -1, for a dividend that may be MinValue). The place is
// the site of the kernel's error site table (unsigned int) or, in a device function, a message that names it.
// [WHY] On CUDA the failure is written inline (__gsharp_inline_trap, as for a null array read): a call of the noinline
// __gsharp_trap_site makes ptxas keep the caller's live registers across the call.
#if defined(__CUDACC__)
#define __GSHARP_DIVISOR_CHECK(divisor, site) \
    do { if ((divisor) == 0) { __gsharp_inline_trap(GSharpErrorCode::DivideByZero, __gsharp_inline_site(site)); } } \
    while (0)
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

// Math.Abs of a signed integer throws OverflowException for the minimum value in C# (#947); C++ abs of it is undefined
// and CUDA gives the minimum back. Every build tests the value, as the divisor check above does, and the failing thread
// stops. The place is the site of the kernel's error site table or, in a device function, a message that names it.
#if defined(__CUDACC__)
#define __GSHARP_ABS_CHECK(cond, site)     do { if (cond) { __gsharp_inline_trap(GSharpErrorCode::Overflow, __gsharp_inline_site(site)); } } while (0)
#else
#define __GSHARP_ABS_CHECK(cond, site)     do { if (cond) { __gsharp_trap_abort(GSharpErrorCode::Overflow, site); } } while (0)
#endif

template <typename S>
__device__ __forceinline__ int __gsharp_abs_i32(int a, S site) {
    __GSHARP_ABS_CHECK(a == (-2147483647 - 1), site);
    return a < 0 ? -a : a;
}
template <typename S>
__device__ __forceinline__ long long __gsharp_abs_i64(long long a, S site) {
    __GSHARP_ABS_CHECK(a == (-9223372036854775807LL - 1), site);
    return a < 0 ? -a : a;
}
template <typename S>
__device__ __forceinline__ short __gsharp_abs_i16(short a, S site) {
    __GSHARP_ABS_CHECK(a == -32768, site);
    return static_cast<short>(a < 0 ? -a : a);
}
template <typename S>
__device__ __forceinline__ signed char __gsharp_abs_i8(signed char a, S site) {
    __GSHARP_ABS_CHECK(a == -128, site);
    return static_cast<signed char>(a < 0 ? -a : a);
}

#undef __GSHARP_ABS_CHECK

#undef __GSHARP_DIVISOR_CHECK
