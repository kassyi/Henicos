// --- Warp & Synchronization ---
// The lanes of the calling thread's warp that exist in the block: all 32, or fewer in the last warp of a block whose size
// is not a multiple of 32. A converged warp on the GPU reports the same lanes.
inline unsigned __activemask() {
    uint32_t tid = threadIdx.z * blockDim.y * blockDim.x + threadIdx.y * blockDim.x + threadIdx.x;
    uint32_t lanes = blockDim.x * blockDim.y * blockDim.z - (tid & ~31u);
    return lanes >= 32u ? 0xFFFFFFFFu : (1u << lanes) - 1u;
}
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

// The shuffles that CUDA leaves undefined report an InvalidOperationException with the place of the shuffle in the C#
// source and return the caller's own value (#666): a width that is not 1, 2, 4, 8, 16 or 32, and a source lane that does
// not exist in the block (the last warp of a block whose size is not a multiple of 32). A source outside the caller's
// segment of the warp is defined: up and down return the caller's own value, xor reads an earlier segment and returns
// the caller's own value for a later one, idx reads lane srcLane % width of the segment (#692). ManagedGpuContext applies
// the same rules on the managed path.
inline bool __gsharp_warp_shuffle_check_width(int width, const char* name, const char* location) {
    if (width == 1 || width == 2 || width == 4 || width == 8 || width == 16 || width == 32) {
        return true;
    }
    char text[512];
    std::snprintf(text, sizeof(text),
                  "G#: %s at %s has width %d; the width must be 1, 2, 4, 8, 16 or 32 (CUDA leaves the result undefined)",
                  name, location, width);
    __gsharp_trap(GSharpErrorCode::InvalidOperation, text);
    return false;
}

inline uint32_t __gsharp_warp_linear_tid() {
    return threadIdx.z * blockDim.y * blockDim.x + threadIdx.y * blockDim.x + threadIdx.x;
}

// Reads the value that lane srcLane of the caller's warp published for this shuffle, after checking that the lane exists
// in the block
template<typename TContext, typename T>
inline T __gsharp_warp_shuffle_read_lane(TContext* __gsharp_ctx, uint32_t slot, T val, uint32_t tid, uint32_t srcLane,
                                         const char* name, const char* location) {
    uint32_t threads = blockDim.x * blockDim.y * blockDim.z;
    uint32_t warpBase = tid & ~31u;
    uint32_t srcTid = warpBase + srcLane;
    if (srcTid >= threads) {
        char text[512];
        std::snprintf(text, sizeof(text),
                      "G#: %s at %s reads lane %u of warp %u, which does not exist in a block of %u threads (CUDA leaves the value undefined)",
                      name, location, srcLane, warpBase / 32u, threads);
        __gsharp_trap(GSharpErrorCode::InvalidOperation, text);
        return val;
    }
    T result;
    uint64_t raw = __gsharp_ctx->__gsharp_all_contexts[srcTid].__gsharp_warp_exchange[slot];
    std::memcpy(&result, &raw, sizeof(T));
    return result;
}

template<typename TContext, typename T>
inline T __gsharp_warp_shuffle_read_down_sync(TContext* __gsharp_ctx, uint32_t mask, T val, uint32_t delta, int width,
                                              const char* location) {
    uint32_t slot = __gsharp_ctx->__gsharp_warp_shuffle_count++ & 1u;
    const char* name = "Warp.ShuffleDownSync";
    if (!__gsharp_warp_shuffle_check_width(width, name, location)) {
        return val;
    }
    uint32_t tid = __gsharp_warp_linear_tid();
    uint32_t laneId = tid % 32u;
    uint32_t logicalLane = laneId % (uint32_t)width;
    uint64_t srcLogicalLane = (uint64_t)logicalLane + delta;
    if (srcLogicalLane >= (uint64_t)width) {
        return val;
    }
    return __gsharp_warp_shuffle_read_lane(__gsharp_ctx, slot, val, tid, laneId - logicalLane + (uint32_t)srcLogicalLane, name, location);
}

template<typename TContext, typename T>
inline T __gsharp_warp_shuffle_read_up_sync(TContext* __gsharp_ctx, uint32_t mask, T val, uint32_t delta, int width,
                                            const char* location) {
    uint32_t slot = __gsharp_ctx->__gsharp_warp_shuffle_count++ & 1u;
    const char* name = "Warp.ShuffleUpSync";
    if (!__gsharp_warp_shuffle_check_width(width, name, location)) {
        return val;
    }
    uint32_t tid = __gsharp_warp_linear_tid();
    uint32_t laneId = tid % 32u;
    uint32_t logicalLane = laneId % (uint32_t)width;
    if (logicalLane < delta) {
        return val;
    }
    return __gsharp_warp_shuffle_read_lane(__gsharp_ctx, slot, val, tid, laneId - delta, name, location);
}

template<typename TContext, typename T>
inline T __gsharp_warp_shuffle_read_xor_sync(TContext* __gsharp_ctx, uint32_t mask, T val, uint32_t laneMask, int width,
                                             const char* location) {
    uint32_t slot = __gsharp_ctx->__gsharp_warp_shuffle_count++ & 1u;
    const char* name = "Warp.ShuffleXorSync";
    if (!__gsharp_warp_shuffle_check_width(width, name, location)) {
        return val;
    }
    uint32_t tid = __gsharp_warp_linear_tid();
    uint32_t laneId = tid % 32u;
    // CUDA lets a group of width lanes read its own group and the earlier ones; a later group gives the caller's own value
    // (PTX shfl.bfly reads lane j when j <= maxLane, the last lane of the caller's group)
    uint32_t srcLane = laneId ^ laneMask;
    if (srcLane > (laneId | ((uint32_t)width - 1u))) {
        return val;
    }
    return __gsharp_warp_shuffle_read_lane(__gsharp_ctx, slot, val, tid, srcLane, name, location);
}

template<typename TContext, typename T>
inline T __gsharp_warp_shuffle_read_sync(TContext* __gsharp_ctx, uint32_t mask, T val, int32_t srcLane, int width,
                                         const char* location) {
    uint32_t slot = __gsharp_ctx->__gsharp_warp_shuffle_count++ & 1u;
    const char* name = "Warp.ShuffleSync";
    if (!__gsharp_warp_shuffle_check_width(width, name, location)) {
        return val;
    }
    uint32_t tid = __gsharp_warp_linear_tid();
    uint32_t laneId = tid % 32u;
    uint32_t logicalLane = laneId % (uint32_t)width;
    // CUDA reads lane srcLane % width of the caller's segment
    uint32_t srcLogicalLane = (uint32_t)srcLane & (uint32_t)(width - 1);
    return __gsharp_warp_shuffle_read_lane(__gsharp_ctx, slot, val, tid, laneId - logicalLane + srcLogicalLane, name, location);
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
