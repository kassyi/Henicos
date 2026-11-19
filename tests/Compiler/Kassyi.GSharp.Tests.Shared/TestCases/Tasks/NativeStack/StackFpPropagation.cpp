struct FpStateGuard {
    uint32_t origMxcsr;
    uint16_t origFcw;
    ~FpStateGuard() noexcept {
        __asm__ __volatile__("ldmxcsr %0\n\tfldcw %1" : : "m"(origMxcsr), "m"(origFcw));
    }
};

extern "C" __attribute__((visibility("default")))
void ProbeFpInheritance(
    uint32_t* expectedMxcsr,
    uint16_t* expectedFcw,
    uint32_t* beforeMxcsr,
    uint32_t* afterMxcsr,
    uint16_t* beforeFcw,
    uint16_t* afterFcw) {
    uint32_t origMxcsr = 0;
    uint16_t origFcw = 0;
    __asm__ __volatile__("stmxcsr %0\n\tfnstcw %1" : "=m"(origMxcsr), "=m"(origFcw));
    FpStateGuard guard{origMxcsr, origFcw};

    uint32_t targetMxcsr = (origMxcsr & ~0x603Fu) | 0x6000u;
    uint16_t targetFcw = static_cast<uint16_t>((origFcw & ~0x0C00u) | 0x0C00u);
    *expectedMxcsr = targetMxcsr;
    *expectedFcw = targetFcw;
    __asm__ __volatile__("ldmxcsr %0\n\tfldcw %1" : : "m"(targetMxcsr), "m"(targetFcw));

    constexpr uint32_t Threads = 4;
    auto body = [&]() {
        uint32_t tid = threadIdx.x;
        uint32_t mx = 0;
        uint16_t fc = 0;
        __asm__ __volatile__("stmxcsr %0\n\tfnstcw %1" : "=m"(mx), "=m"(fc));
        beforeMxcsr[tid] = mx;
        beforeFcw[tid] = fc;
        __gsharp_stack_barrier();
        __asm__ __volatile__("stmxcsr %0\n\tfnstcw %1" : "=m"(mx), "=m"(fc));
        afterMxcsr[tid] = mx;
        afterFcw[tid] = fc;
    };

    __gsharp_stack_pool stacks;
    __gsharp_stack_block block(stacks, Threads, body);
    __gsharp_stack_thread* contexts = block.threads.data();
    bool anyActive = true;
    while (anyActive) {
        anyActive = false;
        for (uint32_t t = 0; t < Threads; ++t) {
            if (contexts[t].__gsharp_state != -1 &&
                !contexts[t].__gsharp_yielded_for_block_barrier &&
                !contexts[t].__gsharp_yielded_for_grid_sync) {
                __gsharp_set_thread_idx(t, 0, 0, 0, 0, 0, Threads, 1, 1, 1, 1, 1);
                __gsharp_stack_resume(contexts[t]);
                if (contexts[t].__gsharp_state != -1 &&
                    !contexts[t].__gsharp_yielded_for_block_barrier &&
                    !contexts[t].__gsharp_yielded_for_grid_sync) {
                    anyActive = true;
                }
            }
        }
        if (!anyActive) {
            for (uint32_t t = 0; t < Threads; ++t) {
                if (contexts[t].__gsharp_yielded_for_block_barrier) {
                    contexts[t].__gsharp_yielded_for_block_barrier = false;
                    anyActive = true;
                }
            }
        }
    }
}
