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
        using var library = NativeStackProbeLibrary.BuildOnLinuxX64(
            "Tasks/NativeStack/StackVmaBudget.cpp", "stack_vma", "Linux VMA budget throttling applies on Linux only.");

        var probeAvailableVmas = (delegate* unmanaged[Cdecl]<nuint>)library.GetExport("ProbeAvailableVmas");
        var probeThreadLimit = (delegate* unmanaged[Cdecl]<ulong, long, nuint, int>)library.GetExport("ProbeThreadLimit");
        var probeMaxOmpThreads = (delegate* unmanaged[Cdecl]<int>)library.GetExport("ProbeMaxOmpThreads");
        var runMultiBlockWithBudget =
            (delegate* unmanaged[Cdecl]<long, uint, nuint, int*, int*, void>)library.GetExport("RunMultiBlockWithBudget");

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

    // The caller's floating-point control at each launch: the default, round toward zero with flush to zero, the default
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
            (delegate* unmanaged[Cdecl]<long, uint, uint, ushort, uint*, uint*, ushort*, ushort*, uint*, uint*, int*, int*, void>)
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
}
