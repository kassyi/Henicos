struct FpStateGuard {
    uint32_t origMxcsr;
    uint16_t origFcw;
    ~FpStateGuard() noexcept {
        __asm__ __volatile__("ldmxcsr %0\n\tfldcw %1" : : "m"(origMxcsr), "m"(origFcw));
    }
};

static void RunFpLaunch(
    int64_t totalBlocks,
    uint32_t threadsPerBlock,
    uint32_t* beforeMxcsr,
    uint32_t* afterMxcsr,
    uint16_t* beforeFcw,
    uint16_t* afterFcw,
    uint32_t* sumBits,
    uint32_t* prodBits,
    int* ompTids,
    int* observedOmpThreads) {
    *observedOmpThreads = 0;
    auto body = [&]() {
        uint32_t idx = blockIdx.x * blockDim.x + threadIdx.x;
        uint32_t mx = 0;
        uint16_t fc = 0;
        __asm__ __volatile__("stmxcsr %0\n\tfnstcw %1" : "=m"(mx), "=m"(fc));
        beforeMxcsr[idx] = mx;
        beforeFcw[idx] = fc;

        volatile float a = 1.0f;
        volatile float b = 6e-8f;
        float sum = a + b;
        volatile float c = 1e-40f;
        volatile float d = 2.0f;
        float prod = c * d;
        std::memcpy(&sumBits[idx], &sum, sizeof(uint32_t));
        std::memcpy(&prodBits[idx], &prod, sizeof(uint32_t));

        __gsharp_stack_barrier();

        __asm__ __volatile__("stmxcsr %0\n\tfnstcw %1" : "=m"(mx), "=m"(fc));
        afterMxcsr[idx] = mx;
        afterFcw[idx] = fc;
#ifdef _OPENMP
        ompTids[idx] = omp_get_thread_num();
#else
        ompTids[idx] = 0;
#endif
    };

    const auto fp = __gsharp_stack_capture_fp();
    #pragma omp parallel \
        num_threads(__gsharp_stack_thread_limit(static_cast<uint64_t>(threadsPerBlock), totalBlocks, 0))
    {
        #pragma omp single
        {
#ifdef _OPENMP
            *observedOmpThreads = omp_get_num_threads();
#else
            *observedOmpThreads = 1;
#endif
        }

        __gsharp_stack_pool stacks(fp);
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

extern "C" __attribute__((visibility("default")))
int ProbeMaxOmpThreadsForFp() {
#ifdef _OPENMP
    return omp_get_max_threads();
#else
    return 1;
#endif
}

extern "C" __attribute__((visibility("default")))
void RunConsecutiveFpLaunches(
    int64_t totalBlocks,
    uint32_t threadsPerBlock,
    uint32_t mxcsr,
    uint16_t fcw,
    uint32_t* beforeMxcsr,
    uint32_t* afterMxcsr,
    uint16_t* beforeFcw,
    uint16_t* afterFcw,
    uint32_t* sumBits,
    uint32_t* prodBits,
    int* ompTids,
    int* observedOmpThreads) {
    uint32_t origMxcsr = 0;
    uint16_t origFcw = 0;
    __asm__ __volatile__("stmxcsr %0\n\tfnstcw %1" : "=m"(origMxcsr), "=m"(origFcw));
    FpStateGuard guard{origMxcsr, origFcw};

    __asm__ __volatile__("ldmxcsr %0\n\tfldcw %1" : : "m"(mxcsr), "m"(fcw));
    RunFpLaunch(
        totalBlocks,
        threadsPerBlock,
        beforeMxcsr,
        afterMxcsr,
        beforeFcw,
        afterFcw,
        sumBits,
        prodBits,
        ompTids,
        observedOmpThreads);
}
