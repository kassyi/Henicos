#nullable enable
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Tests for CUDA Graph capture and replay in <see cref="ModPowZeroSyncController"/>.
/// </summary>
[Collection(TestCollections.DeviceExclusive)]
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class ModPowZeroSyncGraphTests
{
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ModPow_LegacyCopyOnAnotherThreadDuringLadderCapture_BothSucceed()
    {
        CudaCapability.EnsureGpuAvailable();
        const string Name = nameof(ModPow_LegacyCopyOnAnotherThreadDuringLadderCapture_BothSucceed);
        if (!IsolatedTestProcess.IsChild(Name))
        {
            IsolatedTestProcess.Run(typeof(ModPowZeroSyncGraphTests), Name);
            return;
        }

        const int NumWords = 64;
        const int Seed = 2391;
        using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

        var random = new Random(Seed);
        var modValue = GenerateFullWidthBigInteger(random, NumWords * 32) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, NumWords * 32) % modValue;
        uint[] expWords = [0x89ABCDEFu, 0x01234567u];
        var expValue = FromWords(expWords);
        var expected = BigInteger.ModPow(baseValue, expValue, modValue);

        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, NumWords);
        using var gpuExp = new GpuBigUInt(workspace, expWords);
        using var gpuMod = GpuBigUInt.FromBigInteger(workspace, modValue, NumWords);
        using var gpuResult = new GpuBigUInt(workspace, NumWords);

        using var start = new ManualResetEventSlim(false);
        using var done = new ManualResetEventSlim(false);
        Exception? otherThreadError = null;
        var hostBuffer = new int[16];

        var otherThread = new Thread(() =>
        {
            start.Wait();
            try
            {
                using var scope = new GpuKernelLoader.CudaContextScope();
                using var deviceBuffer = new GpuBuffer<int>(16);
                deviceBuffer.CopyToHost(hostBuffer);
            }
            catch (Exception ex)
            {
                otherThreadError = ex;
            }
            finally
            {
                done.Set();
            }
        });
        otherThread.Start();

        var manager = (DefaultGpuMemoryManager)workspace.MemoryManager;
        var observer = new LegacyCopyOnCaptureObserver(workspace, start, done);
        var launchesBefore = workspace.ModPowGraphLaunches;
        manager.Observer = observer;
        try
        {
            GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);
        }
        finally
        {
            manager.Observer = null;
            start.Set();
            otherThread.Join();
        }

        // Coverage witness: the legacy copy ran while the ladder capture was active, and graph playback ran.
        // Word 0 (0x89ABCDEF): 32 squares + 20 multiplies = 52; word 1 (0x01234567): 25 squares + 12 multiplies = 37;
        // total 89 steps, minus the eager first step = 88 graph launches.
        Assert.True(observer.Fired, "The memory observer never observed an active graph capture.");
        Assert.Equal(88L, workspace.ModPowGraphLaunches - launchesBefore);

        // Neither the concurrent legacy-stream copy nor ModPow may fail, and the result must match BigInteger.ModPow.
        // mutant: CapturedStreamGraph.cs:182 `graphProvider.BeginCapture(captureStream);`
        // -> `graphProvider.BeginCapture(workspace.BaseStream);`
        // mutant: CapturedStreamGraph.cs:11 `private const uint NonBlockingStreamFlag = 1;`
        // -> `private const uint NonBlockingStreamFlag = 0;`
        // mutant: GpuWorkspace.cs:56 `public IntPtr Stream => _manager?.CurrentStream ?? _stream;`
        // -> `public IntPtr Stream => _stream;`
        Assert.Null(otherThreadError);
        Assert.True(expected == gpuResult.ToBigInteger(), $"seed {Seed} mismatch");
        workspace.ThrowIfDeviceErrors();
    }

    [SkippableFact]
    [Category(TestCategory.Stress)]
    [Scenario(TestScenario.Boundary)]
    public void ModPow_LegacyCopiesOnAnotherThreadInALoop_NoFailures()
    {
        CudaCapability.EnsureGpuAvailable();
        const string Name = nameof(ModPow_LegacyCopiesOnAnotherThreadInALoop_NoFailures);
        if (!IsolatedTestProcess.IsChild(Name))
        {
            IsolatedTestProcess.Run(typeof(ModPowZeroSyncGraphTests), Name);
            return;
        }

        const int NumWords = 64;
        const int Seed = 2392;
        const int Iterations = 40;
        using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

        var random = new Random(Seed);
        var modValue = GenerateFullWidthBigInteger(random, NumWords * 32) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, NumWords * 32) % modValue;
        uint[] expWords = [0x89ABCDEFu, 0x01234567u];
        var expValue = FromWords(expWords);
        var expected = BigInteger.ModPow(baseValue, expValue, modValue);

        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, NumWords);
        using var gpuExp = new GpuBigUInt(workspace, expWords);
        using var gpuMod = GpuBigUInt.FromBigInteger(workspace, modValue, NumWords);
        using var gpuResult = new GpuBigUInt(workspace, NumWords);

        var stop = false;
        var copiesCompleted = 0;
        var legacyFailures = 0;
        Exception? firstLegacyError = null;
        var hostBuffer = new int[16];

        var otherThread = new Thread(() =>
        {
            using var scope = new GpuKernelLoader.CudaContextScope();
            while (!Volatile.Read(ref stop))
            {
                try
                {
                    using var deviceBuffer = new GpuBuffer<int>(16);
                    deviceBuffer.CopyToHost(hostBuffer);
                    Interlocked.Increment(ref copiesCompleted);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref legacyFailures);
                    Interlocked.CompareExchange(ref firstLegacyError, ex, null);
                }
            }
        });
        otherThread.Start();

        var launchesBefore = workspace.ModPowGraphLaunches;
        try
        {
            for (var i = 0; i < Iterations; i++)
            {
                GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);
                Assert.True(expected == gpuResult.ToBigInteger(), $"seed {Seed} iteration {i} mismatch");
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            otherThread.Join();
        }

        // Non-blocking capture stream keeps concurrent legacy-stream copies free of implicit capture conflicts.
        // mutant: CapturedStreamGraph.cs:11 `private const uint NonBlockingStreamFlag = 1;`
        // -> `private const uint NonBlockingStreamFlag = 0;`
        Assert.Equal(0, legacyFailures);
        Assert.Null(firstLegacyError);
        Assert.True(copiesCompleted > 0, "Background thread completed zero legacy copies.");
        Assert.Equal(Iterations * 88L, workspace.ModPowGraphLaunches - launchesBefore);
        workspace.ThrowIfDeviceErrors();
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0x89ABCDEFu, 0x01234567u)] // odd low bit: eager first step is Multiply
    [InlineData(0x89ABCDEEu, 0x01234567u)] // even low bit: eager first step is Square
    public void ModPow_WhileAnotherThreadHoldsTheWorkspaceCapture_RunsThePlainLoop(uint expWord0, uint expWord1)
    {
        CudaCapability.EnsureGpuAvailable();
        const int NumWords = 64;
        var seed = 2393 + (int)(expWord0 & 1u);
        using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

        var random = new Random(seed);
        var modValue = GenerateFullWidthBigInteger(random, NumWords * 32) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, NumWords * 32) % modValue;
        uint[] expWords = [expWord0, expWord1];
        var expected = BigInteger.ModPow(baseValue, FromWords(expWords), modValue);

        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, NumWords);
        using var gpuExp = new GpuBigUInt(workspace, expWords);
        using var gpuMod = GpuBigUInt.FromBigInteger(workspace, modValue, NumWords);
        using var gpuResult = new GpuBigUInt(workspace, NumWords);

        var manager = (DefaultGpuMemoryManager)workspace.MemoryManager;
        using var deferralHeld = new ManualResetEventSlim(false);
        using var releaseDeferral = new ManualResetEventSlim(false);
        Exception? holderError = null;

        var holder = new Thread(() =>
        {
            CaptureDeferral? deferral = null;
            try
            {
                using var scope = workspace.ActivateContext();
                deferral = manager.BeginDeferral();
                deferralHeld.Set();
                releaseDeferral.Wait();
            }
            catch (Exception ex)
            {
                holderError = ex;
                deferralHeld.Set();
            }
            finally
            {
                if (deferral is not null)
                {
                    using var scope = workspace.ActivateContext();
                    manager.ReturnDeferred(manager.EndDeferral(deferral).Buffers);
                }
            }
        });
        holder.Start();
        Assert.True(deferralHeld.Wait(TimeSpan.FromSeconds(10)), "Timed out waiting for holder thread deferral.");

        var launchesBefore = workspace.ModPowGraphLaunches;
        try
        {
            // When another thread holds the workspace capture session, ExecuteLadder must fall back to the plain
            // loop (skipping the eager first step) instead of throwing InvalidOperationException.
            // mutant: ModPowZeroSyncController.cs:103
            // `if (!TryCaptureStepGraphs(buffers, out var multiplyGraph, out var squareGraph))`
            // -> throw InvalidOperationException instead of running the plain-loop fallback
            GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);
        }
        finally
        {
            releaseDeferral.Set();
            holder.Join();
        }

        Assert.Null(holderError);
        Assert.Equal(0L, workspace.ModPowGraphLaunches - launchesBefore);
        Assert.True(expected == gpuResult.ToBigInteger(), $"seed {seed} mismatch");
        workspace.ThrowIfDeviceErrors();
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(2048, 64)]
    [InlineData(2048, 2048)]
    [InlineData(4096, 64)]
    [InlineData(4096, 4096)]
    [InlineData(8192, 64)]
    public void ModPow_ZeroSyncGraphWidths_MatchesBigIntegerReference(int modulusBits, int expBits)
    {
        CudaCapability.EnsureGpuAvailable();

        var numWords = modulusBits / 32;
        var expWords = (expBits + 31) / 32;
        using var workspace = new GpuWorkspace(Math.Max(4096, numWords * 16), GpuExecutionEngine.GSharpAot);

        var seed = 2346 + modulusBits * 17 + expBits;
        var random = new Random(seed);
        var modValue = GenerateFullWidthBigInteger(random, modulusBits) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, modulusBits) % modValue;
        var expValue = GenerateFullWidthBigInteger(random, expBits);
        var expected = BigInteger.ModPow(baseValue, expValue, modValue);

        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, numWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(workspace, expValue, expWords);
        using var gpuMod = GpuBigUInt.FromBigInteger(workspace, modValue, numWords);
        using var gpuResult = new GpuBigUInt(workspace, numWords);

        var launchesBefore = workspace.ModPowGraphLaunches;
        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);
        var actual = gpuResult.ToBigInteger();

        // Result must match host BigInteger.ModPow and use CUDA Graph launches for wide moduli.
        // mutant: ModPowZeroSyncController.cs:380 `squareGraph.Launch();`
        // -> `squareGraph.Launch(); squareGraph.Launch();`
        Assert.True(expected == actual, $"seed {seed}: expected {expected}, actual {actual}");
        Assert.True(workspace.ModPowGraphLaunches - launchesBefore > 0);
        workspace.ThrowIfDeviceErrors();
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(64, true, 128)]
    [InlineData(63, false, 65)]
    public void ModPow_AllOnesAndPowerOfTwoExponents_MatchesReferenceAndLaunchCount(
        int bitCount,
        bool allOnes,
        int expectedSteps)
    {
        CudaCapability.EnsureGpuAvailable();

        const int NumWords = 64;
        using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

        var seed = 2347 + bitCount;
        var random = new Random(seed);
        var modValue = GenerateFullWidthBigInteger(random, NumWords * 32) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, NumWords * 32) % modValue;
        var expValue = allOnes ? (BigInteger.One << bitCount) - 1 : BigInteger.One << bitCount;
        var expected = BigInteger.ModPow(baseValue, expValue, modValue);

        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, NumWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(workspace, expValue, 2);
        using var gpuMod = GpuBigUInt.FromBigInteger(workspace, modValue, NumWords);
        using var gpuResult = new GpuBigUInt(workspace, NumWords);

        var launchesBefore = workspace.ModPowGraphLaunches;
        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);

        // Every multiply and square step must execute; exact graph launches equal steps - 1 (after 1 eager step).
        // mutant: ModPowZeroSyncController.cs:369 `multiplyGraph.Launch();` -> removed
        // mutant: ModPowZeroSyncController.cs:381 `workspace.CountModPowGraphLaunch();` -> removed
        Assert.True(expected == gpuResult.ToBigInteger(), $"seed {seed} mismatch");
        Assert.Equal(expectedSteps - 1L, workspace.ModPowGraphLaunches - launchesBefore);
        workspace.ThrowIfDeviceErrors();
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ModPow_SparseExponentWithZeroWordsInPaddedBuffer_BreaksAtTopNonZeroWord()
    {
        CudaCapability.EnsureGpuAvailable();

        const int NumWords = 64;
        using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

        const int Seed = 2349;
        var random = new Random(Seed);
        var modValue = GenerateFullWidthBigInteger(random, NumWords * 32) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, NumWords * 32) % modValue;
        var expValue = (BigInteger.One << 200) + 1;
        var expected = BigInteger.ModPow(baseValue, expValue, modValue);

        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, NumWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(workspace, expValue, NumWords);
        using var gpuMod = GpuBigUInt.FromBigInteger(workspace, modValue, NumWords);
        using var gpuResult = new GpuBigUInt(workspace, NumWords);

        // Word 0 has 1 mul + 32 sq = 33; words 1..5 have 5 * 32 = 160 sq; word 6 has 32 sq + 1 mul = 33;
        // total = 226 steps, minus 1 eager first step = 225 graph launches.
        const long ExpectedLaunches = 225L;
        var launchesBefore = workspace.ModPowGraphLaunches;
        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);

        // Outer all-zero break must stop at word 7 rather than launching 56 full words of 32 squarings plus 1
        // squaring of the last word (1793 extra launches).
        // mutant: ModPowZeroSyncController.cs:215 `break;` -> removed
        Assert.True(expected == gpuResult.ToBigInteger(), $"seed {Seed} mismatch");
        Assert.Equal(ExpectedLaunches, workspace.ModPowGraphLaunches - launchesBefore);
        workspace.ThrowIfDeviceErrors();
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0u, 0L)]
    [InlineData(1u, 0L)]
    [InlineData(2u, 0L)]
    [InlineData(3u, 0L)]
    [InlineData(4u, 0L)]
    [InlineData(5u, 4L)]
    [InlineData(8u, 4L)]
    public void ModPow_FewStepsAndMinimumGraphStepsBoundary_UsesExpectedPathAndMatchesReference(
        uint expWord,
        long expectedLaunches)
    {
        CudaCapability.EnsureGpuAvailable();

        const int NumWords = 64;
        using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

        var seed = 2350 + (int)expWord;
        var random = new Random(seed);
        var modValue = GenerateFullWidthBigInteger(random, NumWords * 32) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, NumWords * 32) % modValue;
        var expValue = new BigInteger(expWord);
        var expected = BigInteger.ModPow(baseValue, expValue, modValue);

        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, NumWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(workspace, expValue, 1);
        using var gpuMod = GpuBigUInt.FromBigInteger(workspace, modValue, NumWords);
        using var gpuResult = new GpuBigUInt(workspace, NumWords);

        var launchesBefore = workspace.ModPowGraphLaunches;
        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);

        // Below MinimumGraphSteps (5) the plain loop must run with 0 graph launches; at 5 steps, graph playback runs.
        // mutant: ModPowZeroSyncController.cs:12 `internal const int MinimumGraphSteps = 5;` -> `... = 0;`
        Assert.True(expected == gpuResult.ToBigInteger(), $"seed {seed}, exp {expWord} mismatch");
        Assert.Equal(expectedLaunches, workspace.ModPowGraphLaunches - launchesBefore);
        workspace.ThrowIfDeviceErrors();
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ExecuteLadder_InsideUserStreamCapture_SkipsInternalCaptureAndReplaysCorrectly()
    {
        CudaCapability.EnsureGpuAvailable();

        const int NumWords = 64;
        using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

        const int Seed = 2351;
        var random = new Random(Seed);
        var modValue = GenerateFullWidthBigInteger(random, NumWords * 32) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, NumWords * 32) % modValue;
        var expValue = (BigInteger.One << 64) - 1;
        var expected = BigInteger.ModPow(baseValue, expValue, modValue);

        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, NumWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(workspace, expValue, 2);
        using var gpuMod = GpuBigUInt.FromBigInteger(workspace, modValue, NumWords);
        using var gpuResult = new GpuBigUInt(workspace, NumWords);

        // Run one ModPow call before stream capture so pooled buffers and loaded modules are already initialized.
        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);
        Assert.True(expected == gpuResult.ToBigInteger(), $"seed {Seed} pre-capture call mismatch");

        using var buffers = ModPowZeroSyncController.PrepareLadder(gpuBase, gpuMod);
        uint[] hostExp = [uint.MaxValue, uint.MaxValue];

        var launchesBefore = workspace.ModPowGraphLaunches;
        var graph = IntPtr.Zero;
        var graphExec = IntPtr.Zero;
        var manager = (DefaultGpuMemoryManager)workspace.MemoryManager;
        var deferral = manager.BeginDeferral();
        CapturedResources? captured = null;
        try
        {
            workspace.Provider.BeginCapture(workspace.Stream);
            // CaptureChecks.IsCapturing(workspace) must skip nested graph capture inside a user-started capture.
            // mutant: ModPowZeroSyncController.cs:85 `CaptureChecks.IsCapturing(buffers.Workspace) ||` -> removed
            ModPowZeroSyncController.ExecuteLadder(buffers, hostExp, 2);
            graph = workspace.Provider.EndCapture(workspace.Stream);
            captured = manager.EndDeferral(deferral);
            graphExec = workspace.Provider.InstantiateGraph(graph);
            workspace.Provider.LaunchGraph(graphExec, workspace.Stream);
            workspace.Provider.SynchronizeStream(workspace.Stream);

            gpuResult.CopyFrom(buffers.TempResult, numWords: NumWords);
            Assert.True(expected == gpuResult.ToBigInteger(), $"seed {Seed} captured replay mismatch");
            Assert.Equal(0L, workspace.ModPowGraphLaunches - launchesBefore);
            workspace.ThrowIfDeviceErrors();
        }
        finally
        {
            if (captured is null)
            {
                AbortUserStreamCaptureIfActive(workspace);
                captured = manager.EndDeferral(deferral);
            }

            if (graph != IntPtr.Zero || graphExec != IntPtr.Zero)
            {
                workspace.Provider.DestroyGraph(graph, graphExec);
            }

            manager.ReturnDeferred(captured.Value.Buffers);
        }
    }

    private static void AbortUserStreamCaptureIfActive(GpuWorkspace workspace)
    {
        using var scope = workspace.ActivateContext();
        if (CudaDriverApi.cuStreamIsCapturing(workspace.Stream, out var status) == CUresult.CudaSuccess &&
            status != CUstreamCaptureStatus.None)
        {
            CudaDriverApi.cuStreamEndCapture(workspace.Stream, out var aborted);
            if (aborted != IntPtr.Zero)
            {
                CudaDriverApi.cuGraphDestroy(aborted);
            }
        }
    }

    private static BigInteger GenerateFullWidthBigInteger(Random random, int bits)
    {
        var bytes = new byte[bits / 8 + 1];
        random.NextBytes(bytes);
        bytes[^1] = 0;
        bytes[^2] |= 0x80;
        return new BigInteger(bytes);
    }

    private static BigInteger FromWords(uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        Buffer.BlockCopy(words, 0, bytes, 0, bytes.Length);
        return new BigInteger(bytes, isUnsigned: true);
    }

    private sealed class LegacyCopyOnCaptureObserver(
        GpuWorkspace workspace,
        ManualResetEventSlim start,
        ManualResetEventSlim done) : IGpuMemoryObserver
    {
        private bool _fired;

        public bool Fired => Volatile.Read(ref _fired);

        public void Leased(IntPtr pointer, int numWords)
        {
            if (!_fired && CaptureChecks.IsCapturing(workspace))
            {
                Volatile.Write(ref _fired, true);
                start.Set();
                Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "Timed out waiting for thread B legacy copy.");
            }
        }

        public void Released(IntPtr pointer, int numWords)
        {
        }
    }
}
