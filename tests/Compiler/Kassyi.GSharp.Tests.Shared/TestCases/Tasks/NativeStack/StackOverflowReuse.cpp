__noinline__ static uint8_t ProbeOverflowStackFrame(volatile uint8_t seed) {
    volatile uint8_t buffer[2u * 1024u * 1024u];
    buffer[0] = seed;
    buffer[sizeof(buffer) - 1u] = static_cast<uint8_t>(seed + 1u);
    return static_cast<uint8_t>(buffer[0] ^ buffer[sizeof(buffer) - 1u]);
}

static void RunProbeBlock(__gsharp_stack_block& block, uint32_t threadsPerBlock) {
    __gsharp_stack_thread* contexts = block.threads.data();
    bool anyActive = true;
    while (anyActive) {
        anyActive = false;
        for (uint32_t t = 0; t < threadsPerBlock; ++t) {
            if (contexts[t].__gsharp_state != -1 &&
                !contexts[t].__gsharp_yielded_for_block_barrier &&
                !contexts[t].__gsharp_yielded_for_grid_sync) {
                __gsharp_set_thread_idx(t, 0, 0, 0, 0, 0, threadsPerBlock, 1, 1, 1, 1, 1);
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

extern "C" __declspec(dllexport) void ProbeOverflowReuse(
    int* block1Outputs,
    int* block2Outputs,
    int32_t* block1ErrorCode,
    uint64_t* block1CreatedDelta,
    uint64_t* block2CreatedDelta) {
    constexpr uint32_t Threads = 2;
    // A prior generated kernel launch on this OS thread leaves __gsharp_active_error_slot (TLS UNIQUE across DSOs on
    // Linux) pointing at its stack-local GSharpErrorSlot; clear it so __gsharp_record_failure writes to
    // __gsharp_host_last_error.
    __gsharp_active_error_slot = nullptr;
    __gsharp_consume_last_error(nullptr);
    __gsharp_stack_pool stacks;

    uint64_t before1 = __gsharp_stack_get_created_count();
    {
        auto body1 = [&]() {
            uint32_t tid = threadIdx.x;
            if (tid == 0u) {
                block1Outputs[0] = static_cast<int>(ProbeOverflowStackFrame(static_cast<volatile uint8_t>(42u)));
            } else {
                block1Outputs[1] = 101;
            }
        };
        __gsharp_stack_block block1(stacks, Threads, body1, "Probe.Overflow");
        RunProbeBlock(block1, Threads);
    }
    *block1CreatedDelta = __gsharp_stack_get_created_count() - before1;
    *block1ErrorCode = __gsharp_consume_last_error(nullptr);

    uint64_t before2 = __gsharp_stack_get_created_count();
    {
        auto body2 = [&]() {
            uint32_t tid = threadIdx.x;
            __gsharp_stack_barrier();
            block2Outputs[tid] = static_cast<int>(tid + 200u);
        };
        __gsharp_stack_block block2(stacks, Threads, body2, "Probe.Reuse");
        RunProbeBlock(block2, Threads);
    }
    *block2CreatedDelta = __gsharp_stack_get_created_count() - before2;
}
