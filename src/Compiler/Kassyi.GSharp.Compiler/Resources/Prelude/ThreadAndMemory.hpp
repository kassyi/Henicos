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
inline thread_local char __gsharp_dynamic_smem[65536];
// [WHY] A SharedMemory<T> declaration is one buffer per block, like a CUDA __shared__ variable: every call of a device
// function, from any call site, gets the same buffer. The compiler identifies each declaration by a site key (a hash of
// the declaring method and the ordinal of the declaration in it), so buffers do not depend on the order of allocations.
#define __GSHARP_SMEM_MAX_SITES 64
inline thread_local unsigned long long __gsharp_smem_keys[__GSHARP_SMEM_MAX_SITES];
inline thread_local size_t __gsharp_smem_offsets[__GSHARP_SMEM_MAX_SITES];
inline thread_local int __gsharp_smem_site_count;
inline thread_local size_t __gsharp_smem_current_base;

inline void __gsharp_reset_smem() {
    __gsharp_smem_current_base = 0;
    __gsharp_smem_site_count = 0;
}

inline void* __gsharp_allocate_smem(unsigned long long siteKey, size_t size, size_t alignment) {
    for (int i = 0; i < __gsharp_smem_site_count; i++) {
        if (__gsharp_smem_keys[i] == siteKey) {
            return (void*)(__gsharp_dynamic_smem + __gsharp_smem_offsets[i]);
        }
    }
    size_t offset = (__gsharp_smem_current_base + alignment - 1) & ~(alignment - 1);
    if (__gsharp_smem_site_count == __GSHARP_SMEM_MAX_SITES || offset + size > sizeof(__gsharp_dynamic_smem)) {
        std::fprintf(stderr, "G#: shared memory of the block exceeds %d declarations or %zu bytes\n",
                     __GSHARP_SMEM_MAX_SITES, sizeof(__gsharp_dynamic_smem));
        std::abort();
    }
    __gsharp_smem_keys[__gsharp_smem_site_count] = siteKey;
    __gsharp_smem_offsets[__gsharp_smem_site_count] = offset;
    __gsharp_smem_site_count++;
    __gsharp_smem_current_base = offset + size;
    return (void*)(__gsharp_dynamic_smem + offset);
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
