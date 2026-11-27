#nullable enable
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
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
        using var library = NativeStackProbeLibrary.BuildOnLinuxX64(
            "Tasks/NativeStack/StackVmaBudget.cpp", "stack_vma", "Linux VMA budget throttling applies on Linux only.");

        var probeAvailableVmas = (delegate* unmanaged[Cdecl]<nuint>)library.GetExport("ProbeAvailableVmas");
        var probeThreadLimit =
            (delegate* unmanaged[Cdecl]<ulong, long, nuint, int>)library.GetExport("ProbeThreadLimit");
        var probeMaxOmpThreads = (delegate* unmanaged[Cdecl]<int>)library.GetExport("ProbeMaxOmpThreads");
        var runMultiBlockWithBudget =
            (delegate* unmanaged[Cdecl]<long, uint, nuint, int*, int*, void>)
            library.GetExport("RunMultiBlockWithBudget");

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
            $"availableVmas={availableVmas}, maxOmpThreads={maxOmpThreads}, " +
            $"observedThreadsDefault={observedThreadsDefault}, observedThreadsLimited={observedThreadsLimited}");

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

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public unsafe void StackBlock_LauncherNonDefaultFloatingPointControl_PropagatesToGpuThreadsAcrossBarrier()
    {
        using var library = NativeStackProbeLibrary.BuildOnLinuxX64(
            "Tasks/NativeStack/StackFpPropagation.cpp", "stack_fp",
            "x86-64 initial frame FP propagation test runs on Linux only.");

        var probeFpInheritance = (delegate* unmanaged[Cdecl]<uint*, ushort*, uint*, uint*, ushort*, ushort*, void>)
            library.GetExport("ProbeFpInheritance");

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

    // The caller's floating-point control at each launch: the default, round toward zero with flush to zero,
    // the default
    private static readonly (string Label, uint Mxcsr, ushort Fcw, uint SumBits, uint ProdBits)[] s_fpLaunches =
    [
        ("Launch1_Default", 0x1F80u, 0x037F, 0x3F800001u, 0x00022D84u),
        ("Launch2_RTZ", 0xFFC0u, 0x0F7F, 0x3F800000u, 0x00000000u),
        ("Launch3_Default", 0x1F80u, 0x037F, 0x3F800001u, 0x00022D84u)
    ];

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public unsafe void StackBlock_ConsecutiveMultiThreadLaunchesWithChangedFp_InheritsCallerFpOnAllWorkers()
    {
        using var library = NativeStackProbeLibrary.BuildOnLinuxX64(
            "Tasks/NativeStack/StackFpMultiLaunch.cpp", "stack_fp_multi",
            "x86-64 multi-launch OpenMP FP inheritance test runs on Linux only.");

        var probeMaxOmpThreads = (delegate* unmanaged[Cdecl]<int>)library.GetExport("ProbeMaxOmpThreadsForFp");
        var runConsecutiveFpLaunches =
            (delegate* unmanaged[Cdecl]<
                long, uint, uint, ushort, uint*, uint*, ushort*, ushort*, uint*, uint*, int*, int*, void>)
            library.GetExport("RunConsecutiveFpLaunches");

        const int TotalBlocks = 64;
        const int ThreadsPerBlock = 4;
        const int TotalGpuThreads = TotalBlocks * ThreadsPerBlock;

        var beforeMxcsr = new uint[TotalGpuThreads];
        var afterMxcsr = new uint[TotalGpuThreads];
        var beforeFcw = new ushort[TotalGpuThreads];
        var afterFcw = new ushort[TotalGpuThreads];
        var sumBits = new uint[TotalGpuThreads];
        var prodBits = new uint[TotalGpuThreads];
        var ompTids = new int[TotalGpuThreads];
        int maxOmpThreads = probeMaxOmpThreads();

        fixed (uint* pBeforeMxcsr = beforeMxcsr)
        fixed (uint* pAfterMxcsr = afterMxcsr)
        fixed (ushort* pBeforeFcw = beforeFcw)
        fixed (ushort* pAfterFcw = afterFcw)
        fixed (uint* pSumBits = sumBits)
        fixed (uint* pProdBits = prodBits)
        fixed (int* pOmpTids = ompTids)
        {
            foreach (var (label, mxcsr, fcw, expectedSumBits, expectedProdBits) in s_fpLaunches)
            {
                int observed = 0;
                runConsecutiveFpLaunches(TotalBlocks, ThreadsPerBlock, mxcsr, fcw, pBeforeMxcsr, pAfterMxcsr,
                    pBeforeFcw, pAfterFcw, pSumBits, pProdBits, pOmpTids, &observed);
                AssertLaunchFpState(label, mxcsr, fcw, expectedSumBits, expectedProdBits, beforeMxcsr, afterMxcsr,
                    beforeFcw, afterFcw, sumBits, prodBits, ompTids, observed, maxOmpThreads);
            }
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
            $"{label}: maxOmpThreads={maxOmpThreads}, observedOmpThreads={observedOmpThreads}, " +
            $"nonZeroWorkerGpuThreads={nonZeroWorkerThreads}/{beforeMxcsr.Length}");
        if (maxOmpThreads >= 2)
        {
            Assert.True(
                observedOmpThreads >= 2,
                $"{label}: expected multi-threaded OpenMP launch, got {observedOmpThreads}.");
        }
    }

    [SkippableFact]
    [Platform(TestPlatform.Windows)]
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
        var directory = NativeStackProbeLibrary.CreateTempDirectory("gsharp-stack-asan-");
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

    // A thread that has a signal stack (each thread CoreCLR runs) keeps it, so that CoreCLR frees its own at the end of
    // the thread; a thread without one (an OpenMP worker) gets the 64 KiB one of the prelude
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public unsafe void StackPool_SignalStackOfTheThread_KeptWhenPresentAndAddedWhenMissing()
    {
        int[] outputs = [0, 0, 0];

        using (var library = NativeStackProbeLibrary.BuildForHostX64(
                   "Tasks/NativeStack/StackOverflowReuse.cpp", "stack_signal_stack", runsOnNativeStacks: true))
        {
            var probeSignalStack = (delegate* unmanaged[Cdecl]<int*, void>)library.GetExport("ProbeSignalStack");
            fixed (int* pOutputs = outputs)
            {
                probeSignalStack(pOutputs);
            }
        }

        // mutant: acquire installs its own signal stack on every thread: outputs[1] becomes 0
        int[] expected = OperatingSystem.IsLinux() ? [1, 1, 1] : [-1, -1, -1];
        Assert.Equal(expected, outputs);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public unsafe void StackPool_BlockAfterOverflow_ReusesStackOnLinuxAndReplacesFiberOnWindows()
    {
        int[] block1Outputs = [-1, -1];
        int[] block2Outputs = [-1, -1];
        int block1ErrorCode = 0;
        ulong block1CreatedDelta = 0;
        ulong block2CreatedDelta = 0;

        using (var library = NativeStackProbeLibrary.BuildForHostX64(
                   "Tasks/NativeStack/StackOverflowReuse.cpp", "stack_overflow_reuse", runsOnNativeStacks: true))
        {
            var probeOverflowReuse =
                (delegate* unmanaged[Cdecl]<int*, int*, int*, ulong*, ulong*, void>)
                library.GetExport("ProbeOverflowReuse");

            fixed (int* pBlock1 = block1Outputs)
            fixed (int* pBlock2 = block2Outputs)
            {
                probeOverflowReuse(pBlock1, pBlock2, &block1ErrorCode, &block1CreatedDelta, &block2CreatedDelta);
            }
        }

        Assert.Equal((int)Kassyi.GSharp.Runtime.Aot.GSharpErrorCode.InsufficientExecutionStack, block1ErrorCode);
        Assert.Equal([-1, 101], block1Outputs);
        Assert.Equal([200, 201], block2Outputs);
        Assert.Equal(2UL, block1CreatedDelta);
        if (OperatingSystem.IsLinux())
        {
            Assert.Equal(0UL, block2CreatedDelta);
        }
        else
        {
            Assert.Equal(1UL, block2CreatedDelta);
        }

        // NativeLibrary.Free has already run on the probe .so; a managed SIGSEGV on a fresh thread must still reach CoreCLR.
        Exception? otherThreadFailure = null;
        var otherThread = new Thread(() =>
        {
            try
            {
                Assert.Throws<NullReferenceException>(() => DereferenceNullValues(default(NullArrayHolder).Values));
            }
            catch (Exception ex)
            {
                otherThreadFailure = ex;
            }
        });
        otherThread.Start();
        otherThread.Join();
        Assert.Null(otherThreadFailure);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public unsafe void StackPool_MultipleLibrariesWithoutGnuUnique_HandlesOverflowAndForwardsAcrossLibraries()
    {
        using (var firstNoUnique = NativeStackProbeLibrary.BuildOnLinuxX64WithoutGnuUnique(
                   "Tasks/NativeStack/StackOverflowReuse.cpp", "stack_overflow_nq1",
                   "Linux multi-DSO without STB_GNU_UNIQUE check runs on Linux only."))
        using (var secondNoUnique = NativeStackProbeLibrary.BuildOnLinuxX64WithoutGnuUnique(
                   "Tasks/NativeStack/StackOverflowReuse.cpp", "stack_overflow_nq2",
                   "Linux multi-DSO without STB_GNU_UNIQUE check runs on Linux only."))
        using (var defaultUnique = NativeStackProbeLibrary.BuildOnLinuxX64(
                   "Tasks/NativeStack/StackOverflowReuse.cpp", "stack_overflow_def",
                   "Linux multi-DSO without STB_GNU_UNIQUE check runs on Linux only."))
        {
            AssertProbeOverflowReuse(firstNoUnique);
            AssertProbeOverflowReuse(secondNoUnique);
            AssertProbeOverflowReuse(firstNoUnique);
            AssertProbeOverflowReuse(secondNoUnique);
            AssertProbeOverflowReuse(defaultUnique);
            AssertProbeOverflowReuse(firstNoUnique);
            AssertProbeOverflowReuse(defaultUnique);

            Assert.Throws<NullReferenceException>(() => DereferenceNullValues(default(NullArrayHolder).Values));

            Exception? workerFailure = null;
            var worker = new Thread(() =>
            {
                try
                {
                    AssertProbeOverflowReuse(secondNoUnique);
                    AssertProbeOverflowReuse(firstNoUnique);
                    AssertProbeOverflowReuse(secondNoUnique);
                    Assert.Throws<NullReferenceException>(() => DereferenceNullValues(default(NullArrayHolder).Values));
                }
                catch (Exception ex)
                {
                    workerFailure = ex;
                }
            });
            worker.Start();
            worker.Join();
            Assert.Null(workerFailure);
        }

        Assert.Throws<NullReferenceException>(() => DereferenceNullValues(default(NullArrayHolder).Values));

        Exception? postUnloadFailure = null;
        var postUnloadThread = new Thread(() =>
        {
            try
            {
                Assert.Throws<NullReferenceException>(() => DereferenceNullValues(default(NullArrayHolder).Values));
            }
            catch (Exception ex)
            {
                postUnloadFailure = ex;
            }
        });
        postUnloadThread.Start();
        postUnloadThread.Join();
        Assert.Null(postUnloadFailure);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackPool_PostThreadLocalDestructorOverflow_ReportsInsufficientExecutionStack()
    {
        using var library = NativeStackProbeLibrary.BuildOnLinuxX64WithoutGnuUnique(
            "Tasks/NativeStack/StackOverflowReuse.cpp",
            "stack_overflow_post_dtor",
            "Linux post-TLS-destructor stack overflow check runs on Linux only.");

        var engine = new MockBuildEngine();
        var log = new TaskLoggingHelper(engine, nameof(NativeStackToolchainTests));
        var toolchain = new GccClangToolchain(ProcessRunner.Default);
        Skip.If(
            toolchain.PrepareEnvironment(["x64"], log) != ToolchainPreparationResult.Ready,
            "GCC/Clang toolchain is not available.");

        var directory = NativeStackProbeLibrary.CreateTempDirectory("gsharp-stack-post-dtor-host-");
        try
        {
            var hostSourcePath = Path.Combine(directory, "host.cpp");
            var hostBinaryPath = Path.Combine(directory, "host");
            File.WriteAllText(
                hostSourcePath,
                """
                #include <cstdio>
                #include <dlfcn.h>
                #include <thread>

                using ProbeOverflowReuseFn = void (*)(int*, int*, int*, unsigned long*, unsigned long*);
                static ProbeOverflowReuseFn g_probe = nullptr;
                static int g_dtor_ok = 0;

                struct HostEarlyTls {
                    bool touched = false;
                    void touch() { touched = true; }
                    ~HostEarlyTls() {
                        if (!touched || g_probe == nullptr) {
                            return;
                        }
                        int b1[2] = {-1, -1};
                        int b2[2] = {-1, -1};
                        int err = 0;
                        unsigned long createdDelta1 = 0;
                        unsigned long createdDelta2 = 0;
                        g_probe(b1, b2, &err, &createdDelta1, &createdDelta2);
                        if (err == 13 && b1[0] == -1 && b1[1] == 101 &&
                            b2[0] == 200 && b2[1] == 201 && createdDelta1 == 2 && createdDelta2 == 0) {
                            g_dtor_ok = 1;
                            std::printf("PASS:err=%d\n", err);
                        } else {
                            std::printf("FAIL:err=%d\n", err);
                        }
                    }
                };

                static thread_local HostEarlyTls g_early;

                int main(int argc, char** argv) {
                    if (argc < 2) {
                        return 2;
                    }
                    void* handle = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
                    if (handle == nullptr) {
                        return 3;
                    }
                    g_probe = reinterpret_cast<ProbeOverflowReuseFn>(dlsym(handle, "ProbeOverflowReuse"));
                    if (g_probe == nullptr) {
                        return 4;
                    }
                    int firstOk = 0;
                    std::thread worker([&]() {
                        g_early.touch();
                        int b1[2] = {-1, -1};
                        int b2[2] = {-1, -1};
                        int err = 0;
                        unsigned long createdDelta1 = 0;
                        unsigned long createdDelta2 = 0;
                        g_probe(b1, b2, &err, &createdDelta1, &createdDelta2);
                        firstOk = (err == 13 && b1[0] == -1 && b1[1] == 101 &&
                                   b2[0] == 200 && b2[1] == 201 && createdDelta1 == 2 && createdDelta2 == 0) ? 1 : 0;
                    });
                    worker.join();
                    return (firstOk == 1 && g_dtor_ok == 1) ? 0 : 1;
                }
                """);

            var identity = toolchain.GetCompilerIdentity("x64");
            var compiler = string.IsNullOrWhiteSpace(identity.Path) ? "g++" : identity.Path;
            var compileResult = ProcessRunner.Default.Run(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = compiler,
                    Arguments = $"-O2 -std=c++17 -pthread \"{hostSourcePath}\" -ldl -o \"{hostBinaryPath}\""
                });
            Assert.True(
                compileResult.ExitCode == 0,
                $"Failed to compile C++ host probe: {compileResult.StandardError}");

            var runResult = ProcessRunner.Default.Run(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = hostBinaryPath,
                    Arguments = $"\"{library.LibraryPath}\""
                });
            Assert.True(
                runResult.ExitCode == 0,
                $"Host probe failed (exit {runResult.ExitCode}): {runResult.StandardOutput} {runResult.StandardError}");
            Assert.Contains("PASS:err=13", runResult.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static unsafe void AssertProbeOverflowReuse(NativeStackProbeLibrary library)
    {
        int[] block1Outputs = [-1, -1];
        int[] block2Outputs = [-1, -1];
        int block1ErrorCode = 0;
        ulong block1CreatedDelta = 0;
        ulong block2CreatedDelta = 0;

        var probeOverflowReuse =
            (delegate* unmanaged[Cdecl]<int*, int*, int*, ulong*, ulong*, void>)
            library.GetExport("ProbeOverflowReuse");

        fixed (int* pBlock1 = block1Outputs)
        fixed (int* pBlock2 = block2Outputs)
        {
            probeOverflowReuse(pBlock1, pBlock2, &block1ErrorCode, &block1CreatedDelta, &block2CreatedDelta);
        }

        Assert.Equal((int)Kassyi.GSharp.Runtime.Aot.GSharpErrorCode.InsufficientExecutionStack, block1ErrorCode);
        Assert.Equal([-1, 101], block1Outputs);
        Assert.Equal([200, 201], block2Outputs);
        Assert.Equal(2UL, block1CreatedDelta);
        Assert.Equal(0UL, block2CreatedDelta);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int DereferenceNullValues(int[] values) => values.Length;

    private struct NullArrayHolder
    {
        public int[] Values;
    }
}
