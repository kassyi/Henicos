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

// A warp collective of a thread (#1468): the instance of the call it reached and the value it published there. The
// instance is the frame's offset in the kernel context (the path through the device function frames, 0 in the kernel
// body), the number of the call in the body of its function, and how often the frame reached that call. The round is the
// launcher's round in which the thread published it; the name and the place of the call are for the error messages.
struct __gsharp_warp_record {
    uint64_t value;
    uint32_t path;
    uint32_t site;
    uint32_t visit;
    uint32_t round;
    uint32_t mask;
    const char* name;
    const char* location;
};

struct __gsharp_warp_instance {
    uint32_t path;
    uint32_t site;
    uint32_t visit;
};

template<typename TRoot, typename TFrame>
inline uint32_t __gsharp_warp_path_of(TRoot* __gsharp_root, TFrame* __gsharp_ctx) {
    return (uint32_t)(reinterpret_cast<char*>(__gsharp_ctx) - reinterpret_cast<char*>(__gsharp_root));
}

// Publishes the value of the calling lane for this instance of the call, then the lane waits until the lanes of the
// mask have met there (__gsharp_warp_can_run). The kernel context keeps the last two values a lane published: a lane
// publishes at most once per round, and every lane of a group reads in the same round, so the value a slower lane of
// the group still has to read is never overwritten.
template<typename TRoot, typename TFrame, typename T>
inline void __gsharp_warp_publish(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask, T val,
                                  const char* name, const char* location) {
    static_assert(sizeof(T) <= sizeof(uint64_t), "a warp collective exchanges at most 8 bytes");
    __gsharp_warp_record& record = __gsharp_root->__gsharp_warp_records[__gsharp_root->__gsharp_warp_record_next];
    __gsharp_root->__gsharp_warp_record_next ^= 1u;
    record.value = 0u;
    std::memcpy(&record.value, &val, sizeof(T));
    record.path = __gsharp_warp_path_of(__gsharp_root, __gsharp_ctx);
    record.site = site;
    record.visit = ++__gsharp_ctx->__gsharp_warp_visits[site];
    record.round = __gsharp_root->__gsharp_warp_round;
    record.mask = mask;
    record.name = name;
    record.location = location;
    __gsharp_root->__gsharp_warp_waiting = true;
}

// The record of a lane for an instance, or null when the lane has not reached it (a visit count starts at 1, so an empty
// slot never matches)
template<typename TContext>
inline const __gsharp_warp_record* __gsharp_warp_find(const TContext& lane, uint32_t path, uint32_t site,
                                                      uint32_t visit) {
    for (uint32_t slot = 0u; slot < 2u; slot++) {
        const __gsharp_warp_record& record = lane.__gsharp_warp_records[slot];
        if (record.visit == visit && record.site == site && record.path == path) {
            return &record;
        }
    }
    return nullptr;
}

// The lanes of the mask of wait that have not exited and have not published the same instance before the given round
template<typename TContext>
inline uint32_t __gsharp_warp_missing_lanes(const TContext* contexts, uint32_t tid, uint32_t threads, uint32_t round,
                                            const __gsharp_warp_record& wait) {
    uint32_t warpBase = tid & ~31u;
    uint32_t missing = 0u;
    for (uint32_t lane = 0u; lane < 32u; lane++) {
        uint32_t laneTid = warpBase + lane;
        if ((wait.mask & (1u << lane)) == 0u || laneTid >= threads || contexts[laneTid].__gsharp_state == -1) {
            continue;
        }
        const __gsharp_warp_record* record = __gsharp_warp_find(contexts[laneTid], wait.path, wait.site, wait.visit);
        if (record == nullptr || record->round >= round) {
            missing |= 1u << lane;
        }
    }
    return missing;
}

// Whether thread tid of the block takes its next step in this round: it does not wait at a warp collective, or every
// lane of the mask that has not exited published the same instance in an earlier round (CUDA evaluates a collective
// for the lanes of the mask that have not exited). The lanes of a group all go on in the same round.
template<typename TContext>
inline bool __gsharp_warp_can_run(TContext* contexts, uint32_t tid, uint32_t threads, uint32_t round) {
    TContext& self = contexts[tid];
    self.__gsharp_warp_round = round;
    return !self.__gsharp_warp_waiting ||
           __gsharp_warp_missing_lanes(contexts, tid, threads, round,
                                       self.__gsharp_warp_records[self.__gsharp_warp_record_next ^ 1u]) == 0u;
}

// No thread of the block could run in a round while some wait at a warp collective: a lane of their mask that has not
// exited never reaches the same call with them, which CUDA leaves undefined. Reports the first such lane
template<typename TContext>
inline void __gsharp_warp_report_stall(const TContext* contexts, uint32_t threads) {
    for (uint32_t tid = 0u; tid < threads; tid++) {
        const TContext& self = contexts[tid];
        if (self.__gsharp_state == -1 || !self.__gsharp_warp_waiting) {
            continue;
        }
        const __gsharp_warp_record& wait = self.__gsharp_warp_records[self.__gsharp_warp_record_next ^ 1u];
        uint32_t missing = __gsharp_warp_missing_lanes(contexts, tid, threads, UINT32_MAX, wait);
        if (missing == 0u) {
            continue;
        }
        uint32_t first = 0u;
        while ((missing & (1u << first)) == 0u) {
            first++;
        }
        const TContext& other = contexts[(tid & ~31u) + first];
        char where[320];
        if (other.__gsharp_warp_waiting) {
            const __gsharp_warp_record& otherWait = other.__gsharp_warp_records[other.__gsharp_warp_record_next ^ 1u];
            std::snprintf(where, sizeof(where), "waits at %s at %s", otherWait.name, otherWait.location);
        } else if (other.__gsharp_yielded_for_block_barrier) {
            std::snprintf(where, sizeof(where), "waits at Gpu.SyncThreads()");
        } else if (other.__gsharp_yielded_for_grid_sync) {
            std::snprintf(where, sizeof(where), "waits at Gpu.SyncGrid()");
        } else {
            std::snprintf(where, sizeof(where), "has not reached it");
        }
        char text[1024];
        std::snprintf(text, sizeof(text),
                      "G#: %s at %s in lane %u of warp %u waits for lanes 0x%08X of its mask 0x%08X, which never reach "
                      "the same call: lane %u %s (CUDA leaves the result undefined unless every lane of the mask that "
                      "has not exited calls it with the same mask)",
                      wait.name, wait.location, tid & 31u, tid / 32u, missing, wait.mask, first, where);
        __gsharp_trap(GSharpErrorCode::InvalidOperation, text);
        return;
    }
    __gsharp_trap(GSharpErrorCode::InvalidOperation,
                  "G#: the threads of a block wait for each other at warp collectives that never complete");
}

// The start of a read: the lane no longer waits, and the instance it reads is the one it published
template<typename TRoot, typename TFrame>
inline __gsharp_warp_instance __gsharp_warp_begin_read(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site) {
    __gsharp_root->__gsharp_warp_waiting = false;
    return {__gsharp_warp_path_of(__gsharp_root, __gsharp_ctx), site, __gsharp_ctx->__gsharp_warp_visits[site]};
}

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

// Gpu.LaneId() and Gpu.WarpId(): a warp holds 32 consecutive linear thread indices of the block, as on the GPU (#845)
inline int __gsharp_lane_id() { return (int)(__gsharp_warp_linear_tid() & 31u); }
inline int __gsharp_warp_id() { return (int)(__gsharp_warp_linear_tid() >> 5); }

// Reads the value that lane srcLane of the caller's warp published for the same instance of the call. Reading a lane
// that does not exist in the block, that has exited or that does not take part in the call, or one that called it with
// another mask, is undefined on CUDA and reports an InvalidOperationException
template<typename TRoot, typename T>
inline T __gsharp_warp_shuffle_read_lane(TRoot* __gsharp_root, const __gsharp_warp_instance& instance, uint32_t mask,
                                         T val, uint32_t tid, uint32_t srcLane, const char* name,
                                         const char* location) {
    uint32_t threads = blockDim.x * blockDim.y * blockDim.z;
    uint32_t warpBase = tid & ~31u;
    uint32_t srcTid = warpBase + srcLane;
    char text[640];
    if (srcTid >= threads) {
        std::snprintf(text, sizeof(text),
                      "G#: %s at %s reads lane %u of warp %u, which does not exist in a block of %u threads "
                      "(CUDA leaves the value undefined. Keep the source lane within the block by limiting delta, srcLane or laneMask, or launch a multiple of 32 threads; the mask does not limit which lane is read)",
                      name, location, srcLane, warpBase / 32u, threads);
        __gsharp_trap(GSharpErrorCode::InvalidOperation, text);
        return val;
    }
    const auto& source = __gsharp_root->__gsharp_all_contexts[srcTid];
    const __gsharp_warp_record* record = __gsharp_warp_find(source, instance.path, instance.site, instance.visit);
    if (record == nullptr) {
        if (source.__gsharp_state == -1) {
            std::snprintf(text, sizeof(text),
                          "G#: %s at %s in lane %u reads lane %u of warp %u, which has exited (CUDA leaves the value "
                          "of a lane that does not take part undefined)",
                          name, location, tid & 31u, srcLane, warpBase / 32u);
        } else {
            std::snprintf(text, sizeof(text),
                          "G#: %s at %s in lane %u reads lane %u of warp %u, which does not take part: it is not in "
                          "the mask 0x%08X and has not reached the same call (CUDA leaves the value of a lane that "
                          "does not take part undefined)",
                          name, location, tid & 31u, srcLane, warpBase / 32u, mask);
        }
        __gsharp_trap(GSharpErrorCode::InvalidOperation, text);
        return val;
    }
    if (record->mask != mask) {
        std::snprintf(text, sizeof(text),
                      "G#: %s at %s has mask 0x%08X in lane %u and mask 0x%08X in lane %u of warp %u (CUDA leaves "
                      "the result undefined unless the lanes call it with the same mask)",
                      name, location, mask, tid & 31u, record->mask, srcLane, warpBase / 32u);
        __gsharp_trap(GSharpErrorCode::InvalidOperation, text);
        return val;
    }
    T result;
    std::memcpy(&result, &record->value, sizeof(T));
    return result;
}

template<typename TRoot, typename TFrame, typename T>
inline T __gsharp_warp_shuffle_read_down_sync(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask,
                                              T val, uint32_t delta, int width, const char* location) {
    __gsharp_warp_instance instance = __gsharp_warp_begin_read(__gsharp_root, __gsharp_ctx, site);
    const char* name = "Warp.ShuffleDownSync";
    if (!__gsharp_warp_shuffle_check_width(width, name, location)) {
        return val;
    }
    uint32_t tid = __gsharp_warp_linear_tid();
    uint32_t laneId = tid % 32u;
    uint32_t logicalLane = laneId % (uint32_t)width;
    // PTX shfl.sync uses only the low 5 bits of the lane offset
    uint32_t srcLogicalLane = logicalLane + (delta & 31u);
    if (srcLogicalLane >= (uint32_t)width) {
        return val;
    }
    uint32_t srcLaneId = laneId - logicalLane + srcLogicalLane;
    return __gsharp_warp_shuffle_read_lane(__gsharp_root, instance, mask, val, tid, srcLaneId, name, location);
}

template<typename TRoot, typename TFrame, typename T>
inline T __gsharp_warp_shuffle_read_up_sync(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask,
                                            T val, uint32_t delta, int width, const char* location) {
    __gsharp_warp_instance instance = __gsharp_warp_begin_read(__gsharp_root, __gsharp_ctx, site);
    const char* name = "Warp.ShuffleUpSync";
    if (!__gsharp_warp_shuffle_check_width(width, name, location)) {
        return val;
    }
    uint32_t tid = __gsharp_warp_linear_tid();
    uint32_t laneId = tid % 32u;
    uint32_t logicalLane = laneId % (uint32_t)width;
    // PTX shfl.sync uses only the low 5 bits of the lane offset
    uint32_t offset = delta & 31u;
    if (logicalLane < offset) {
        return val;
    }
    return __gsharp_warp_shuffle_read_lane(__gsharp_root, instance, mask, val, tid, laneId - offset, name, location);
}

template<typename TRoot, typename TFrame, typename T>
inline T __gsharp_warp_shuffle_read_xor_sync(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask,
                                             T val, uint32_t laneMask, int width, const char* location) {
    __gsharp_warp_instance instance = __gsharp_warp_begin_read(__gsharp_root, __gsharp_ctx, site);
    const char* name = "Warp.ShuffleXorSync";
    if (!__gsharp_warp_shuffle_check_width(width, name, location)) {
        return val;
    }
    uint32_t tid = __gsharp_warp_linear_tid();
    uint32_t laneId = tid % 32u;
    // CUDA lets a group of width lanes read its own group and the earlier ones; a later group gives the caller's own value
    // (PTX shfl.bfly reads lane j when j <= maxLane, the last lane of the caller's group). PTX uses only the low 5 bits
    // of the lane mask
    uint32_t srcLane = laneId ^ (laneMask & 31u);
    if (srcLane > (laneId | ((uint32_t)width - 1u))) {
        return val;
    }
    return __gsharp_warp_shuffle_read_lane(__gsharp_root, instance, mask, val, tid, srcLane, name, location);
}

template<typename TRoot, typename TFrame, typename T>
inline T __gsharp_warp_shuffle_read_sync(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask,
                                         T val, int32_t srcLane, int width, const char* location) {
    __gsharp_warp_instance instance = __gsharp_warp_begin_read(__gsharp_root, __gsharp_ctx, site);
    const char* name = "Warp.ShuffleSync";
    if (!__gsharp_warp_shuffle_check_width(width, name, location)) {
        return val;
    }
    uint32_t tid = __gsharp_warp_linear_tid();
    uint32_t laneId = tid % 32u;
    uint32_t logicalLane = laneId % (uint32_t)width;
    // CUDA reads lane srcLane % width of the caller's segment
    uint32_t srcLogicalLane = (uint32_t)srcLane & (uint32_t)(width - 1);
    return __gsharp_warp_shuffle_read_lane(__gsharp_root, instance, mask, val, tid,
                                           laneId - logicalLane + srcLogicalLane, name, location);
}

template<typename T>
inline bool __gsharp_raw_bits_equal(T a, T b) {
    if constexpr (sizeof(T) == sizeof(uint32_t)) {
        uint32_t ra = 0, rb = 0;
        std::memcpy(&ra, &a, sizeof(T));
        std::memcpy(&rb, &b, sizeof(T));
        return ra == rb;
    } else if constexpr (sizeof(T) == sizeof(uint64_t)) {
        uint64_t ra = 0, rb = 0;
        std::memcpy(&ra, &a, sizeof(T));
        std::memcpy(&rb, &b, sizeof(T));
        return ra == rb;
    } else {
        return std::memcmp(&a, &b, sizeof(T)) == 0;
    }
}

// The lanes that take part in a vote, reduction, match or Gpu.SyncWarp (#845): the lanes of the mask that reached the
// same instance of the call. A lane of the mask that has exited does not take part, as CUDA evaluates the call for the
// lanes of the mask that have not exited. The caller must be in the mask and every lane must pass the same mask (CUDA
// leaves the result undefined otherwise). ManagedGpuContext computes the same.
template<typename TRoot>
inline uint32_t __gsharp_warp_vote_lanes(TRoot* __gsharp_root, const __gsharp_warp_instance& instance, uint32_t mask,
                                         const char* name, const char* location) {
    uint32_t tid = __gsharp_warp_linear_tid();
    uint32_t laneId = tid & 31u;
    char text[512];
    if ((mask & (1u << laneId)) == 0u) {
        std::snprintf(text, sizeof(text),
                      "G#: %s at %s has mask 0x%08X without caller lane %u (CUDA leaves the result undefined)",
                      name, location, mask, laneId);
        __gsharp_trap(GSharpErrorCode::InvalidOperation, text);
        return 0u;
    }
    uint32_t threads = blockDim.x * blockDim.y * blockDim.z;
    uint32_t warpBase = tid & ~31u;
    uint32_t lanes = 0u;
    for (uint32_t lane = 0u; lane < 32u; lane++) {
        if ((mask & (1u << lane)) == 0u || warpBase + lane >= threads) {
            continue;
        }
        const __gsharp_warp_record* record = __gsharp_warp_find(__gsharp_root->__gsharp_all_contexts[warpBase + lane],
                                                                instance.path, instance.site, instance.visit);
        if (record == nullptr) {
            continue;
        }
        if (record->mask != mask) {
            std::snprintf(text, sizeof(text),
                          "G#: %s at %s has mask 0x%08X in lane %u and mask 0x%08X in lane %u of warp %u (CUDA leaves "
                          "the result undefined unless the lanes call it with the same mask)",
                          name, location, mask, laneId, record->mask, lane, warpBase / 32u);
            __gsharp_trap(GSharpErrorCode::InvalidOperation, text);
            return 0u;
        }
        lanes |= 1u << lane;
    }
    return lanes;
}

template<typename TRoot, typename TFrame, typename T>
inline uint32_t __gsharp_warp_match_any_read_sync(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site,
                                                  uint32_t mask, T val, const char* location) {
    __gsharp_warp_instance instance = __gsharp_warp_begin_read(__gsharp_root, __gsharp_ctx, site);
    const char* name = "Gpu.MatchAnySync";
    uint32_t lanes = __gsharp_warp_vote_lanes(__gsharp_root, instance, mask, name, location);
    uint32_t tid = __gsharp_warp_linear_tid();
    uint32_t result = 0u;
    for (uint32_t srcLane = 0u; srcLane < 32u; srcLane++) {
        if ((lanes & (1u << srcLane)) != 0u &&
            __gsharp_raw_bits_equal(
                __gsharp_warp_shuffle_read_lane(__gsharp_root, instance, mask, val, tid, srcLane, name, location), val)) {
            result |= (1u << srcLane);
        }
    }
    return result;
}

template<typename TRoot, typename TFrame, typename T>
inline uint32_t __gsharp_warp_match_all_read_sync(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site,
                                                  uint32_t mask, T val, int* pred, const char* location) {
    __gsharp_warp_instance instance = __gsharp_warp_begin_read(__gsharp_root, __gsharp_ctx, site);
    const char* name = "Gpu.MatchAllSync";
    uint32_t lanes = __gsharp_warp_vote_lanes(__gsharp_root, instance, mask, name, location);
    uint32_t tid = __gsharp_warp_linear_tid();
    bool allEqual = true;
    for (uint32_t srcLane = 0u; srcLane < 32u && allEqual; srcLane++) {
        if ((lanes & (1u << srcLane)) != 0u &&
            !__gsharp_raw_bits_equal(
                __gsharp_warp_shuffle_read_lane(__gsharp_root, instance, mask, val, tid, srcLane, name, location), val)) {
            allEqual = false;
        }
    }
    if (pred) *pred = allEqual ? 1 : 0;
    return allEqual ? lanes : 0u;
}

// Gpu.SyncWarp(mask): the lanes of the mask meet, with the checks of a vote
template<typename TRoot, typename TFrame>
inline void __gsharp_warp_sync_read(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask,
                                    const char* location) {
    __gsharp_warp_instance instance = __gsharp_warp_begin_read(__gsharp_root, __gsharp_ctx, site);
    __gsharp_warp_vote_lanes(__gsharp_root, instance, mask, "Gpu.SyncWarp", location);
}

// The lanes of lanes whose published predicate is true
template<typename TRoot>
inline uint32_t __gsharp_warp_ballot_lanes(TRoot* __gsharp_root, const __gsharp_warp_instance& instance, uint32_t mask,
                                           uint32_t lanes, bool pred, const char* name, const char* location) {
    uint32_t tid = __gsharp_warp_linear_tid();
    uint32_t result = 0u;
    for (uint32_t srcLane = 0u; srcLane < 32u; srcLane++) {
        if ((lanes & (1u << srcLane)) != 0u &&
            __gsharp_warp_shuffle_read_lane(__gsharp_root, instance, mask, pred, tid, srcLane, name, location)) {
            result |= (1u << srcLane);
        }
    }
    return result;
}

template<typename TRoot, typename TFrame>
inline uint32_t __gsharp_warp_ballot_read_sync(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask,
                                               bool pred, const char* location) {
    __gsharp_warp_instance instance = __gsharp_warp_begin_read(__gsharp_root, __gsharp_ctx, site);
    const char* name = "Warp.BallotSync";
    uint32_t lanes = __gsharp_warp_vote_lanes(__gsharp_root, instance, mask, name, location);
    return __gsharp_warp_ballot_lanes(__gsharp_root, instance, mask, lanes, pred, name, location);
}

template<typename TRoot, typename TFrame>
inline bool __gsharp_warp_all_read_sync(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask,
                                        bool pred, const char* location) {
    __gsharp_warp_instance instance = __gsharp_warp_begin_read(__gsharp_root, __gsharp_ctx, site);
    const char* name = "Warp.AllSync";
    uint32_t lanes = __gsharp_warp_vote_lanes(__gsharp_root, instance, mask, name, location);
    return __gsharp_warp_ballot_lanes(__gsharp_root, instance, mask, lanes, pred, name, location) == lanes;
}

template<typename TRoot, typename TFrame>
inline bool __gsharp_warp_any_read_sync(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask,
                                        bool pred, const char* location) {
    __gsharp_warp_instance instance = __gsharp_warp_begin_read(__gsharp_root, __gsharp_ctx, site);
    const char* name = "Warp.AnySync";
    uint32_t lanes = __gsharp_warp_vote_lanes(__gsharp_root, instance, mask, name, location);
    return __gsharp_warp_ballot_lanes(__gsharp_root, instance, mask, lanes, pred, name, location) != 0u;
}

// The published values of the lanes that take part, combined in lane order
template<typename TRoot, typename TFrame, typename T, typename TCombine>
inline T __gsharp_warp_reduce_lanes(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask, T val,
                                    const char* name, const char* location, TCombine combine) {
    __gsharp_warp_instance instance = __gsharp_warp_begin_read(__gsharp_root, __gsharp_ctx, site);
    uint32_t lanes = __gsharp_warp_vote_lanes(__gsharp_root, instance, mask, name, location);
    uint32_t tid = __gsharp_warp_linear_tid();
    T result = val;
    bool first = true;
    for (uint32_t srcLane = 0u; srcLane < 32u; srcLane++) {
        if ((lanes & (1u << srcLane)) != 0u) {
            T laneVal = __gsharp_warp_shuffle_read_lane(__gsharp_root, instance, mask, val, tid, srcLane, name, location);
            result = first ? laneVal : combine(result, laneVal);
            first = false;
        }
    }
    return result;
}

// The sum wraps around in 32 bits, as on the GPU, also for int
template<typename TRoot, typename TFrame, typename T>
inline T __gsharp_warp_reduce_add_read_sync(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask,
                                            T val, const char* location) {
    return __gsharp_warp_reduce_lanes(__gsharp_root, __gsharp_ctx, site, mask, val, "Warp.ReduceAddSync", location,
                                      [](T a, T b) { return (T)((uint32_t)a + (uint32_t)b); });
}

// Compared with the sign of T: signed for int, unsigned for uint
template<typename TRoot, typename TFrame, typename T>
inline T __gsharp_warp_reduce_min_read_sync(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask,
                                            T val, const char* location) {
    return __gsharp_warp_reduce_lanes(__gsharp_root, __gsharp_ctx, site, mask, val, "Warp.ReduceMinSync", location,
                                      [](T a, T b) { return b < a ? b : a; });
}

template<typename TRoot, typename TFrame, typename T>
inline T __gsharp_warp_reduce_max_read_sync(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask,
                                            T val, const char* location) {
    return __gsharp_warp_reduce_lanes(__gsharp_root, __gsharp_ctx, site, mask, val, "Warp.ReduceMaxSync", location,
                                      [](T a, T b) { return a < b ? b : a; });
}

template<typename TRoot, typename TFrame, typename T>
inline T __gsharp_warp_reduce_and_read_sync(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask,
                                            T val, const char* location) {
    return __gsharp_warp_reduce_lanes(__gsharp_root, __gsharp_ctx, site, mask, val, "Warp.ReduceAndSync", location,
                                      [](T a, T b) { return (T)(a & b); });
}

template<typename TRoot, typename TFrame, typename T>
inline T __gsharp_warp_reduce_or_read_sync(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask,
                                           T val, const char* location) {
    return __gsharp_warp_reduce_lanes(__gsharp_root, __gsharp_ctx, site, mask, val, "Warp.ReduceOrSync", location,
                                      [](T a, T b) { return (T)(a | b); });
}

template<typename TRoot, typename TFrame, typename T>
inline T __gsharp_warp_reduce_xor_read_sync(TRoot* __gsharp_root, TFrame* __gsharp_ctx, uint32_t site, uint32_t mask,
                                            T val, const char* location) {
    return __gsharp_warp_reduce_lanes(__gsharp_root, __gsharp_ctx, site, mask, val, "Warp.ReduceXorSync", location,
                                      [](T a, T b) { return (T)(a ^ b); });
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
