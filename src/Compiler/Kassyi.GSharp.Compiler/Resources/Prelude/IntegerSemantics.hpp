// --- C# integer semantics (both dialects; IntegerSemantics.cs decides where the transpiler calls them) ---

// int and long + - * and unary - wrap in two's complement (C# spec §12.10, unchecked context). C++ leaves signed overflow
// undefined, and nvcc folds on it (x + 1 < x becomes false). The operation is computed in the unsigned type, whose
// arithmetic C++ defines modulo 2^n, and converted back (modulo 2^n in nvcc, MSVC, GCC and Clang, and by C++20). After
// inlining they are the instructions of the signed operators without the no-overflow assumption.
__device__ __forceinline__ int __gsharp_add_i32(int a, int b) { return (int)((unsigned int)a + (unsigned int)b); }
__device__ __forceinline__ int __gsharp_sub_i32(int a, int b) { return (int)((unsigned int)a - (unsigned int)b); }
__device__ __forceinline__ int __gsharp_mul_i32(int a, int b) { return (int)((unsigned int)a * (unsigned int)b); }
__device__ __forceinline__ int __gsharp_neg_i32(int a) { return (int)(0u - (unsigned int)a); }
__device__ __forceinline__ long long __gsharp_add_i64(long long a, long long b) {
    return (long long)((unsigned long long)a + (unsigned long long)b);
}
__device__ __forceinline__ long long __gsharp_sub_i64(long long a, long long b) {
    return (long long)((unsigned long long)a - (unsigned long long)b);
}
__device__ __forceinline__ long long __gsharp_mul_i64(long long a, long long b) {
    return (long long)((unsigned long long)a * (unsigned long long)b);
}
__device__ __forceinline__ long long __gsharp_neg_i64(long long a) { return (long long)(0ull - (unsigned long long)a); }

// A shift uses the low 5 (int, uint) or 6 (long, ulong) bits of its count (C# spec §12.11). C++ leaves a count of the
// width or more undefined (PTX shl gives 0), and C++17 a left shift of a negative value or into the sign bit. The
// signed left shift and >>> are computed in the unsigned type; the masked 32-bit shift is one SHF.L.W / SHF.R.W.
__device__ __forceinline__ int __gsharp_shl_i32(int a, int n) { return (int)((unsigned int)a << (n & 31)); }
__device__ __forceinline__ int __gsharp_ushr_i32(int a, int n) { return (int)((unsigned int)a >> (n & 31)); }
__device__ __forceinline__ long long __gsharp_shl_i64(long long a, int n) {
    return (long long)((unsigned long long)a << (n & 63));
}
__device__ __forceinline__ long long __gsharp_ushr_i64(long long a, int n) {
    return (long long)((unsigned long long)a >> (n & 63));
}

// x op= y is x = (T)(x op y) with x evaluated once (C# spec §12.21.4); T is the target, which may be narrower than the
// operator (short += short is an int addition). ++x and --x are x += 1 and x -= 1. They return the target, as C# does.
// The transpiler writes x=__gsharp_add_i32(x,y) where evaluating x writes nothing, and these only for a target such as
// a[i++]: a T& bound to a member of a packed struct would read it as aligned.
template <typename T> __device__ __forceinline__ T& __gsharp_add_assign_i32(T& x, int y) {
    x = (T)__gsharp_add_i32((int)x, y); return x;
}
template <typename T> __device__ __forceinline__ T& __gsharp_sub_assign_i32(T& x, int y) {
    x = (T)__gsharp_sub_i32((int)x, y); return x;
}
template <typename T> __device__ __forceinline__ T& __gsharp_mul_assign_i32(T& x, int y) {
    x = (T)__gsharp_mul_i32((int)x, y); return x;
}
template <typename T> __device__ __forceinline__ T& __gsharp_shl_assign_i32(T& x, int n) {
    x = (T)__gsharp_shl_i32((int)x, n); return x;
}
template <typename T> __device__ __forceinline__ T& __gsharp_ushr_assign_i32(T& x, int n) {
    x = (T)__gsharp_ushr_i32((int)x, n); return x;
}
template <typename T> __device__ __forceinline__ T& __gsharp_add_assign_i64(T& x, long long y) {
    x = (T)__gsharp_add_i64((long long)x, y); return x;
}
template <typename T> __device__ __forceinline__ T& __gsharp_sub_assign_i64(T& x, long long y) {
    x = (T)__gsharp_sub_i64((long long)x, y); return x;
}
template <typename T> __device__ __forceinline__ T& __gsharp_mul_assign_i64(T& x, long long y) {
    x = (T)__gsharp_mul_i64((long long)x, y); return x;
}
template <typename T> __device__ __forceinline__ T& __gsharp_shl_assign_i64(T& x, int n) {
    x = (T)__gsharp_shl_i64((long long)x, n); return x;
}
template <typename T> __device__ __forceinline__ T& __gsharp_ushr_assign_i64(T& x, int n) {
    x = (T)__gsharp_ushr_i64((long long)x, n); return x;
}

// x++ and x-- whose value is used: the value before the step
template <typename T> __device__ __forceinline__ int __gsharp_postinc_i32(T& x) {
    int old = (int)x; x = (T)__gsharp_add_i32(old, 1); return old;
}
template <typename T> __device__ __forceinline__ int __gsharp_postdec_i32(T& x) {
    int old = (int)x; x = (T)__gsharp_sub_i32(old, 1); return old;
}
template <typename T> __device__ __forceinline__ long long __gsharp_postinc_i64(T& x) {
    long long old = (long long)x; x = (T)__gsharp_add_i64(old, 1); return old;
}
template <typename T> __device__ __forceinline__ long long __gsharp_postdec_i64(T& x) {
    long long old = (long long)x; x = (T)__gsharp_sub_i64(old, 1); return old;
}

// float and double to an integer type, as .NET 9 and later convert them: truncated toward zero, saturated at the ends of
// the type, NaN to 0. The C# specification leaves the out-of-range result unspecified (§10.3.2) and C++ leaves it
// undefined. A type narrower than int is the int32 result truncated (the transpiler writes (T)__gsharp_to_i32(x)),
// which is also what .NET gives. CUDA's round-toward-zero conversions (cvt.rzi) saturate; they are the same instruction
// as a C cast. Only float to a 32-bit type gives 0 for NaN (sm_86); from double, and to a 64-bit type, cvt.rzi gives the
// sign bit alone (0x80000000, 0x8000000000000000), so NaN is tested first (3 to 4 more instructions on sm_86).
#if defined(__CUDACC__)
__device__ __forceinline__ int __gsharp_to_i32(float x) { return __float2int_rz(x); }
__device__ __forceinline__ int __gsharp_to_i32(double x) { return x != x ? 0 : __double2int_rz(x); }
__device__ __forceinline__ unsigned int __gsharp_to_u32(float x) { return __float2uint_rz(x); }
__device__ __forceinline__ unsigned int __gsharp_to_u32(double x) { return x != x ? 0u : __double2uint_rz(x); }
__device__ __forceinline__ long long __gsharp_to_i64(float x) { return x != x ? 0 : __float2ll_rz(x); }
__device__ __forceinline__ long long __gsharp_to_i64(double x) { return x != x ? 0 : __double2ll_rz(x); }
__device__ __forceinline__ unsigned long long __gsharp_to_u64(float x) { return x != x ? 0ull : __float2ull_rz(x); }
__device__ __forceinline__ unsigned long long __gsharp_to_u64(double x) { return x != x ? 0ull : __double2ull_rz(x); }
#else
// The C cast is used only inside the range, where C++ defines it; a float widens to double exactly
__forceinline__ int __gsharp_to_i32(double x) {
    return x > -2147483649.0 && x < 2147483648.0 ? (int)x : (x != x ? 0 : (x > 0 ? 2147483647 : -2147483647 - 1));
}
__forceinline__ int __gsharp_to_i32(float x) { return __gsharp_to_i32((double)x); }
__forceinline__ unsigned int __gsharp_to_u32(double x) {
    return x > -1.0 && x < 4294967296.0 ? (unsigned int)x : (x > 0 ? 4294967295u : 0u);
}
__forceinline__ unsigned int __gsharp_to_u32(float x) { return __gsharp_to_u32((double)x); }
__forceinline__ long long __gsharp_to_i64(double x) {
    return x >= -9223372036854775808.0 && x < 9223372036854775808.0
        ? (long long)x
        : (x != x ? 0 : (x > 0 ? 9223372036854775807LL : -9223372036854775807LL - 1));
}
__forceinline__ long long __gsharp_to_i64(float x) { return __gsharp_to_i64((double)x); }
__forceinline__ unsigned long long __gsharp_to_u64(double x) {
    return x > -1.0 && x < 18446744073709551616.0 ? (unsigned long long)x : (x > 0 ? 18446744073709551615ULL : 0ULL);
}
__forceinline__ unsigned long long __gsharp_to_u64(float x) { return __gsharp_to_u64((double)x); }
#endif
