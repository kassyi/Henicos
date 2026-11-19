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

// float and double to an integer type, as .NET 9 and later convert them: truncated toward zero, saturated at the ends of
// the type, NaN to 0. The C# specification leaves the out-of-range result unspecified (§10.3.2) and C++ leaves it
// undefined. These helpers cover int, uint, long and ulong.
//
// A type narrower than int (sbyte, byte, short, ushort, char) has no helper of its own: the transpiler writes
// (T)__gsharp_to_i32(x), which saturates to int32 and then keeps the low bits of the result. That is the result of
// .NET 9 and 10: (byte)300f is 44, (sbyte)-129f is 127, (ushort)-40000f is 25536, (byte)-1f is 255, and NaN is 0.
// .NET 11 saturates to the target type instead (dotnet/runtime#128604): (byte)298f is 255, (ushort)-42f is 0.
// G# follows .NET 10 for now because the managed path runs the C# on the host runtime, so on net10.0 the GPU, the
// native CPU build and the managed path all give the same value. IntegerSemanticsScenarios.Conversions will fail on a
// .NET 11 host; switching these conversions to the .NET 11 result when the host moves to .NET 11 is #890.
// CUDA's round-toward-zero conversions (cvt.rzi) saturate; they are the same instruction as a C cast. Only float to a
// 32-bit type gives 0 for NaN (sm_86); from double, and to a 64-bit type, cvt.rzi gives the sign bit alone (0x80000000,
// 0x8000000000000000), so NaN is tested first (3 to 4 more instructions on sm_86).
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

// The exact sum of 64-bit integers (StackLinq Sum of long and ulong): 128 bits, the low word unsigned and the high word
// signed. [WHY] The elements are added without a check and the total is range-checked once (__gsharp_sum_i64), so the
// result does not depend on the order of the additions, which the block and grid partial sums change; no sequence has
// 2^63 elements, so the high word cannot overflow.
struct __gsharp_wide_sum { unsigned long long lo; long long hi; };
__device__ __forceinline__ __gsharp_wide_sum __gsharp_wide_add_i64(__gsharp_wide_sum s, long long v) {
    const unsigned long long lo = s.lo + (unsigned long long)v;
    s.hi = (long long)((unsigned long long)s.hi + (v < 0 ? ~0ull : 0ull) + (lo < s.lo ? 1ull : 0ull));
    s.lo = lo;
    return s;
}
__device__ __forceinline__ __gsharp_wide_sum __gsharp_wide_add_u64(__gsharp_wide_sum s, unsigned long long v) {
    const unsigned long long lo = s.lo + v;
    s.hi = (long long)((unsigned long long)s.hi + (lo < s.lo ? 1ull : 0ull));
    s.lo = lo;
    return s;
}
// A 64-bit partial sum as a wide sum (the grid combine of an int or uint Sum, which can pass 2^63 in total)
__device__ __forceinline__ __gsharp_wide_sum __gsharp_wide_from_i64(long long v) {
    __gsharp_wide_sum s;
    s.lo = (unsigned long long)v;
    s.hi = v < 0 ? -1ll : 0ll;
    return s;
}
__device__ __forceinline__ __gsharp_wide_sum __gsharp_wide_from_u64(unsigned long long v) {
    __gsharp_wide_sum s;
    s.lo = v;
    s.hi = 0;
    return s;
}
// The sum of two partial sums (the combine of a block or grid reduction)
__device__ __forceinline__ __gsharp_wide_sum __gsharp_wide_combine(__gsharp_wide_sum a, __gsharp_wide_sum b) {
    __gsharp_wide_sum s;
    s.lo = a.lo + b.lo;
    s.hi = (long long)((unsigned long long)a.hi + (unsigned long long)b.hi + (s.lo < a.lo ? 1ull : 0ull));
    return s;
}

// Math.Max / Math.Min / Math.Sign of the integer types other than int. The C# overload returns the narrow type:
// `var m = Math.Max(b1, b2)` is a byte, and `ref m` must be a pointer to one. CUDA's max(int, int) would give an int.
// The arguments are already converted to the parameter type, so T is deduced from both.
template <typename T> __device__ __forceinline__ T __gsharp_max_int(T a, T b) { return b > a ? b : a; }
template <typename T> __device__ __forceinline__ T __gsharp_min_int(T a, T b) { return b < a ? b : a; }
template <typename T> __device__ __forceinline__ int __gsharp_sign_int(T a) { return (int)(a > 0) - (int)(a < 0); }

// BitOperations.IsPow2: the signed forms test v > 0 first and subtract in the unsigned type: MinValue - 1 is undefined
// in C++. The nint form receives the unsigned long long cast that every native-sized BitOperations call gets
// (BclIntrinsicMapBuilder), converted back to long long, which keeps the bits.
__device__ __forceinline__ bool __gsharp_is_pow2_i32(int v) {
    return v > 0 && ((unsigned int)v & ((unsigned int)v - 1u)) == 0u;
}
__device__ __forceinline__ bool __gsharp_is_pow2_u32(unsigned int v) { return v != 0u && (v & (v - 1u)) == 0u; }
__device__ __forceinline__ bool __gsharp_is_pow2_i64(long long v) {
    return v > 0 && ((unsigned long long)v & ((unsigned long long)v - 1ull)) == 0ull;
}
__device__ __forceinline__ bool __gsharp_is_pow2_u64(unsigned long long v) {
    return v != 0ull && (v & (v - 1ull)) == 0ull;
}
