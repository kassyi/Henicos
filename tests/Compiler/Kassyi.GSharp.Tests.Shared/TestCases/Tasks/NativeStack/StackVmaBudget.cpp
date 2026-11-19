extern "C" __attribute__((visibility("default")))
size_t ProbeAvailableVmas() {
    return __gsharp_linux_available_vmas();
}

extern "C" __attribute__((visibility("default")))
int ProbeThreadLimit(uint64_t threadsPerBlock, int64_t totalBlocks, size_t vmaBudgetOverride) {
    return __gsharp_stack_thread_limit(threadsPerBlock, totalBlocks, vmaBudgetOverride);
}

extern "C" __attribute__((visibility("default")))
int ProbeMaxOmpThreads() {
#ifdef _OPENMP
    return omp_get_max_threads();
#else
    return 1;
#endif
}

extern "C" __attribute__((visibility("default")))
void RunMultiBlockWithBudget(
    int64_t totalBlocks,
    uint32_t threadsPerBlock,
    size_t vmaBudgetOverride,
    int* output,
    int* observedThreads) {
    *observedThreads = 0;
    auto body = [&]() {
        uint32_t tid = threadIdx.x;
        uint32_t bid = blockIdx.x;
        uint32_t bdim = blockDim.x;
        int* smem = static_cast<int*>(
            __gsharp_allocate_smem_static(1ULL, static_cast<size_t>(bdim) * sizeof(int), alignof(int)));
        smem[tid] = static_cast<int>((bid + 1u) * 1000u + tid + 1u);
        __gsharp_stack_barrier();
        uint32_t next = (tid + 1u) % bdim;
        output[static_cast<size_t>(bid) * bdim + tid] = smem[next];
    };

    #pragma omp parallel \
        num_threads(__gsharp_stack_thread_limit(static_cast<uint64_t>(threadsPerBlock), totalBlocks, vmaBudgetOverride))
    {
        #pragma omp single
        {
#ifdef _OPENMP
            *observedThreads = omp_get_num_threads();
#else
            *observedThreads = 1;
#endif
        }

        __gsharp_stack_pool stacks;
        #pragma omp for schedule(dynamic)
        for (int64_t b = 0; b < totalBlocks; ++b) {
            __gsharp_reset_smem(0);
            __gsharp_stack_block block(stacks, threadsPerBlock, body);
            __gsharp_stack_thread* contexts = block.threads.data();
            bool anyActive = true;
            while (anyActive) {
                anyActive = false;
                for (uint32_t t = 0; t < threadsPerBlock; ++t) {
                    if (contexts[t].__gsharp_state != -1 &&
                        !contexts[t].__gsharp_yielded_for_block_barrier &&
                        !contexts[t].__gsharp_yielded_for_grid_sync) {
                        __gsharp_set_thread_idx(
                            t, 0, 0,
                            static_cast<uint32_t>(b), 0, 0,
                            threadsPerBlock, 1, 1,
                            static_cast<uint32_t>(totalBlocks), 1, 1);
                        __gsharp_stack_resume(contexts[t]);
                        if (contexts[t].__gsharp_state != -1 &&
                            !contexts[t].__gsharp_yielded_for_block_barrier &&
                            !contexts[t].__gsharp_yielded_for_grid_sync) {
                            anyActive = true;
                        }
                    }
                }
                if (!anyActive) {
                    for (uint32_t t = 0; t < threadsPerBlock; ++t) {
                        if (contexts[t].__gsharp_yielded_for_block_barrier) {
                            contexts[t].__gsharp_yielded_for_block_barrier = false;
                            anyActive = true;
                        }
                    }
                }
            }
        }
    }
}
