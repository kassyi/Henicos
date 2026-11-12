// --- Math & Range ---
template <typename T> T max(T a, T b) { return std::max(a, b); }
template <typename T> T min(T a, T b) { return std::min(a, b); }
// Math.Clamp in the .NET order: value < min first, so a NaN value stays NaN, a NaN bound is ignored and the sign of a
// zero value is kept
template <typename T> T __gsharp_clamp(T value, T min_val, T max_val) {
    return value < min_val ? min_val : (value > max_val ? max_val : value);
}
// Math.Max / Math.Min (IEEE 754:2019 maximum / minimum), written as the .NET implementation: NaN if either input is NaN,
// +0 > -0. fmax / fmin are not used: C leaves the order of signed zeros to the library.
// One name per type, as fmaxf / fmax: a call passes its C# arguments unconverted (Math.Max(double, float)), which
// an overload set would find ambiguous.
// The __gsharp_ieee_* templates hold the bodies; the per-type wrappers below are the only callers.
template <typename T> T __gsharp_ieee_max(T a, T b) {
    if (a != b) { return std::isnan(a) ? a : (b < a ? a : b); }
    return std::signbit(b) ? a : b;
}
template <typename T> T __gsharp_ieee_min(T a, T b) {
    if (a != b) { return std::isnan(a) ? a : (a < b ? a : b); }
    return std::signbit(a) ? a : b;
}
__forceinline__ float __gsharp_max_f32(float a, float b) { return __gsharp_ieee_max(a, b); }
__forceinline__ double __gsharp_max_f64(double a, double b) { return __gsharp_ieee_max(a, b); }
__forceinline__ float __gsharp_min_f32(float a, float b) { return __gsharp_ieee_min(a, b); }
__forceinline__ double __gsharp_min_f64(double a, double b) { return __gsharp_ieee_min(a, b); }
// float / double MaxNumber / MinNumber (IEEE 754:2019 maximumNumber / minimumNumber), as .NET: NaN only if both are
// NaN, +0 > -0. The same values as PTX max / min, which the CUDA prelude uses.
template <typename T> T __gsharp_ieee_max_number(T a, T b) {
    if (a != b) { return std::isnan(b) ? a : (b < a ? a : b); }
    return std::signbit(b) ? a : b;
}
template <typename T> T __gsharp_ieee_min_number(T a, T b) {
    if (a != b) { return std::isnan(b) ? a : (a < b ? a : b); }
    return std::signbit(a) ? a : b;
}
__forceinline__ float __gsharp_max_number_f32(float a, float b) { return __gsharp_ieee_max_number(a, b); }
__forceinline__ double __gsharp_max_number_f64(double a, double b) { return __gsharp_ieee_max_number(a, b); }
__forceinline__ float __gsharp_min_number_f32(float a, float b) { return __gsharp_ieee_min_number(a, b); }
__forceinline__ double __gsharp_min_number_f64(double a, double b) { return __gsharp_ieee_min_number(a, b); }

// --- Bit Manipulation & Arithmetic ---
__forceinline__ uint32_t __gsharp_rotr32(uint32_t value, int offset) { return _rotr(value, offset); }
__forceinline__ uint64_t __gsharp_rotr64(uint64_t value, int offset) { return _rotr64(value, offset); }
__forceinline__ uint32_t __gsharp_rotl32(uint32_t value, int offset) { return _rotl(value, offset); }
__forceinline__ uint64_t __gsharp_rotl64(uint64_t value, int offset) { return _rotl64(value, offset); }
__forceinline__ int __popc(uint32_t value) { return (int)__popcnt(value); }
__forceinline__ int __popcll(uint64_t value) { return (int)__popcnt64(value); }
__forceinline__ int __gsharp_lzc_32(uint32_t value) { unsigned long index; return _BitScanReverse(&index, value) ? (int)(31 - index) : 32; }
__forceinline__ int __gsharp_lzc_64(uint64_t value) { unsigned long index; return _BitScanReverse64(&index, value) ? (int)(63 - index) : 64; }
__forceinline__ int __gsharp_tzc_32(uint32_t value) { unsigned long index; return _BitScanForward(&index, value) ? (int)index : 32; }
__forceinline__ int __gsharp_tzc_64(uint64_t value) { unsigned long index; return _BitScanForward64(&index, value) ? (int)index : 64; }
__forceinline__ int __gsharp_log2_32(uint32_t value) { unsigned long index; return _BitScanReverse(&index, value) ? (int)index : 0; }
__forceinline__ int __gsharp_log2_64(uint64_t value) { unsigned long index; return _BitScanReverse64(&index, value) ? (int)index : 0; }
// PTX bfe.u32 / bfe.u64: the field of a from start, of length bits, with the bits past the top of a zero. The GPU takes
// pos and len modulo 256 for 32 bits and as unsigned 32-bit values for 64 bits (measured on the GPU; the PTX ISA says
// modulo 256 for both)
__forceinline__ unsigned long long __gsharp_extract_field(unsigned long long a, unsigned int start, unsigned int length,
                                                          unsigned int bits) {
    if (length == 0 || start >= bits) return 0;
    unsigned long long field = a >> start;
    return length >= bits ? field : field & ((1ULL << length) - 1);
}
__forceinline__ unsigned int __gsharp_bfe(unsigned int a, int pos, int len) {
    return (unsigned int)__gsharp_extract_field(a, (unsigned int)pos & 0xFFu, (unsigned int)len & 0xFFu, 32);
}
__forceinline__ unsigned long long __gsharp_bfe64(unsigned long long a, int pos, int len) {
    return __gsharp_extract_field(a, (unsigned int)pos, (unsigned int)len, 64);
}
// PTX bfi.b32 / bfi.b64: the low bits of a replace the field of b from start, up to the top of b; pos and len as for
// bfe
__forceinline__ unsigned long long __gsharp_insert_field(unsigned long long a, unsigned long long b, unsigned int start,
                                                         unsigned int length, unsigned int bits) {
    if (length == 0 || start >= bits) return b;
    unsigned int width = length < bits - start ? length : bits - start;
    unsigned long long mask = width >= 64 ? 0xFFFFFFFFFFFFFFFFULL : ((1ULL << width) - 1);
    return (b & ~(mask << start)) | ((a & mask) << start);
}
__forceinline__ unsigned int __gsharp_bfi(unsigned int a, unsigned int b, int pos, int len) {
    return (unsigned int)__gsharp_insert_field(a, b, (unsigned int)pos & 0xFFu, (unsigned int)len & 0xFFu, 32);
}
__forceinline__ unsigned long long __gsharp_bfi64(unsigned long long a, unsigned long long b, int pos, int len) {
    return __gsharp_insert_field(a, b, (unsigned int)pos, (unsigned int)len, 64);
}
__forceinline__ unsigned int __gsharp_lop3(unsigned int a, unsigned int b, unsigned int c, unsigned char immLut) {
    unsigned int res = 0;
    for (int i = 0; i < 32; i++) {
        int bitA = (a >> i) & 1;
        int bitB = (b >> i) & 1;
        int bitC = (c >> i) & 1;
        int idx = (bitA << 2) | (bitB << 1) | bitC;
        if ((immLut >> idx) & 1) res |= (1U << i);
    }
    return res;
}
__forceinline__ unsigned int __brev(unsigned int val) {
    unsigned int res = 0;
    for (int i = 0; i < 32; i++) {
        if ((val >> i) & 1) res |= (1U << (31 - i));
    }
    return res;
}
// CUDA wraps the shift amount to 0-31
__forceinline__ unsigned int __funnelshift_l(unsigned int lo, unsigned int hi, unsigned int shift) {
    unsigned long long pair = ((unsigned long long)hi << 32) | lo;
    return (unsigned int)((pair << (shift & 31)) >> 32);
}
__forceinline__ unsigned int __funnelshift_r(unsigned int lo, unsigned int hi, unsigned int shift) {
    unsigned long long pair = ((unsigned long long)hi << 32) | lo;
    return (unsigned int)(pair >> (shift & 31));
}
// Like the PTX add.cc / addc sequence of the CUDA prelude: any non-zero carry-in adds one
__forceinline__ unsigned int __gsharp_add_cc(unsigned int a, unsigned int b, unsigned long long* carry) {
    unsigned long long sum = (unsigned long long)a + b + ((unsigned int)*carry != 0 ? 1 : 0);
    *carry = sum >> 32;
    return (unsigned int)sum;
}
// Like the PTX sub.cc / subc sequence of the CUDA prelude: any non-zero borrow-in subtracts one
__forceinline__ unsigned int __gsharp_sub_cc(unsigned int a, unsigned int b, long long* borrow) {
    unsigned long long subtrahend = (unsigned long long)b + ((unsigned int)*borrow != 0 ? 1 : 0);
    *borrow = (unsigned long long)a < subtrahend ? 1 : 0;
    return (unsigned int)((unsigned long long)a - subtrahend);
}
// Gpu.CountLeadingZeros: 32 or 64 for zero, as the CUDA __clz / __clzll
__forceinline__ int __clz(uint32_t val) { return __gsharp_lzc_32(val); }
__forceinline__ int __clzll(uint64_t val) { return __gsharp_lzc_64(val); }
// Gpu.BytePermute: byte n of the result is the byte of the 64-bit value {y, x} that the low three bits of nibble n of
// the selector name (bytes 0-3 are x, 4-7 are y), as the CUDA __byte_perm; the high bit of each nibble is ignored
__forceinline__ unsigned int __byte_perm(unsigned int x, unsigned int y, unsigned int s) {
    unsigned long long pair = ((unsigned long long)y << 32) | x;
    unsigned int res = 0;
    for (int n = 0; n < 4; n++) {
        unsigned int index = (s >> (4 * n)) & 7u;
        res |= (unsigned int)((pair >> (8 * index)) & 0xFFu) << (8 * n);
    }
    return res;
}
// Gpu.MultiplyHigh: the high half of the full product, as the CUDA __mulhi / __umul64hi / __mul64hi
__forceinline__ int __mulhi(int a, int b) {
    return (int)(unsigned int)((unsigned long long)((long long)a * (long long)b) >> 32);
}
__forceinline__ unsigned long long __umul64hi(unsigned long long a, unsigned long long b) {
    unsigned long long aLo = a & 0xFFFFFFFFull, aHi = a >> 32, bLo = b & 0xFFFFFFFFull, bHi = b >> 32;
    unsigned long long lowLow = aLo * bLo;
    unsigned long long cross1 = aHi * bLo + (lowLow >> 32);
    unsigned long long cross2 = aLo * bHi + (cross1 & 0xFFFFFFFFull);
    return aHi * bHi + (cross1 >> 32) + (cross2 >> 32);
}
// The signed high half is the unsigned one less each operand that the sign bit of the other multiplies by 2^64
__forceinline__ long long __mul64hi(long long a, long long b) {
    unsigned long long hi = __umul64hi((unsigned long long)a, (unsigned long long)b);
    hi -= a < 0 ? (unsigned long long)b : 0ull;
    hi -= b < 0 ? (unsigned long long)a : 0ull;
    return (long long)hi;
}
// Gpu.MultiplyWide and Gpu.MultiplyAddWide: the 64-bit product of two 32-bit operands plus a 64-bit addend, wrapping
// modulo 2^64 as PTX mul.wide / mad.wide do
__forceinline__ unsigned long long __gsharp_mul_wide(unsigned int a, unsigned int b) {
    return (unsigned long long)a * b;
}
__forceinline__ unsigned long long __gsharp_mad_wide(unsigned int a, unsigned int b, unsigned long long c) {
    return (unsigned long long)a * b + c;
}
__forceinline__ long long __gsharp_mad_wide_s(int a, int b, long long c) {
    return (long long)((unsigned long long)((long long)a * (long long)b) + (unsigned long long)c);
}
// Gpu.MultiplyAddWideWithCarry: (a * b) + c + carry-in, as the CUDA prelude; the low bit of *carry is the carry-in,
// and *carry receives the carry-out of the 64-bit sum
__forceinline__ unsigned long long __gsharp_madc_cc(unsigned int a, unsigned int b, unsigned long long c,
                                                    unsigned long long* carry) {
    unsigned long long product = (unsigned long long)a * b;
    unsigned long long partial = product + c;
    unsigned long long carryOut = partial < product ? 1ull : 0ull;
    unsigned long long res = partial + (*carry & 1ull);
    carryOut |= res < partial ? 1ull : 0ull;
    *carry = carryOut;
    return res;
}
// Gpu.AbsDiff: |a - b| in 32 bits, as PTX sad (the CUDA __sad / __usad with a zero addend); a signed difference
// above int.MaxValue keeps its 32 bits and reads as negative
__forceinline__ int __gsharp_abs_diff(int a, int b) {
    return (int)(a < b ? (unsigned int)b - (unsigned int)a : (unsigned int)a - (unsigned int)b);
}
__forceinline__ unsigned int __gsharp_abs_diff(unsigned int a, unsigned int b) { return a < b ? b - a : a - b; }
__forceinline__ unsigned long long __brevll(unsigned long long val) {
    unsigned long long res = 0;
    for (int i = 0; i < 64; i++) {
        if ((val >> i) & 1) res |= (1ULL << (63 - i));
    }
    return res;
}
// The sum wraps in 32 bits, as the instruction does
__forceinline__ int __dp4a(int a, int b, int c) {
    const signed char* va = reinterpret_cast<const signed char*>(&a);
    const signed char* vb = reinterpret_cast<const signed char*>(&b);
    unsigned int sum = (unsigned int)c;
    for (int i = 0; i < 4; i++) sum += (unsigned int)(va[i] * vb[i]);
    return (int)sum;
}
__forceinline__ unsigned int __dp4a(unsigned int a, unsigned int b, unsigned int c) {
    const unsigned char* va = reinterpret_cast<const unsigned char*>(&a);
    const unsigned char* vb = reinterpret_cast<const unsigned char*>(&b);
    return c + va[0]*vb[0] + va[1]*vb[1] + va[2]*vb[2] + va[3]*vb[3];
}
__forceinline__ unsigned int __gsharp_vabsdiff4(unsigned int a, unsigned int b, unsigned int c) {
    const unsigned char* va = reinterpret_cast<const unsigned char*>(&a);
    const unsigned char* vb = reinterpret_cast<const unsigned char*>(&b);
    unsigned int diff = (unsigned int)std::abs((int)va[0] - (int)vb[0])
                      + (unsigned int)std::abs((int)va[1] - (int)vb[1])
                      + (unsigned int)std::abs((int)va[2] - (int)vb[2])
                      + (unsigned int)std::abs((int)va[3] - (int)vb[3]);
    return c + diff;
}
