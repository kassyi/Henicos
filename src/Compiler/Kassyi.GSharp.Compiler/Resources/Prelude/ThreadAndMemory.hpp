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
inline thread_local size_t __gsharp_smem_offsets[64];
inline thread_local bool __gsharp_smem_initialized[64];
inline thread_local size_t __gsharp_smem_current_base;

inline void __gsharp_reset_smem() {
    __gsharp_smem_current_base = 0;
    for (int i = 0; i < 64; i++) {
        __gsharp_smem_initialized[i] = false;
        __gsharp_smem_offsets[i] = 0;
    }
}

inline void* __gsharp_allocate_smem(int allocId, size_t size, size_t alignment) {
    if (!__gsharp_smem_initialized[allocId]) {
        __gsharp_smem_current_base = (__gsharp_smem_current_base + alignment - 1) & ~(alignment - 1);
        __gsharp_smem_offsets[allocId] = __gsharp_smem_current_base;
        __gsharp_smem_current_base += size;
        __gsharp_smem_initialized[allocId] = true;
    }
    return (void*)(__gsharp_dynamic_smem + __gsharp_smem_offsets[allocId]);
}
