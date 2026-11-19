#nullable enable
using System.Runtime.InteropServices;
using Kassyi.GSharp.Compiler.Tasks;
using Kassyi.GSharp.Compiler.Tasks.Helpers;
using Kassyi.GSharp.Compiler.Tasks.Toolchains;
using Kassyi.GSharp.Tests.Shared.Tasks;
using Microsoft.Build.Utilities;

namespace Kassyi.GSharp.Tests.Cpu.Core;

// Process-wide: the probes go through NativeLibrary.Load, share the OpenMP runtime, and one counts /proc/self/maps
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Compiler)]
public sealed class NativeStackToolchainTests(ITestOutputHelper output)
{
    static NativeStackToolchainTests()
    {
        MSBuildLocatorSetup.EnsureRegistered();
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public unsafe void StackThreadLimit_SmallVmaBudgetOverride_ReducesParallelismAndPreservesResults()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux VMA budget throttling applies on Linux only.");
        Skip.IfNot(RuntimeInformation.ProcessArchitecture == Architecture.X64, "Checked on x86-64 Linux.");

        var engine = new MockBuildEngine();
        var log = new TaskLoggingHelper(engine, nameof(NativeStackToolchainTests));
        var toolchain = new GccClangToolchain(ProcessRunner.Default);
        Skip.If(
            toolchain.PrepareEnvironment(["x64"], log) != ToolchainPreparationResult.Ready,
            "GCC/Clang toolchain is not available.");

        const string ProbeSource = """
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

                #pragma omp parallel num_threads(__gsharp_stack_thread_limit(static_cast<uint64_t>(threadsPerBlock), totalBlocks, vmaBudgetOverride))
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
            """;

        var directory = CreateTempDirectory("gsharp-stack-vma-");
        try
        {
            var sourcePath = Path.Combine(directory, "stack_vma.cpp");
            File.WriteAllText(sourcePath, GSharpPreludeResources.Cpp + GSharpPreludeResources.CppStackThreads + "\n" + ProbeSource);
            var libraryPath = Path.Combine(directory, "libstack_vma.so");

            Assert.True(
                toolchain.TryCompileCppToSharedLibrary(sourcePath, libraryPath, "x64", log),
                string.Join(Environment.NewLine, engine.LoggedErrors));

            var handle = NativeLibrary.Load(libraryPath);
            try
            {
                var probeAvailableVmas = (delegate* unmanaged[Cdecl]<nuint>)NativeLibrary.GetExport(handle, "ProbeAvailableVmas");
                var probeThreadLimit = (delegate* unmanaged[Cdecl]<ulong, long, nuint, int>)NativeLibrary.GetExport(handle, "ProbeThreadLimit");
                var probeMaxOmpThreads = (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(handle, "ProbeMaxOmpThreads");
                var runMultiBlockWithBudget = (delegate* unmanaged[Cdecl]<long, uint, nuint, int*, int*, void>)NativeLibrary.GetExport(handle, "RunMultiBlockWithBudget");

                nuint availableVmas = probeAvailableVmas();
                Assert.True(availableVmas > 0);
                Assert.Equal(1, probeThreadLimit(2_000_000UL, 16L, 0));

                const int TotalBlocks = 16;
                const int ThreadsPerBlock = 4;
                var outputDefault = new int[TotalBlocks * ThreadsPerBlock];
                var outputLimited = new int[TotalBlocks * ThreadsPerBlock];
                int observedThreadsDefault = 0;
                int observedThreadsLimited = 0;

                fixed (int* pDefault = outputDefault)
                fixed (int* pLimited = outputLimited)
                {
                    runMultiBlockWithBudget(TotalBlocks, ThreadsPerBlock, 0, pDefault, &observedThreadsDefault);
                    runMultiBlockWithBudget(TotalBlocks, ThreadsPerBlock, 8, pLimited, &observedThreadsLimited);
                }

                int maxOmpThreads = probeMaxOmpThreads();
                output.WriteLine(
                    $"availableVmas={availableVmas}, maxOmpThreads={maxOmpThreads}, observedThreadsDefault={observedThreadsDefault}, observedThreadsLimited={observedThreadsLimited}");

                Assert.Equal(outputDefault, outputLimited);
                Assert.Equal(1, observedThreadsLimited);
                if (maxOmpThreads >= 2)
                {
                    Assert.True(observedThreadsLimited < observedThreadsDefault);
                }

                for (int b = 0; b < TotalBlocks; b++)
                {
                    for (int t = 0; t < ThreadsPerBlock; t++)
                    {
                        int expected = (b + 1) * 1000 + ((t + 1) % ThreadsPerBlock) + 1;
                        Assert.Equal(expected, outputLimited[b * ThreadsPerBlock + t]);
                    }
                }
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public unsafe void StackBlock_LauncherNonDefaultFloatingPointControl_PropagatesToGpuThreadsAcrossBarrier()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "x86-64 initial frame FP propagation test runs on Linux only.");
        Skip.IfNot(RuntimeInformation.ProcessArchitecture == Architecture.X64, "Checked on x86-64 Linux.");

        var engine = new MockBuildEngine();
        var log = new TaskLoggingHelper(engine, nameof(NativeStackToolchainTests));
        var toolchain = new GccClangToolchain(ProcessRunner.Default);
        Skip.If(
            toolchain.PrepareEnvironment(["x64"], log) != ToolchainPreparationResult.Ready,
            "GCC/Clang toolchain is not available.");

        const string ProbeSource = """
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
            """;

        var directory = CreateTempDirectory("gsharp-stack-fp-");
        try
        {
            var sourcePath = Path.Combine(directory, "stack_fp.cpp");
            File.WriteAllText(sourcePath, GSharpPreludeResources.Cpp + GSharpPreludeResources.CppStackThreads + "\n" + ProbeSource);
            var libraryPath = Path.Combine(directory, "libstack_fp.so");

            Assert.True(
                toolchain.TryCompileCppToSharedLibrary(sourcePath, libraryPath, "x64", log),
                string.Join(Environment.NewLine, engine.LoggedErrors));

            var handle = NativeLibrary.Load(libraryPath);
            try
            {
                var probeFpInheritance = (delegate* unmanaged[Cdecl]<uint*, ushort*, uint*, uint*, ushort*, ushort*, void>)
                    NativeLibrary.GetExport(handle, "ProbeFpInheritance");

                uint expectedMxcsr = 0;
                ushort expectedFcw = 0;
                var beforeMxcsr = new uint[4];
                var afterMxcsr = new uint[4];
                var beforeFcw = new ushort[4];
                var afterFcw = new ushort[4];

                fixed (uint* pBeforeMxcsr = beforeMxcsr)
                fixed (uint* pAfterMxcsr = afterMxcsr)
                fixed (ushort* pBeforeFcw = beforeFcw)
                fixed (ushort* pAfterFcw = afterFcw)
                {
                    probeFpInheritance(&expectedMxcsr, &expectedFcw, pBeforeMxcsr, pAfterMxcsr, pBeforeFcw, pAfterFcw);
                }

                output.WriteLine($"expectedMxcsr=0x{expectedMxcsr:X}, expectedFcw=0x{expectedFcw:X}");
                Assert.NotEqual(0x1F80u, expectedMxcsr);
                Assert.NotEqual((ushort)0x037F, expectedFcw);

                for (int t = 0; t < 4; t++)
                {
                    Assert.Equal(expectedMxcsr, beforeMxcsr[t]);
                    Assert.Equal(expectedMxcsr, afterMxcsr[t]);
                    Assert.Equal(expectedFcw, beforeFcw[t]);
                    Assert.Equal(expectedFcw, afterFcw[t]);
                }
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public unsafe void StackBlock_ConsecutiveMultiThreadLaunchesWithChangedFp_InheritsCallerFpOnAllWorkers()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "x86-64 multi-launch OpenMP FP inheritance test runs on Linux only.");
        Skip.IfNot(RuntimeInformation.ProcessArchitecture == Architecture.X64, "Checked on x86-64 Linux.");

        var engine = new MockBuildEngine();
        var log = new TaskLoggingHelper(engine, nameof(NativeStackToolchainTests));
        var toolchain = new GccClangToolchain(ProcessRunner.Default);
        Skip.If(
            toolchain.PrepareEnvironment(["x64"], log) != ToolchainPreparationResult.Ready,
            "GCC/Clang toolchain is not available.");

        const string ProbeSource = """
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
                #pragma omp parallel num_threads(__gsharp_stack_thread_limit(static_cast<uint64_t>(threadsPerBlock), totalBlocks, 0))
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
            """;

        var directory = CreateTempDirectory("gsharp-stack-fp-multi-");
        try
        {
            var sourcePath = Path.Combine(directory, "stack_fp_multi.cpp");
            File.WriteAllText(sourcePath, GSharpPreludeResources.Cpp + GSharpPreludeResources.CppStackThreads + "\n" + ProbeSource);
            var libraryPath = Path.Combine(directory, "libstack_fp_multi.so");

            Assert.True(
                toolchain.TryCompileCppToSharedLibrary(sourcePath, libraryPath, "x64", log),
                string.Join(Environment.NewLine, engine.LoggedErrors));

            var handle = NativeLibrary.Load(libraryPath);
            try
            {
                var probeMaxOmpThreads = (delegate* unmanaged[Cdecl]<int>)
                    NativeLibrary.GetExport(handle, "ProbeMaxOmpThreadsForFp");
                var runConsecutiveFpLaunches = (delegate* unmanaged[Cdecl]<long, uint, uint, ushort, uint*, uint*, ushort*, ushort*, uint*, uint*, int*, int*, void>)
                    NativeLibrary.GetExport(handle, "RunConsecutiveFpLaunches");

                const int TotalBlocks = 64;
                const int ThreadsPerBlock = 4;
                const int TotalGpuThreads = TotalBlocks * ThreadsPerBlock;

                const uint DefaultMxcsr = 0x1F80u;
                const ushort DefaultFcw = 0x037F;
                const uint DefaultSumBits = 0x3F800001u;
                const uint DefaultProdBits = 0x00022D84u;

                const uint RtzMxcsr = 0xFFC0u;
                const ushort RtzFcw = 0x0F7F;
                const uint RtzSumBits = 0x3F800000u;
                const uint RtzProdBits = 0x00000000u;

                var beforeMxcsr = new uint[TotalGpuThreads];
                var afterMxcsr = new uint[TotalGpuThreads];
                var beforeFcw = new ushort[TotalGpuThreads];
                var afterFcw = new ushort[TotalGpuThreads];
                var sumBits = new uint[TotalGpuThreads];
                var prodBits = new uint[TotalGpuThreads];
                var ompTids = new int[TotalGpuThreads];

                int maxOmpThreads = probeMaxOmpThreads();
                int observed1 = 0;
                int observed2 = 0;
                int observed3 = 0;

                fixed (uint* pBeforeMxcsr = beforeMxcsr)
                fixed (uint* pAfterMxcsr = afterMxcsr)
                fixed (ushort* pBeforeFcw = beforeFcw)
                fixed (ushort* pAfterFcw = afterFcw)
                fixed (uint* pSumBits = sumBits)
                fixed (uint* pProdBits = prodBits)
                fixed (int* pOmpTids = ompTids)
                {
                    // Launch 1: Default FP state
                    runConsecutiveFpLaunches(
                        TotalBlocks,
                        ThreadsPerBlock,
                        DefaultMxcsr,
                        DefaultFcw,
                        pBeforeMxcsr,
                        pAfterMxcsr,
                        pBeforeFcw,
                        pAfterFcw,
                        pSumBits,
                        pProdBits,
                        pOmpTids,
                        &observed1);
                    AssertLaunchFpState(
                        "Launch1_Default",
                        DefaultMxcsr,
                        DefaultFcw,
                        DefaultSumBits,
                        DefaultProdBits,
                        beforeMxcsr,
                        afterMxcsr,
                        beforeFcw,
                        afterFcw,
                        sumBits,
                        prodBits,
                        ompTids,
                        observed1,
                        maxOmpThreads);

                    // Launch 2: Caller changed to RTZ/FTZ/DAZ
                    runConsecutiveFpLaunches(
                        TotalBlocks,
                        ThreadsPerBlock,
                        RtzMxcsr,
                        RtzFcw,
                        pBeforeMxcsr,
                        pAfterMxcsr,
                        pBeforeFcw,
                        pAfterFcw,
                        pSumBits,
                        pProdBits,
                        pOmpTids,
                        &observed2);
                    AssertLaunchFpState(
                        "Launch2_RTZ",
                        RtzMxcsr,
                        RtzFcw,
                        RtzSumBits,
                        RtzProdBits,
                        beforeMxcsr,
                        afterMxcsr,
                        beforeFcw,
                        afterFcw,
                        sumBits,
                        prodBits,
                        ompTids,
                        observed2,
                        maxOmpThreads);

                    // Launch 3: Caller returned to Default
                    runConsecutiveFpLaunches(
                        TotalBlocks,
                        ThreadsPerBlock,
                        DefaultMxcsr,
                        DefaultFcw,
                        pBeforeMxcsr,
                        pAfterMxcsr,
                        pBeforeFcw,
                        pAfterFcw,
                        pSumBits,
                        pProdBits,
                        pOmpTids,
                        &observed3);
                    AssertLaunchFpState(
                        "Launch3_Default",
                        DefaultMxcsr,
                        DefaultFcw,
                        DefaultSumBits,
                        DefaultProdBits,
                        beforeMxcsr,
                        afterMxcsr,
                        beforeFcw,
                        afterFcw,
                        sumBits,
                        prodBits,
                        ompTids,
                        observed3,
                        maxOmpThreads);
                }
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private void AssertLaunchFpState(
        string label,
        uint expectedMxcsr,
        ushort expectedFcw,
        uint expectedSumBits,
        uint expectedProdBits,
        uint[] beforeMxcsr,
        uint[] afterMxcsr,
        ushort[] beforeFcw,
        ushort[] afterFcw,
        uint[] sumBits,
        uint[] prodBits,
        int[] ompTids,
        int observedOmpThreads,
        int maxOmpThreads)
    {
        const uint StickyFlagsMask = 0x3Fu;
        int nonZeroWorkerThreads = 0;
        for (int i = 0; i < beforeMxcsr.Length; i++)
        {
            if (ompTids[i] != 0)
            {
                nonZeroWorkerThreads++;
            }

            Assert.Equal(expectedMxcsr, beforeMxcsr[i]);
            Assert.Equal(expectedMxcsr, afterMxcsr[i] & ~StickyFlagsMask);
            Assert.Equal(expectedFcw, beforeFcw[i]);
            Assert.Equal(expectedFcw, afterFcw[i]);
            Assert.Equal(expectedSumBits, sumBits[i]);
            Assert.Equal(expectedProdBits, prodBits[i]);
        }

        output.WriteLine(
            $"{label}: maxOmpThreads={maxOmpThreads}, observedOmpThreads={observedOmpThreads}, nonZeroWorkerGpuThreads={nonZeroWorkerThreads}/{beforeMxcsr.Length}");
        if (maxOmpThreads >= 2)
        {
            Assert.True(observedOmpThreads >= 2, $"{label}: expected multi-threaded OpenMP launch, got {observedOmpThreads}.");
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void StackThreads_WindowsAddressSanitizer_FailsWithPreprocessorError()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "MSVC ASan check runs on Windows only.");
        Skip.IfNot(RuntimeInformation.ProcessArchitecture == Architecture.X64, "Checked on x64.");

        var engine = new MockBuildEngine();
        var log = new TaskLoggingHelper(engine, nameof(NativeStackToolchainTests));
        var toolchain = new MsvcToolchain(ProcessRunner.Default);
        Skip.If(
            toolchain.PrepareEnvironment(["x64"], log) != ToolchainPreparationResult.Ready,
            "MSVC (vcvarsall.bat) is not available.");

        AssertAddressSanitizerRejected(toolchain, "x64", engine, log, "stack_asan_win.dll");
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void StackThreads_LinuxAddressSanitizer_FailsWithPreprocessorError()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "GCC/Clang ASan check runs on Linux only.");
        Skip.IfNot(RuntimeInformation.ProcessArchitecture == Architecture.X64, "Checked on x64.");

        var engine = new MockBuildEngine();
        var log = new TaskLoggingHelper(engine, nameof(NativeStackToolchainTests));
        var toolchain = new GccClangToolchain(ProcessRunner.Default);
        Skip.If(
            toolchain.PrepareEnvironment(["x64"], log) != ToolchainPreparationResult.Ready,
            "GCC/Clang toolchain is not available.");

        AssertAddressSanitizerRejected(toolchain, "x64", engine, log, "libstack_asan_linux.so");
    }

    private static void AssertAddressSanitizerRejected(
        INativeToolchain toolchain,
        string architecture,
        MockBuildEngine engine,
        TaskLoggingHelper log,
        string outputFileName)
    {
        var directory = CreateTempDirectory("gsharp-stack-asan-");
        try
        {
            var sourcePath = Path.Combine(directory, "stack_asan.cpp");
            File.WriteAllText(sourcePath, GSharpPreludeResources.Cpp + GSharpPreludeResources.CppStackThreads);
            var libraryPath = Path.Combine(directory, outputFileName);

            var compiled = toolchain.TryCompileCppToSharedLibrary(
                sourcePath,
                libraryPath,
                architecture,
                log,
                sanitizers: NativeSanitizers.Address);

            Assert.False(compiled);
            Assert.Contains(
                engine.LoggedErrors,
                static message => message.Contains(
                    "GSharpNativeBarrierModel=Stack does not support AddressSanitizer yet",
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }
}
