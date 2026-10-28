// --- Thread & Block State ---
thread_local uint3 threadIdx;
thread_local uint3 blockIdx;
thread_local dim3 blockDim;
thread_local dim3 gridDim;

extern "C" __declspec(dllexport) void __gsharp_set_thread_idx(uint32_t tx, uint32_t ty, uint32_t tz, uint32_t bx, uint32_t by, uint32_t bz, uint32_t dx, uint32_t dy, uint32_t dz, uint32_t gx, uint32_t gy, uint32_t gz) {
    threadIdx.x = tx; threadIdx.y = ty; threadIdx.z = tz;
    blockIdx.x = bx; blockIdx.y = by; blockIdx.z = bz;
    blockDim.x = dx; blockDim.y = dy; blockDim.z = dz;
    gridDim.x = gx; gridDim.y = gy; gridDim.z = gz;
}

namespace cg {
    struct grid_group {
        void sync() {}
    };
    inline grid_group this_grid() { return grid_group(); }
}

inline void __syncthreads() {}

// --- Shared Memory ---
// [WHY] A SharedMemory<T> declaration is one buffer per block, like a CUDA __shared__ variable: every call of a device
// function, from any call site, gets the same buffer. The compiler identifies each declaration by a site key (a hash of
// the declaring method and the ordinal of the declaration in it), so buffers do not depend on the order of allocations.
// Each OpenMP thread owns one arena and resets it at the start of each block it runs (#544); the launcher of a cooperative
// kernel instead gives every block an arena of its own, which keeps the block's shared memory across its grid syncs (#664):
// - A dynamic declaration (a count that is not a C# constant) is a region of the launch's sharedMemBytes, placed with the
//   arithmetic of __gsharp_dsmem_place in Prelude.cu: aligned to max(alignof(T), 16), a launch that does not give it
//   enough bytes is an ArgumentOutOfRangeException, and a region that grows after a later region is placed is an
//   InvalidOperationException.
// - A static declaration (a constant count) has a buffer of its own, which CUDA does not count in sharedMemBytes either.
// The CPU has no limit of its own on the bytes or the number of declarations. The contents are not cleared between blocks
// (CUDA leaves them undefined too). A placement that fails ends the step (__gsharp_trap_abort): there is no memory to
// hand out, so the thread must not go on.

// Every buffer is aligned to 64 bytes, which covers alignof(T) of every type a kernel can declare
constexpr size_t __gsharp_smem_alignment = 64;

inline unsigned char* __gsharp_smem_alloc(size_t bytes) {
    return static_cast<unsigned char*>(::operator new(bytes > 0 ? bytes : 1, std::align_val_t(__gsharp_smem_alignment), std::nothrow));
}

inline void __gsharp_smem_free(unsigned char* data) {
    ::operator delete(data, std::align_val_t(__gsharp_smem_alignment));
}

struct __gsharp_smem_region {
    unsigned long long key;
    size_t offset;
    size_t size;
};

struct __gsharp_smem_static_buffer {
    unsigned long long key;
    size_t size;
    unsigned char* data;
};

struct __gsharp_block_smem {
    __gsharp_block_smem() = default;
    // The arena owns its buffers
    __gsharp_block_smem(const __gsharp_block_smem&) = delete;
    __gsharp_block_smem& operator=(const __gsharp_block_smem&) = delete;

    unsigned char* dynamic_data = nullptr;
    size_t dynamic_capacity = 0;
    size_t dynamic_limit = 0;
    size_t dynamic_cursor = 0;
    std::vector<__gsharp_smem_region> regions;
    // Kept across blocks: a static declaration's buffer is allocated once per arena
    std::vector<__gsharp_smem_static_buffer> static_buffers;
    // The buffers that a static declaration outgrew (another instantiation of a generic device function with a larger T):
    // a thread may still hold a pointer into them, so they live as long as the arena
    std::vector<unsigned char*> outgrown_buffers;

    ~__gsharp_block_smem() {
        __gsharp_smem_free(dynamic_data);
        for (auto& buffer : static_buffers) {
            __gsharp_smem_free(buffer.data);
        }
        for (unsigned char* buffer : outgrown_buffers) {
            __gsharp_smem_free(buffer);
        }
    }

    // Starts a block with sharedMemBytes of dynamic shared memory. The buffer itself is allocated at the first dynamic
    // placement, inside a kernel step, where a failure reaches the error slot of the launch
    void reset(size_t sharedMemBytes) {
        regions.clear();
        dynamic_cursor = 0;
        dynamic_limit = sharedMemBytes;
    }

    // The dynamic buffer of the block, allocated before its first region is placed and never moved while it has regions
    unsigned char* dynamic_buffer() {
        if (dynamic_capacity < dynamic_limit) {
            __gsharp_smem_free(dynamic_data);
            dynamic_data = __gsharp_smem_alloc(dynamic_limit);
            if (dynamic_data == nullptr) {
                dynamic_capacity = 0;
                __gsharp_trap_abort(GSharpErrorCode::ArgumentOutOfRange, "G#: the dynamic shared memory of a block (sharedMemBytes) could not be allocated");
            }
            dynamic_capacity = dynamic_limit;
        }
        return dynamic_data;
    }
};

inline thread_local __gsharp_block_smem __gsharp_thread_smem;
// The arena of the block that the calling OpenMP thread runs, when it is not the thread's own (a cooperative kernel)
inline thread_local __gsharp_block_smem* __gsharp_bound_smem = nullptr;

inline __gsharp_block_smem& __gsharp_current_smem() {
    return __gsharp_bound_smem != nullptr ? *__gsharp_bound_smem : __gsharp_thread_smem;
}

// Starts the block that the calling OpenMP thread runs next, in the thread's own arena
inline void __gsharp_reset_smem(uint32_t sharedMemBytes) {
    __gsharp_bound_smem = nullptr;
    __gsharp_thread_smem.reset(sharedMemBytes);
}

// Runs the next step of a block of a cooperative kernel in the block's own arena; nullptr returns to the thread's own
inline void __gsharp_bind_smem(__gsharp_block_smem* arena) {
    __gsharp_bound_smem = arena;
}

// A declaration whose count is a C# constant: a buffer of its own, not counted in sharedMemBytes
inline void* __gsharp_allocate_smem_static(unsigned long long siteKey, size_t bytes, size_t alignment) {
    __gsharp_block_smem& arena = __gsharp_current_smem();
    (void)alignment; // every buffer is aligned to __gsharp_smem_alignment
    for (auto& buffer : arena.static_buffers) {
        if (buffer.key == siteKey) {
            if (bytes > buffer.size) {
                // The same declaration in another instantiation of a generic device function, with a larger T
                unsigned char* grown = __gsharp_smem_alloc(bytes);
                if (grown == nullptr) {
                    __gsharp_trap_abort(GSharpErrorCode::InvalidOperation, "G#: the static shared memory of a declaration could not be allocated");
                }
                arena.outgrown_buffers.push_back(buffer.data);
                buffer.data = grown;
                buffer.size = bytes;
            }
            return buffer.data;
        }
    }
    unsigned char* data = __gsharp_smem_alloc(bytes);
    if (data == nullptr) {
        __gsharp_trap_abort(GSharpErrorCode::InvalidOperation, "G#: the static shared memory of a declaration could not be allocated");
    }
    arena.static_buffers.push_back({siteKey, bytes, data});
    return data;
}

// A declaration whose count is known at run time: a region of the launch's sharedMemBytes, with the rules of
// __gsharp_dsmem_place (Prelude.cu). place is the declaration in the C# source, for the message
inline void* __gsharp_allocate_smem_dynamic(unsigned long long siteKey, unsigned long long bytes, size_t alignment,
                                            const char* place) {
    __gsharp_block_smem& arena = __gsharp_current_smem();
    size_t align = alignment > 16 ? alignment : 16;
    __gsharp_smem_region* region = nullptr;
    for (auto& candidate : arena.regions) {
        if (candidate.key == siteKey) {
            region = &candidate;
            break;
        }
    }
    if (region == nullptr) {
        size_t offset = (arena.dynamic_cursor + align - 1) & ~(align - 1);
        arena.regions.push_back({siteKey, offset, 0});
        region = &arena.regions.back();
        // An empty region ends at its own offset and stays the last one until another region is placed
        arena.dynamic_cursor = offset;
    } else if (bytes > region->size && region->offset + region->size != arena.dynamic_cursor) {
        char text[512];
        std::snprintf(text, sizeof(text), "G#: a shared memory declaration grew after a later declaration (%s)", place);
        __gsharp_trap_abort(GSharpErrorCode::InvalidOperation, text);
    }
    if (bytes > region->size) {
        // [WHY] bytes is checked alone first: a negative count arrives as at least 2^63 bytes, and adding the offset to it
        // would wrap past 2^64 to a small end
        unsigned long long limit = arena.dynamic_limit;
        if (bytes > limit || (unsigned long long)region->offset + bytes > limit) {
            char text[512];
            std::snprintf(text, sizeof(text),
                          "G#: dynamic shared memory exceeds sharedMemBytes (%s needs %llu bytes at offset %llu; sharedMemBytes is %llu)",
                          place, bytes, (unsigned long long)region->offset, limit);
            __gsharp_trap_abort(GSharpErrorCode::ArgumentOutOfRange, text);
        }
        region->size = (size_t)bytes;
        arena.dynamic_cursor = region->offset + (size_t)bytes;
    }
    return arena.dynamic_buffer() + region->offset;
}

// --- Grid syncs of cooperative kernels (#664) ---
// [WHY] The native launcher of a cooperative kernel runs the grid in phases: in each phase every block runs until all of
// its threads have reached Gpu.SyncGrid or finished, and the next phase starts when every block has reached the grid
// sync. The implicit barrier at the end of the OpenMP loop over the blocks is the grid barrier, so the result does not
// depend on the number of OpenMP threads or their schedule. On the GPU a block that finishes while the other blocks wait,
// or a block whose thread 0 finishes while its other threads wait, never arrives at the grid sync, which then never
// completes; the CPU paths report both.
constexpr uint8_t __gsharp_block_at_grid_sync = 1;
constexpr uint8_t __gsharp_block_finished = 2;

// Reports thread 0 of a block that finished while the block's other threads wait at a grid sync
inline void __gsharp_report_representative_finished(GSharpErrorSlot* error, int32_t bx, int32_t by, int32_t bz,
                                                    const char* kernel) {
    char text[512];
    std::snprintf(text, sizeof(text),
                  "G#: thread (0,0,0) of block (%d,%d,%d) of %s finished while other threads of its block wait at Gpu.SyncGrid; on the GPU the grid sync never completes",
                  bx, by, bz, kernel);
    error->Report(GSharpErrorCode::InvalidOperation, text);
}

// Ends a phase of the grid: true when every block waits at a grid sync, so that the next phase starts; false when every
// block has finished or the launch failed. A phase in which some blocks finished while others wait is reported
inline bool __gsharp_end_grid_phase(GSharpErrorSlot* error, const uint8_t* status, int32_t totalBlocks,
                                    uint32_t gridDimX, uint32_t gridDimY, const char* kernel) {
    if (error->HasError()) {
        return false;
    }
    int32_t firstFinished = -1;
    bool anyWaiting = false;
    for (int32_t block = 0; block < totalBlocks; block++) {
        if (status[block] == __gsharp_block_at_grid_sync) {
            anyWaiting = true;
        } else if (firstFinished < 0) {
            firstFinished = block;
        }
    }
    if (anyWaiting && firstFinished >= 0) {
        int32_t x = (int32_t)(firstFinished % gridDimX);
        int32_t y = (int32_t)((firstFinished / gridDimX) % gridDimY);
        int32_t z = (int32_t)(firstFinished / (gridDimX * gridDimY));
        char text[512];
        std::snprintf(text, sizeof(text),
                      "G#: block (%d,%d,%d) of %s finished while other blocks wait at Gpu.SyncGrid; on the GPU the grid sync never completes",
                      x, y, z, kernel);
        error->Report(GSharpErrorCode::InvalidOperation, text);
        return false;
    }
    return anyWaiting;
}

inline void __gsharp_unsupported(const char* message);

inline void __gsharp_mbarrier_init(unsigned long long*, unsigned int) {
    __gsharp_unsupported("mbarrier is not supported by the StandardCpp dialect.");
}
inline void __gsharp_mbarrier_arrive(unsigned long long*) {
    __gsharp_unsupported("mbarrier is not supported by the StandardCpp dialect.");
}
inline void __gsharp_mbarrier_wait(unsigned long long*, unsigned int) {
    __gsharp_unsupported("mbarrier is not supported by the StandardCpp dialect.");
}

template <typename T>
inline void __gsharp_cp_async(T* dst, const T* src) {
    *dst = *src;
}
inline void __gsharp_cp_async_commit_group() {}
inline void __gsharp_cp_async_wait_group(int) {}
inline void __gsharp_cp_async_wait_all() {}
