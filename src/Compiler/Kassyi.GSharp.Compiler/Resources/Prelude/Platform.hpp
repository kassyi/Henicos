#include <cmath>
#include <algorithm>
#include <cstdint>
#if defined(_MSC_VER)
#include <intrin.h>
#endif
#include <vector>
#include <functional>
#include <atomic>
#include <cstring>
#include <cstdio>
#include <cstdlib>

#define __device__
#define __global__
#if defined(_MSC_VER)
#define __forceinline__ __forceinline
#elif defined(__GNUC__) || defined(__clang__)
#define __forceinline__ inline __attribute__((always_inline))
#else
#define __forceinline__ inline
#endif

#if defined(_MSC_VER)
#define __noinline__ __declspec(noinline)
#elif defined(__GNUC__) || defined(__clang__)
#define __noinline__ __attribute__((noinline))
#else
#define __noinline__
#endif
#define __shared__
#if defined(_MSC_VER)
#define __restrict__ __restrict
#endif

#if !defined(_MSC_VER)
#ifndef __declspec
#define dllexport visibility("default")
#define __declspec(x) __attribute__((x))
#endif
__forceinline__ uint32_t _rotr(uint32_t v, int s) { unsigned int u = (unsigned int)s & 31u; return u == 0 ? v : ((v >> u) | (v << (32u - u))); }
__forceinline__ uint64_t _rotr64(uint64_t v, int s) { unsigned int u = (unsigned int)s & 63u; return u == 0 ? v : ((v >> u) | (v << (64u - u))); }
__forceinline__ uint32_t _rotl(uint32_t v, int s) { unsigned int u = (unsigned int)s & 31u; return u == 0 ? v : ((v << u) | (v >> (32u - u))); }
__forceinline__ uint64_t _rotl64(uint64_t v, int s) { unsigned int u = (unsigned int)s & 63u; return u == 0 ? v : ((v << u) | (v >> (64u - u))); }
__forceinline__ uint32_t __popcnt(uint32_t v) { return (uint32_t)__builtin_popcount(v); }
__forceinline__ uint64_t __popcnt64(uint64_t v) { return (uint64_t)__builtin_popcountll(v); }
__forceinline__ unsigned char _BitScanReverse(unsigned long* idx, uint32_t m) { if (!m) return 0; *idx = (unsigned long)(31 - __builtin_clz(m)); return 1; }
__forceinline__ unsigned char _BitScanReverse64(unsigned long* idx, uint64_t m) { if (!m) return 0; *idx = (unsigned long)(63 - __builtin_clzll(m)); return 1; }
__forceinline__ unsigned char _BitScanForward(unsigned long* idx, uint32_t m) { if (!m) return 0; *idx = (unsigned long)__builtin_ctz(m); return 1; }
__forceinline__ unsigned char _BitScanForward64(unsigned long* idx, uint64_t m) { if (!m) return 0; *idx = (unsigned long)__builtin_ctzll(m); return 1; }
#endif
