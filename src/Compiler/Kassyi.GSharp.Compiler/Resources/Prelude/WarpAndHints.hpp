// --- Warp & Synchronization ---
inline unsigned __activemask() { return 0xFFFFFFFF; }
template <typename T> inline T __shfl_sync(unsigned mask, T var, int srcLane, int width=32) { return var; }
template <typename T> inline T __shfl_up_sync(unsigned mask, T var, unsigned int delta, int width=32) { return var; }
template <typename T> inline T __shfl_down_sync(unsigned mask, T var, unsigned int delta, int width=32) { return var; }
template <typename T> inline T __shfl_xor_sync(unsigned mask, T var, int laneMask, int width=32) { return var; }

template<typename T>
inline void __gsharp_warp_exchange_assign_impl(T val, uint64_t* exchange) {
    uint64_t raw = 0;
    std::memcpy(&raw, &val, sizeof(T));
    *exchange = raw;
}

// Shuffle k publishes into slot k % 2. Lanes of a converged warp run the same shuffle sequence and a source lane is at
// most one shuffle ahead, so it never overwrites the slot a slower lane still has to read.
// The root is the kernel context of the thread, also inside a device function's state machine.
#define __gsharp_warp_exchange_assign(val) __gsharp_warp_exchange_assign_impl(val, &__gsharp_root->__gsharp_warp_exchange[__gsharp_root->__gsharp_warp_shuffle_count & 1u])

template<typename TContext, typename T>
inline T __gsharp_warp_shuffle_read_down_sync(TContext* __gsharp_ctx, uint32_t mask, T val, uint32_t delta, int width = 32) {
    uint32_t slot = __gsharp_ctx->__gsharp_warp_shuffle_count++ & 1u;
    uint32_t tid = threadIdx.z * blockDim.y * blockDim.x + threadIdx.y * blockDim.x + threadIdx.x;
    uint32_t laneId = tid % 32;
    uint32_t warpId = tid / 32;
    uint32_t logicalLane = laneId % width;
    uint32_t srcLane = logicalLane + delta;
    
    T result;
    if (srcLane < (uint32_t)width) {
        uint32_t srcTid = warpId * 32 + (laneId - logicalLane) + srcLane;
        uint64_t raw = __gsharp_ctx->__gsharp_all_contexts[srcTid].__gsharp_warp_exchange[slot];
        std::memcpy(&result, &raw, sizeof(T));
    } else {
        result = val;
    }
    return result;
}

template<typename TContext, typename T>
inline T __gsharp_warp_shuffle_read_up_sync(TContext* __gsharp_ctx, uint32_t mask, T val, uint32_t delta, int width = 32) {
    uint32_t slot = __gsharp_ctx->__gsharp_warp_shuffle_count++ & 1u;
    uint32_t tid = threadIdx.z * blockDim.y * blockDim.x + threadIdx.y * blockDim.x + threadIdx.x;
    uint32_t laneId = tid % 32;
    uint32_t warpId = tid / 32;
    uint32_t logicalLane = laneId % width;
    
    T result;
    if (logicalLane >= delta) {
        uint32_t srcLane = logicalLane - delta;
        uint32_t srcTid = warpId * 32 + (laneId - logicalLane) + srcLane;
        uint64_t raw = __gsharp_ctx->__gsharp_all_contexts[srcTid].__gsharp_warp_exchange[slot];
        std::memcpy(&result, &raw, sizeof(T));
    } else {
        result = val;
    }
    return result;
}

template<typename TContext, typename T>
inline T __gsharp_warp_shuffle_read_xor_sync(TContext* __gsharp_ctx, uint32_t mask, T val, uint32_t laneMask, int width = 32) {
    uint32_t slot = __gsharp_ctx->__gsharp_warp_shuffle_count++ & 1u;
    uint32_t tid = threadIdx.z * blockDim.y * blockDim.x + threadIdx.y * blockDim.x + threadIdx.x;
    uint32_t laneId = tid % 32;
    uint32_t warpId = tid / 32;
    uint32_t logicalLane = laneId % width;
    uint32_t srcLane = logicalLane ^ laneMask;
    
    T result;
    if (srcLane < (uint32_t)width) {
        uint32_t srcTid = warpId * 32 + (laneId - logicalLane) + srcLane;
        uint64_t raw = __gsharp_ctx->__gsharp_all_contexts[srcTid].__gsharp_warp_exchange[slot];
        std::memcpy(&result, &raw, sizeof(T));
    } else {
        result = val;
    }
    return result;
}

template<typename TContext, typename T>
inline T __gsharp_warp_shuffle_read_sync(TContext* __gsharp_ctx, uint32_t mask, T val, uint32_t srcLane, int width = 32) {
    uint32_t slot = __gsharp_ctx->__gsharp_warp_shuffle_count++ & 1u;
    uint32_t tid = threadIdx.z * blockDim.y * blockDim.x + threadIdx.y * blockDim.x + threadIdx.x;
    uint32_t laneId = tid % 32;
    uint32_t warpId = tid / 32;
    uint32_t logicalLane = laneId % width;
    
    T result;
    if (srcLane < (uint32_t)width && (mask & (1u << srcLane))) {
        uint32_t srcTid = warpId * 32 + (laneId - logicalLane) + srcLane;
        uint64_t raw = __gsharp_ctx->__gsharp_all_contexts[srcTid].__gsharp_warp_exchange[slot];
        std::memcpy(&result, &raw, sizeof(T));
    } else {
        result = val;
    }
    return result;
}

template<typename T>
inline unsigned int __match_any_sync(unsigned int mask, T val) {
    return mask & 1;
}

template<typename T>
inline unsigned int __match_all_sync(unsigned int mask, T val, int* pred) {
    if (pred) *pred = 1;
    return mask & 1;
}

inline void __nanosleep(unsigned int duration) {}
inline void __syncwarp(unsigned int mask = 0xFFFFFFFFU) {}

// --- Compiler Hints ---
namespace Hint {
    inline void Unroll() {}
}

// --- Tensor Core (WMMA) ---
struct __gsharp_wmma_matrix_a {};
struct __gsharp_wmma_matrix_b {};
struct __gsharp_wmma_accumulator {};
struct __gsharp_wmma_row_major {};
struct __gsharp_wmma_col_major {};
template <int M, int N, int K> struct __gsharp_wmma_shape {};
template <class Use, int M, int N, int K, class T, class Layout = void>
struct __gsharp_wmma_fragment {};

template <class Shape, class T, class Layout = void, class Frag>
inline void __gsharp_wmma_fill(Frag*, T) {
    __gsharp_unsupported("WMMA is not supported by the StandardCpp dialect.");
}
template <class Shape, class T, class Layout, class Frag>
inline void __gsharp_wmma_load(Frag*, const T*, unsigned int) {
    __gsharp_unsupported("WMMA is not supported by the StandardCpp dialect.");
}
template <class Shape, class T, class Frag>
inline void __gsharp_wmma_load(Frag*, const T*, unsigned int, int) {
    __gsharp_unsupported("WMMA is not supported by the StandardCpp dialect.");
}
template <class Shape, class T, class Frag>
inline void __gsharp_wmma_store(T*, const Frag*, unsigned int, int) {
    __gsharp_unsupported("WMMA is not supported by the StandardCpp dialect.");
}
template <class Shape, class TIn, class LayoutA, class LayoutB, class TAcc, class FragD, class FragA, class FragB, class FragC>
inline void __gsharp_wmma_mma(FragD*, const FragA*, const FragB*, const FragC*) {
    __gsharp_unsupported("WMMA is not supported by the StandardCpp dialect.");
}
