// --- Conversions & Casts ---
__forceinline__ float __gsharp_float_cast(float x) { return x; }
__forceinline__ float __gsharp_float_cast(double x) { return (float)x; }
__forceinline__ double __gsharp_double_cast(float x) { return (double)x; }
__forceinline__ double __gsharp_double_cast(double x) { return x; }
__forceinline__ int __gsharp_int_cast(float x) { return (int)x; }
__forceinline__ int __gsharp_int_cast(double x) { return (int)x; }
__forceinline__ uint32_t __gsharp_uint_cast(float x) { return (uint32_t)x; }
__forceinline__ uint32_t __gsharp_uint_cast(double x) { return (uint32_t)x; }

// --- Math & Range ---
template <typename T> T max(T a, T b) { return std::max(a, b); }
template <typename T> T min(T a, T b) { return std::min(a, b); }
template <typename T> T __gsharp_clamp(T val, T min_val, T max_val) {
    return std::max(min_val, std::min(val, max_val));
}

// --- Bit Manipulation & Arithmetic ---
__forceinline__ uint32_t __gsharp_rotr32(uint32_t value, int offset) { return _rotr(value, offset); }
__forceinline__ uint64_t __gsharp_rotr64(uint64_t value, int offset) { return _rotr64(value, offset); }
__forceinline__ uint32_t __gsharp_rotl32(uint32_t value, int offset) { return _rotl(value, offset); }
__forceinline__ uint64_t __gsharp_rotl64(uint64_t value, int offset) { return _rotl64(value, offset); }
__forceinline__ uint32_t __popc(uint32_t value) { return __popcnt(value); }
__forceinline__ uint64_t __popcll(uint64_t value) { return __popcnt64(value); }
__forceinline__ uint32_t __gsharp_lzc_32(uint32_t value) { unsigned long index; return _BitScanReverse(&index, value) ? 31 - index : 32; }
__forceinline__ uint64_t __gsharp_lzc_64(uint64_t value) { unsigned long index; return _BitScanReverse64(&index, value) ? 63 - index : 64; }
__forceinline__ uint32_t __gsharp_tzc_32(uint32_t value) { unsigned long index; return _BitScanForward(&index, value) ? index : 32; }
__forceinline__ uint64_t __gsharp_tzc_64(uint64_t value) { unsigned long index; return _BitScanForward64(&index, value) ? index : 64; }
__forceinline__ uint32_t __gsharp_log2_32(uint32_t value) { unsigned long index; return _BitScanReverse(&index, value) ? index : 0; }
__forceinline__ uint64_t __gsharp_log2_64(uint64_t value) { unsigned long index; return _BitScanReverse64(&index, value) ? index : 0; }
__forceinline__ unsigned int __gsharp_bfe(unsigned int a, int pos, int len) {
    if (len <= 0) return 0;
    if (len >= 32) return a >> pos;
    return (a >> pos) & ((1U << len) - 1);
}
__forceinline__ unsigned long long __gsharp_bfe64(unsigned long long a, int pos, int len) {
    if (len <= 0) return 0;
    if (len >= 64) return a >> pos;
    return (a >> pos) & ((1ULL << len) - 1);
}
__forceinline__ unsigned int __gsharp_bfi(unsigned int a, unsigned int b, int pos, int len) {
    if (len <= 0) return b;
    unsigned int mask = len >= 32 ? 0xFFFFFFFFU : ((1U << len) - 1);
    return (b & ~(mask << pos)) | ((a & mask) << pos);
}
__forceinline__ unsigned long long __gsharp_bfi64(unsigned long long a, unsigned long long b, int pos, int len) {
    if (len <= 0) return b;
    unsigned long long mask = len >= 64 ? 0xFFFFFFFFFFFFFFFFULL : ((1ULL << len) - 1);
    return (b & ~(mask << pos)) | ((a & mask) << pos);
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
__forceinline__ unsigned long long __brevll(unsigned long long val) {
    unsigned long long res = 0;
    for (int i = 0; i < 64; i++) {
        if ((val >> i) & 1) res |= (1ULL << (63 - i));
    }
    return res;
}
__forceinline__ int __dp4a(int a, int b, int c) {
    const signed char* va = reinterpret_cast<const signed char*>(&a);
    const signed char* vb = reinterpret_cast<const signed char*>(&b);
    return c + va[0]*vb[0] + va[1]*vb[1] + va[2]*vb[2] + va[3]*vb[3];
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
