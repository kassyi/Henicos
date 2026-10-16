#include <cmath>
#include <algorithm>
#include <cstdint>
#include <intrin.h>
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
