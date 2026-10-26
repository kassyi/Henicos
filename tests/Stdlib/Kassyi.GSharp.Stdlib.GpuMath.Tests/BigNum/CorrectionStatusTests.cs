using System.Runtime.InteropServices;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

// Properties checked here (property-test-design §1):
// - A device correction (ReduceZeroSync, ReciprocalMega, InverseSqrtMega) that does not converge within its step limit
//   is reported as InvalidOperationException, naming the operation, at the next read of the same workspace's stream.
//   No synchronization is added: the status word travels with an existing read.
// - After the exception the status is reset, so the same workspace computes correctly again, and a failure on one
//   workspace is not reported on another.
// Every expected value is computed from the words that the test holds.
public class CorrectionStatusTests : IDisposable
{
    private const int ReciprocalWords = 16;
    private const int ReciprocalTargetBits = 2 * ReciprocalWords * 32;

    // The corrections stop after 1000 steps; a guess 5000 steps away cannot be corrected.
    private const int FarOffset = 5000;

    private readonly GpuKernelLoader.CudaContextScope _scope;

    public CorrectionStatusTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    // ReduceZeroSync with a zero reciprocal estimates q = 0, so the remainder needs about a / b (2^64) subtractions.
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void ReduceZeroSync_ZeroReciprocal_ThrowsOnNextReadAndRecovers()
    {
        const int DividendWords = 4;
        const int DivisorWords = 2;
        const int TargetBits = DivisorWords * 32 + DividendWords * 32 + 512;
        const int RecipWords = TargetBits / 32 + 2;
        var a = FromWords(RandomWords(seed: 901, DividendWords));
        var b = FromWords(RandomWords(seed: 902, DivisorWords));

        using var workspace = new GpuWorkspace(RecipWords * 4, GpuExecutionEngine.GSharpAot);
        using var gpuA = new GpuBigUInt(workspace, ToWords(a, DividendWords));
        using var gpuB = new GpuBigUInt(workspace, ToWords(b, DivisorWords));
        using var zeroReciprocal = new GpuBigUInt(workspace, new uint[RecipWords]);
        using var gpuQ = new GpuBigUInt(workspace, DividendWords);
        using var gpuR = new GpuBigUInt(workspace, DividendWords);

        GpuBigIntDivRem.ReduceZeroSync(gpuA, gpuB, zeroReciprocal, TargetBits, DividendWords, gpuQ, gpuR);

        // The unconverged correction is reported at the next read, naming the operation.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntDivRemKernels.cu.cs:252 `Gpu.AtomicAdd(ref status[0], 1u);` -> `status[1] = failureCode;` (not counted)
        // input: this test and every ReciprocalMega test
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:437 `rightOperand.Workspace.CorrectionStatus?.MarkPending();` -> removed
        // input: this test (no read checks the word)
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/GSharpKernelProvider.cs:81 `status?.EndRead();` (CopyDeviceToHost) -> removed
        // input: this test, ReciprocalMega and InverseSqrtMega tests that read with ToBigInteger
        var exception = Assert.Throws<InvalidOperationException>(() => gpuR.ToBigInteger());
        Assert.Contains("ReduceZeroSync", exception.Message);

        // The status was reset: the same workspace reduces correctly with an exact reciprocal.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/GpuCorrectionStatus.cs:186 `if (count <= reported)` -> `if (count <= 0)` (every positive count reported again)
        // input: this test, every read kind, InverseSqrtMega (the read after the exception throws again)
        using var reciprocal = new GpuBigUInt(workspace, ToWords((BigInteger.One << TargetBits) / b, RecipWords));
        GpuBigIntDivRem.ReduceZeroSync(gpuA, gpuB, reciprocal, TargetBits, DividendWords, gpuQ, gpuR);
        Assert.Equal(a % b, gpuR.ToBigInteger());
        Assert.Equal(a / b, gpuQ.ToBigInteger());
    }

    // ReduceZeroSync stays capture-safe: captured into a CUDA graph, its replay reports the failure at the next read.
    // The read between instantiation and launch consumes the check that the capture itself requested, so only the
    // replay's own request (LaunchGraph) can make the last read throw.
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void ReduceZeroSync_ZeroReciprocalReplayedFromGraph_ThrowsOnNextRead()
    {
        const int DividendWords = 4;
        const int DivisorWords = 2;
        const int TargetBits = DivisorWords * 32 + DividendWords * 32 + 512;
        const int RecipWords = TargetBits / 32 + 2;
        using var workspace = new GpuWorkspace(RecipWords * 4, GpuExecutionEngine.GSharpAot);
        using var gpuA = new GpuBigUInt(workspace, RandomWords(seed: 941, DividendWords));
        using var gpuB = new GpuBigUInt(workspace, RandomWords(seed: 942, DivisorWords));
        using var zeroReciprocal = new GpuBigUInt(workspace, new uint[RecipWords]);
        using var gpuQ = new GpuBigUInt(workspace, DividendWords);
        using var gpuR = new GpuBigUInt(workspace, DividendWords);

        // Warm-up outside the capture fills the memory pool; its own failure is reported and reset here.
        GpuBigIntDivRem.ReduceZeroSync(gpuA, gpuB, zeroReciprocal, TargetBits, DividendWords, gpuQ, gpuR);
        Assert.Throws<InvalidOperationException>(() => workspace.Provider.SynchronizeStream(workspace.Stream));

        workspace.Provider.BeginCapture(workspace.Stream);
        GpuBigIntDivRem.ReduceZeroSync(gpuA, gpuB, zeroReciprocal, TargetBits, DividendWords, gpuQ, gpuR);
        var graph = workspace.Provider.EndCapture(workspace.Stream);
        var graphExec = workspace.Provider.InstantiateGraph(graph);
        try
        {
            // Nothing ran yet, so this read finds no failure.
            workspace.Provider.SynchronizeStream(workspace.Stream);

            workspace.Provider.LaunchGraph(graphExec, workspace.Stream);

            // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/GSharpKernelProvider.cs:309 `GpuCorrectionStatus.Find(stream)?.MarkPending();` (LaunchGraph) -> removed
            // input: this test
            var exception = Assert.Throws<InvalidOperationException>(() => gpuR.ToBigInteger());
            Assert.Contains("ReduceZeroSync", exception.Message);
        }
        finally
        {
            workspace.Provider.DestroyGraph(graph, graphExec);
        }
    }

    public static TheoryData<string> ReadKinds() =>
    [
        "CopyDeviceToHost",
        "ReadWord",
        "KernelResult",
        "SynchronizeStream",
        "SynchronizeStreamAsync",
        "CopyDeviceToHostAsync"
    ];

    // Every kind of existing read reports the failure: GpuBigUInt.ToBigInteger (CopyDeviceToHost), GetIsEven
    // (ReadWord), GetBitLength (the kernel-result read of KernelBasicMathHandler), SynchronizeStream and the two
    // asynchronous reads, whose ValueTask completes with the exception.
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(ReadKinds))]
    public async Task ReciprocalMega_FarGuessWithoutIterations_ThrowsOnEveryReadKind(string readKind)
    {
        var b = FromWords(RandomWords(seed: 911, ReciprocalWords));
        var floorY = (BigInteger.One << ReciprocalTargetBits) / b;
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var gpuB = new GpuBigUInt(workspace, ToWords(b, ReciprocalWords));
        using var gpuX = new GpuBigUInt(workspace, ToWords(floorY + FarOffset, ReciprocalTargetBits / 32));

        RunReciprocalMega(workspace, gpuB, gpuX, iterations: 0);

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/GSharpKernelProvider.cs:81 `status?.EndRead();` (CopyDeviceToHost) -> removed
        // input: CopyDeviceToHost
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/GSharpKernelProvider.cs:132 `status?.EndRead();` (ReadWord) -> removed
        // input: ReadWord
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelBasicMathHandler.cs:52 `status?.EndRead();` -> removed
        // input: KernelResult
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/GSharpKernelProvider.cs:156 `status?.EndRead();` (SynchronizeStream) -> removed
        // input: SynchronizeStream
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/GpuCorrectionStatus.cs:349 `owner.EndRead();` (asynchronous reads) -> removed
        // input: SynchronizeStreamAsync, CopyDeviceToHostAsync
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs:377 `workspace.CorrectionStatus?.MarkPending();` (ReciprocalMega) -> removed
        // input: every read kind
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            switch (readKind)
            {
                case "CopyDeviceToHost":
                    _ = gpuX.ToBigInteger();
                    break;
                case "ReadWord":
                    _ = gpuX.GetIsEven();
                    break;
                case "KernelResult":
                    _ = gpuX.GetBitLength();
                    break;
                case "SynchronizeStream":
                    workspace.Provider.SynchronizeStream(workspace.Stream);
                    break;
                case "SynchronizeStreamAsync":
                    await workspace.Provider.SynchronizeStreamAsync(workspace.Stream);
                    break;
                case "CopyDeviceToHostAsync":
                    _ = await gpuX.ToHostArrayAsync();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(readKind), readKind, "Unknown read kind.");
            }
        });
        Assert.Contains("Reciprocal", exception.Message);

        // Reset: an exact reciprocal of the same divisor follows on the same workspace.
        using var exact = new GpuBigUInt(workspace, ReciprocalTargetBits / 32 + 1);
        GpuBigIntDivRem.Reciprocal(gpuB, exact, ReciprocalTargetBits);
        Assert.Equal(floorY, exact.ToBigInteger());
    }

    // Both directions of the reciprocal correction report the failure (too large: decrements, too small: increments).
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(FarOffset)]
    [InlineData(-FarOffset)]
    public void ReciprocalMega_FarGuessWithoutIterations_ThrowsOnNextRead(int offset)
    {
        var b = FromWords(RandomWords(seed: 912, ReciprocalWords));
        var floorY = (BigInteger.One << ReciprocalTargetBits) / b;
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var gpuB = new GpuBigUInt(workspace, ToWords(b, ReciprocalWords));
        using var gpuX = new GpuBigUInt(workspace, ToWords(floorY + offset, ReciprocalTargetBits / 32));

        RunReciprocalMega(workspace, gpuB, gpuX, iterations: 0);

        var exception = Assert.Throws<InvalidOperationException>(() => gpuX.ToBigInteger());
        Assert.Contains("Reciprocal", exception.Message);
    }

    // Both directions of the inverse square root correction report the failure, and the workspace recovers.
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(FarOffset)]
    [InlineData(-FarOffset)]
    public void InverseSqrtMega_FarGuessWithoutIterations_ThrowsOnNextReadAndRecovers(int offset)
    {
        const int NumWords = 16;
        const int MarginWords = 4;
        const int PrecisionBits = (NumWords + MarginWords) * 32;
        var a = FromWords(RandomWords(seed: 921, NumWords));
        var floorY = FloorSqrt((BigInteger.One << (2 * PrecisionBits)) / a);

        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var gpuA = new GpuBigUInt(workspace, ToWords(a, NumWords));
        using var gpuX = new GpuBigUInt(workspace, ToWords(floorY + offset, NumWords + MarginWords));

        new KernelAdvancedMathHandler().InverseSqrtMega(workspace, gpuA, gpuX, MarginWords,
            actualBits: NumWords * 32, iterations: 0);

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntSqrtKernels.cu.cs:195 `Gpu.AtomicAdd(ref status[0], 1u);` -> `status[1] = failureCode;` (not counted)
        // input: both offsets
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs:243 `workspace.CorrectionStatus?.MarkPending();` (InverseSqrtMega) -> removed
        // input: both offsets
        var exception = Assert.Throws<InvalidOperationException>(() => gpuX.ToBigInteger());
        Assert.Contains("InverseSqrt", exception.Message);

        using var exact = new GpuBigUInt(workspace, NumWords + MarginWords);
        GpuBigIntDivRem.InverseSqrt(gpuA, exact, MarginWords);
        Assert.Equal(floorY, exact.ToBigInteger());
    }

    // The status belongs to one workspace. The other workspace ran a converging ReciprocalMega first, so its next read
    // does copy its own status word: that read must not report the failure of the failing workspace, which still
    // reports it afterwards.
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void ReciprocalMega_Failure_IsReportedOnlyOnItsWorkspace()
    {
        var b = FromWords(RandomWords(seed: 931, ReciprocalWords));
        var floorY = (BigInteger.One << ReciprocalTargetBits) / b;
        using var failing = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var other = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var gpuB = new GpuBigUInt(failing, ToWords(b, ReciprocalWords));
        using var gpuX = new GpuBigUInt(failing, ToWords(floorY + FarOffset, ReciprocalTargetBits / 32));
        using var otherB = new GpuBigUInt(other, ToWords(b, ReciprocalWords));
        using var otherX = new GpuBigUInt(other, ToWords(floorY + 1, ReciprocalTargetBits / 32));

        RunReciprocalMega(other, otherB, otherX, iterations: 0);
        RunReciprocalMega(failing, gpuB, gpuX, iterations: 0);

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/GpuCorrectionStatus.cs:88 `s_byStream[stream] = this;` and :107 `TryGetValue(stream, ...)` -> the fixed key `(IntPtr)1`
        // input: this test (the failing stream finds the other status, whose read copies its own clean word)
        Assert.Equal(floorY, otherX.ToBigInteger());
        var exception = Assert.Throws<InvalidOperationException>(() => gpuX.ToBigInteger());
        Assert.Contains("Reciprocal", exception.Message);
    }

    public static TheoryData<int> StepLimitOffsets() => [-1000, -999, 999, 1000];

    // The corrections move at most 1000 steps, and the pass after the last move only checks: a guess exactly 1000
    // steps away is corrected without an exception, one step further is reported.
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(StepLimitOffsets))]
    public void ReciprocalMega_GuessWithinStepLimit_IsCorrectedWithoutException(int offset)
    {
        var b = FromWords(RandomWords(seed: 951, ReciprocalWords));
        var floorY = (BigInteger.One << ReciprocalTargetBits) / b;
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var gpuB = new GpuBigUInt(workspace, ToWords(b, ReciprocalWords));
        using var gpuX = new GpuBigUInt(workspace, ToWords(floorY + offset, ReciprocalTargetBits / 32));

        RunReciprocalMega(workspace, gpuB, gpuX, iterations: 0);

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntDivRemKernels.cu.cs:213 `step <= maxSteps` -> `step < maxSteps` (no check after the last move)
        // input: offsets -1000 and 1000
        Assert.Equal(floorY, gpuX.ToBigInteger());
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(-1001)]
    [InlineData(1001)]
    public void ReciprocalMega_GuessOneBeyondStepLimit_ThrowsOnNextRead(int offset)
    {
        var b = FromWords(RandomWords(seed: 951, ReciprocalWords));
        var floorY = (BigInteger.One << ReciprocalTargetBits) / b;
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var gpuB = new GpuBigUInt(workspace, ToWords(b, ReciprocalWords));
        using var gpuX = new GpuBigUInt(workspace, ToWords(floorY + offset, ReciprocalTargetBits / 32));

        RunReciprocalMega(workspace, gpuB, gpuX, iterations: 0);

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntDivRemKernels.cu.cs:165 `CorrectIntoDivisorRange(..., n, 1000,` -> `1001`
        // input: both offsets
        Assert.Throws<InvalidOperationException>(() => gpuX.ToBigInteger());
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(StepLimitOffsets))]
    public void InverseSqrtMega_GuessWithinStepLimit_IsCorrectedWithoutException(int offset)
    {
        var (workspace, gpuA, gpuX, floorY) = CreateInverseSqrtGuess(offset);
        using (workspace)
        using (gpuA)
        using (gpuX)
        {
            RunInverseSqrtMega(workspace, gpuA, gpuX);

            // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntSqrtKernels.cu.cs:144 `step <= maxSteps` -> `step < maxSteps`
            // input: offsets -1000 and 1000
            Assert.Equal(floorY, gpuX.ToBigInteger());
        }
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(-1001)]
    [InlineData(1001)]
    public void InverseSqrtMega_GuessOneBeyondStepLimit_ThrowsOnNextRead(int offset)
    {
        var (workspace, gpuA, gpuX, _) = CreateInverseSqrtGuess(offset);
        using (workspace)
        using (gpuA)
        using (gpuX)
        {
            RunInverseSqrtMega(workspace, gpuA, gpuX);

            // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntSqrtKernels.cu.cs:103 `slackWords, axWords, n, 1000,` (the step limit of CorrectInverseSqrtSlack) -> `1001`
            // input: both offsets
            Assert.Throws<InvalidOperationException>(() => gpuX.ToBigInteger());
        }
    }

    // A read that fails before its wait (here the asynchronous copy rejects a host buffer shorter than numWords, after
    // the status copy was enqueued) leaves the copy to the next read, which reports the failure.
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void ReciprocalMega_ReadThatThrowsBeforeItsWait_LeavesFailureForNextRead()
    {
        var b = FromWords(RandomWords(seed: 961, ReciprocalWords));
        var floorY = (BigInteger.One << ReciprocalTargetBits) / b;
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var gpuB = new GpuBigUInt(workspace, ToWords(b, ReciprocalWords));
        using var gpuX = new GpuBigUInt(workspace, ToWords(floorY + FarOffset, ReciprocalTargetBits / 32));

        RunReciprocalMega(workspace, gpuB, gpuX, iterations: 0);

        var tooShort = new uint[1];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = workspace.Provider.CopyDeviceToHostAsync(tooShort, gpuX.DevicePointer, 4, workspace.Stream);
        });

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/GpuCorrectionStatus.cs:168 `MarkPending();` (in AbandonRead) -> removed
        // input: this test
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/GSharpKernelProvider.cs:104 `status?.AbandonRead();` (CopyDeviceToHostAsync) -> removed
        // input: this test
        var exception = Assert.Throws<InvalidOperationException>(() => gpuX.ToBigInteger());
        Assert.Contains("Reciprocal", exception.Message);
    }

    // An asynchronous read with a token that is already canceled reads nothing, so the failure stays for the next read.
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("SynchronizeStreamAsync")]
    [InlineData("CopyDeviceToHostAsync")]
    public async Task ReciprocalMega_CanceledAsyncRead_LeavesFailureForNextRead(string readKind)
    {
        var b = FromWords(RandomWords(seed: 962, ReciprocalWords));
        var floorY = (BigInteger.One << ReciprocalTargetBits) / b;
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var gpuB = new GpuBigUInt(workspace, ToWords(b, ReciprocalWords));
        using var gpuX = new GpuBigUInt(workspace, ToWords(floorY + FarOffset, ReciprocalTargetBits / 32));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        RunReciprocalMega(workspace, gpuB, gpuX, iterations: 0);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (readKind == "SynchronizeStreamAsync")
            {
                await workspace.Provider.SynchronizeStreamAsync(workspace.Stream, canceled.Token);
            }
            else
            {
                _ = await gpuX.ToHostArrayAsync(canceled.Token);
            }
        });

        var exception = Assert.Throws<InvalidOperationException>(() => gpuX.ToBigInteger());
        Assert.Contains("Reciprocal", exception.Message);
    }

    // A read attempted while the stream is being captured must not put the status copy into the caller's graph: the
    // request stays pending, and the first read after the capture reports the failure.
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public async Task ReciprocalMega_ReadDuringCapture_KeepsFailureForReadAfterCapture()
    {
        var b = FromWords(RandomWords(seed: 971, ReciprocalWords));
        var floorY = (BigInteger.One << ReciprocalTargetBits) / b;
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var gpuB = new GpuBigUInt(workspace, ToWords(b, ReciprocalWords));
        using var gpuX = new GpuBigUInt(workspace, ToWords(floorY + FarOffset, ReciprocalTargetBits / 32));

        RunReciprocalMega(workspace, gpuB, gpuX, iterations: 0);

        workspace.Provider.BeginCapture(workspace.Stream);
        var graph = IntPtr.Zero;
        try
        {
            // CudaStreamCompletion refuses to await a capturing stream.
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await workspace.Provider.SynchronizeStreamAsync(workspace.Stream));
            Assert.Contains("captured", refused.Message);
        }
        finally
        {
            graph = workspace.Provider.EndCapture(workspace.Stream);
            workspace.Provider.DestroyGraph(graph, IntPtr.Zero);
        }

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/GpuCorrectionStatus.cs:138 `return null;` (stream being captured) -> removed
        // input: this test (the copy goes into the graph and the request is consumed)
        var exception = Assert.Throws<InvalidOperationException>(() => gpuX.ToBigInteger());
        Assert.Contains("Reciprocal", exception.Message);
    }

    // The chunk wait of the ModPow mega kernel is an existing wait too: a failure recorded before the call is reported
    // there. The failure code is written into the status word directly, because a converging Reciprocal precedes the mega
    // kernel in ModPow.
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void ModPowNttMegaKernel_PendingFailure_IsReportedAtChunkWait()
    {
        const int NumWords = 2;
        const int TargetBits = NumWords * 32 + 2 * NumWords * 32 + 512;
        var modulus = FromWords(RandomWords(seed: 981, NumWords));
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var gpuBase = new GpuBigUInt(workspace, ToWords(modulus - 12345, NumWords));
        using var gpuMod = new GpuBigUInt(workspace, ToWords(modulus, NumWords));
        using var gpuRecip = new GpuBigUInt(workspace, ToWords((BigInteger.One << TargetBits) / modulus, TargetBits / 32 + 1));
        using var gpuExp = new GpuBigUInt(workspace, [3u]);
        using var gpuResult = new GpuBigUInt(workspace, NumWords);
        var status = workspace.CorrectionStatus ?? throw new InvalidOperationException("The workspace has no status.");
        var handler = new KernelAdvancedMathHandler();

        // Warm-up: the first call builds the twiddle cache, whose upload waits on the stream (another reader). After it,
        // the chunk wait is the only wait in the call.
        handler.ExecuteModPowNttMegaKernel(workspace, gpuBase, gpuResult, gpuMod, gpuRecip, gpuExp, NumWords, TargetBits,
            gridDim: 1, blockDim: 256);

        // One failure (word 0 counts the failures) of the reciprocal correction (word 1 holds the code).
        workspace.Provider.WriteWord(status.DevicePointer, 0, 1u, workspace.Stream);
        workspace.Provider.WriteWord(status.DevicePointer, 1, GpuBigIntDivRemKernels.ReciprocalCorrectionFailed,
            workspace.Stream);
        status.MarkPending();

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs:835 `status?.EndRead();` -> removed
        // input: this test
        var exception = Assert.Throws<InvalidOperationException>(() =>
            handler.ExecuteModPowNttMegaKernel(workspace, gpuBase, gpuResult, gpuMod, gpuRecip, gpuExp, NumWords,
                TargetBits, gridDim: 1, blockDim: 256));
        Assert.Contains("Reciprocal", exception.Message);
    }

    private static (GpuWorkspace Workspace, GpuBigUInt A, GpuBigUInt X, BigInteger FloorY) CreateInverseSqrtGuess(int offset)
    {
        const int NumWords = 16;
        const int MarginWords = 4;
        const int PrecisionBits = (NumWords + MarginWords) * 32;
        var a = FromWords(RandomWords(seed: 991, NumWords));
        var floorY = FloorSqrt((BigInteger.One << (2 * PrecisionBits)) / a);
        var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        var gpuA = new GpuBigUInt(workspace, ToWords(a, NumWords));
        var gpuX = new GpuBigUInt(workspace, ToWords(floorY + offset, NumWords + MarginWords));
        return (workspace, gpuA, gpuX, floorY);
    }

    private static void RunInverseSqrtMega(GpuWorkspace workspace, GpuBigUInt operand, GpuBigUInt guess) =>
        new KernelAdvancedMathHandler().InverseSqrtMega(workspace, operand, guess, marginWords: 4,
            actualBits: operand.NumWords * 32, iterations: 0);

    // Three asynchronous reads in a row reuse one source: a failure, a clean read, then a new failure. The clean read in
    // the middle must succeed, so a source whose state is not reset between reads (it would replay the first exception)
    // is caught; the third read reports the new failure only.
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public async Task ReciprocalMega_SequentialAsyncReads_ReportEachNewFailureOnce()
    {
        var b = FromWords(RandomWords(seed: 1001, ReciprocalWords));
        var floorY = (BigInteger.One << ReciprocalTargetBits) / b;
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var gpuB = new GpuBigUInt(workspace, ToWords(b, ReciprocalWords));
        using var gpuX = new GpuBigUInt(workspace, ToWords(floorY + FarOffset, ReciprocalTargetBits / 32));
        var status = workspace.CorrectionStatus ?? throw new InvalidOperationException("The workspace has no status.");

        RunReciprocalMega(workspace, gpuB, gpuX, iterations: 0);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await workspace.Provider.SynchronizeStreamAsync(workspace.Stream));

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/GpuCorrectionStatus.cs:295 `_core.Reset();` -> removed
        // input: this test (the clean read replays the first exception; the late completion crashes the test host)
        status.MarkPending();
        await workspace.Provider.SynchronizeStreamAsync(workspace.Stream);

        workspace.Provider.CopyHostToDevice(gpuX.DevicePointer, ToWords(floorY + FarOffset, ReciprocalTargetBits / 32),
            ReciprocalTargetBits / 32, workspace.Stream);
        RunReciprocalMega(workspace, gpuB, gpuX, iterations: 0);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await workspace.Provider.SynchronizeStreamAsync(workspace.Stream));
        Assert.Equal(2, status.ReportedFailures);
    }

    // Checked asynchronous reads one after another reuse the source instead of allocating: the source must be freed when
    // its result is consumed.
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void CheckedAsyncReads_RepeatedOnOneWorkspace_DoNotAllocateInSteadyState()
    {
        using var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        var status = workspace.CorrectionStatus ?? throw new InvalidOperationException("The workspace has no status.");
        for (var i = 0; i < 64; i++)
        {
            status.MarkPending();
            SpinUntilConsumed(workspace.Provider.SynchronizeStreamAsync(workspace.Stream));
        }

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/GpuCorrectionStatus.cs:327 `Volatile.Write(ref _busy, 0);` -> removed (every later read takes the allocating path)
        // input: this test
        var allocatedPerWindow = new long[3];
        for (var window = 0; window < allocatedPerWindow.Length; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 256; i++)
            {
                status.MarkPending();
                SpinUntilConsumed(workspace.Provider.SynchronizeStreamAsync(workspace.Stream));
            }

            allocatedPerWindow[window] = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocatedPerWindow[window] == 0)
            {
                return;
            }
        }

        Assert.Fail($"Every window of 256 checked reads allocated (bytes per window): {string.Join(", ", allocatedPerWindow)}.");
    }

    // A read canceled while it waits (the stream is held by a gate) ends without its check; the next read reports the
    // failure.
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public async Task ReciprocalMega_AsyncReadCanceledWhileWaiting_LeavesFailureForNextRead()
    {
        var b = FromWords(RandomWords(seed: 1002, ReciprocalWords));
        var floorY = (BigInteger.One << ReciprocalTargetBits) / b;
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var gpuB = new GpuBigUInt(workspace, ToWords(b, ReciprocalWords));
        using var gpuX = new GpuBigUInt(workspace, ToWords(floorY + FarOffset, ReciprocalTargetBits / 32));
        using var cancellation = new CancellationTokenSource();

        RunReciprocalMega(workspace, gpuB, gpuX, iterations: 0);
        using (var gate = new CudaStreamGate())
        {
            gate.Enqueue(workspace.Stream);
            var read = workspace.Provider.SynchronizeStreamAsync(workspace.Stream, cancellation.Token);
            Assert.False(read.IsCompleted);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await read);
            gate.Open();
        }

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/GpuCorrectionStatus.cs:342 `owner.AbandonRead();` (canceled read) -> removed
        // input: this test
        var exception = Assert.Throws<InvalidOperationException>(() => gpuX.ToBigInteger());
        Assert.Contains("Reciprocal", exception.Message);
    }

    // Two asynchronous reads in flight at once: the second one uses the fallback path. Both copies carry the same
    // failure, which is reported by exactly one of them and not again by a later read.
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public async Task ReciprocalMega_TwoConcurrentAsyncReads_ReportFailureExactlyOnce()
    {
        var b = FromWords(RandomWords(seed: 1003, ReciprocalWords));
        var floorY = (BigInteger.One << ReciprocalTargetBits) / b;
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var gpuB = new GpuBigUInt(workspace, ToWords(b, ReciprocalWords));
        using var gpuX = new GpuBigUInt(workspace, ToWords(floorY + FarOffset, ReciprocalTargetBits / 32));
        var status = workspace.CorrectionStatus ?? throw new InvalidOperationException("The workspace has no status.");

        RunReciprocalMega(workspace, gpuB, gpuX, iterations: 0);
        Task first;
        Task second;
        using (var gate = new CudaStreamGate())
        {
            gate.Enqueue(workspace.Stream);
            first = workspace.Provider.SynchronizeStreamAsync(workspace.Stream).AsTask();
            status.MarkPending();
            second = workspace.Provider.SynchronizeStreamAsync(workspace.Stream).AsTask();
            gate.Open();
        }

        var outcomes = await Task.WhenAll(Outcome(first), Outcome(second));

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/GpuCorrectionStatus.cs:186 `if (count <= reported)` -> `if (count <= 0)`
        // input: this test (both reads and the later read report the same failure)
        Assert.Equal(1, outcomes.Count(failed => failed));
        status.MarkPending();
        workspace.Provider.SynchronizeStream(workspace.Stream);
        Assert.Equal(1, status.ReportedFailures);

        static async Task<bool> Outcome(Task read)
        {
            try
            {
                await read;
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    // Two threads share one workspace and alternate an unconverged ReciprocalMega with a read. Whatever the interleaving,
    // every failure is counted on the device and the reported count reaches the number of failing launches.
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void ReciprocalMega_TwoThreadsOnOneWorkspace_LoseNoFailure()
    {
        const int Iterations = 100;
        const int Threads = 2;
        var b = FromWords(RandomWords(seed: 1004, ReciprocalWords));
        var floorY = (BigInteger.One << ReciprocalTargetBits) / b;
        var farGuess = ToWords(floorY + FarOffset, ReciprocalTargetBits / 32);
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        var status = workspace.CorrectionStatus ?? throw new InvalidOperationException("The workspace has no status.");
        var reports = 0;

        // One handler per thread, warmed up with a converging guess: its twiddle cache is built here, so the loop below
        // launches every failing kernel and the only waits are the reads of the test.
        var handlers = new KernelAdvancedMathHandler[Threads];
        using (var warmB = new GpuBigUInt(workspace, ToWords(b, ReciprocalWords)))
        using (var warmX = new GpuBigUInt(workspace, ToWords(floorY, ReciprocalTargetBits / 32)))
        {
            for (var t = 0; t < Threads; t++)
            {
                handlers[t] = new KernelAdvancedMathHandler();
                RunReciprocalMega(handlers[t], workspace, warmB, warmX);
            }

            Assert.Equal(floorY, warmX.ToBigInteger());
        }

        void Run(KernelAdvancedMathHandler handler)
        {
            using var context = new GpuKernelLoader.CudaContextScope();
            using var gpuB = new GpuBigUInt(workspace, ToWords(b, ReciprocalWords));
            using var gpuX = new GpuBigUInt(workspace, farGuess.Length);
            for (var i = 0; i < Iterations; i++)
            {
                workspace.Provider.CopyHostToDevice(gpuX.DevicePointer, farGuess, farGuess.Length, workspace.Stream);
                RunReciprocalMega(handler, workspace, gpuB, gpuX);
                try
                {
                    _ = gpuX.ToBigInteger();
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref reports);
                }
            }
        }

        var threads = new Thread[Threads];
        for (var t = 0; t < Threads; t++)
        {
            var handler = handlers[t];
            threads[t] = new Thread(() => Run(handler));
            threads[t].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        // A final read takes any count that the last reads raced past.
        status.MarkPending();
        try
        {
            workspace.Provider.SynchronizeStream(workspace.Stream);
        }
        catch (InvalidOperationException)
        {
            reports++;
        }

        // Every failing launch is counted and reported; a read may report several at once.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntDivRemKernels.cu.cs:252 `Gpu.AtomicAdd(ref status[0], 1u);` -> `status[0] = 1u;`
        // input: this test (the count stays at 1)
        Assert.Equal(Threads * Iterations, status.ReportedFailures);
        Assert.InRange(reports, 1, Threads * Iterations);
    }

    private static void RunReciprocalMega(KernelAdvancedMathHandler handler, GpuWorkspace workspace, GpuBigUInt divisor,
        GpuBigUInt guess) =>
        handler.ReciprocalMega(workspace, divisor, guess, marginWords: 0, numBits: ReciprocalWords * 32, marginBits: 0,
            iterations: 0, targetBits: ReciprocalTargetBits);

    // The last pass at the step limit only checks: an x 1001 steps away ends exactly 1000 steps closer, not corrected.
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(-1001)]
    [InlineData(1001)]
    public void ReciprocalMega_GuessOneBeyondStepLimit_MovesExactlyMaxSteps(int offset)
    {
        var b = FromWords(RandomWords(seed: 951, ReciprocalWords));
        var floorY = (BigInteger.One << ReciprocalTargetBits) / b;
        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var gpuB = new GpuBigUInt(workspace, ToWords(b, ReciprocalWords));
        using var gpuX = new GpuBigUInt(workspace, ToWords(floorY + offset, ReciprocalTargetBits / 32));

        RunReciprocalMega(workspace, gpuB, gpuX, iterations: 0);
        Assert.Throws<InvalidOperationException>(() => gpuX.ToBigInteger());

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntDivRemKernels.cu.cs:237 `if (belowZero || step == maxSteps)` -> `if (belowZero)` (moves on the last pass)
        // input: offset -1001
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntDivRemKernels.cu.cs:220 `if (step == maxSteps)` -> `if (step > maxSteps)` (moves on the last pass)
        // input: offset 1001
        Assert.Equal(floorY + Math.Sign(offset), gpuX.ToBigInteger());
    }

    // A slot is returned only after the read that copies into it has finished, even when the status is disposed first.
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public async Task Dispose_WithAsyncReadInFlight_ReleasesSlotAfterTheRead()
    {
        using var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        var status = workspace.CorrectionStatus ?? throw new InvalidOperationException("The workspace has no status.");
        status.MarkPending();
        ValueTask read;
        using (var gate = new CudaStreamGate())
        {
            gate.Enqueue(workspace.Stream);
            read = workspace.Provider.SynchronizeStreamAsync(workspace.Stream);
            status.Dispose();

            // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/GpuCorrectionStatus.cs:260 `Volatile.Read(ref _readsInFlight) != 0 ||` -> removed (released at Dispose)
            // input: this test
            Assert.False(status.SlotReleased);
            gate.Open();
        }

        await read;
        Assert.True(status.SlotReleased);
    }

    // A read abandoned before its wait leaves a copy that may still land, so its slot is discarded rather than reused;
    // only such reads add to the discarded count.
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Dispose_AfterAbandonedRead_DiscardsSlot()
    {
        var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        var status = workspace.CorrectionStatus ?? throw new InvalidOperationException("The workspace has no status.");
        using (var value = new GpuBigUInt(workspace, 4))
        {
            status.MarkPending();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                _ = workspace.Provider.CopyDeviceToHostAsync(new uint[1], value.DevicePointer, 4, workspace.Stream);
            });
        }

        var discardedBefore = GpuCorrectionStatus.DiscardedSlots;
        workspace.Dispose();

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/GpuCorrectionStatus.cs:266 `if (Volatile.Read(ref _slotAbandoned) == 0)` -> `if (true)` (an abandoned slot is reused)
        // input: this test
        Assert.True(status.SlotReleased);
        Assert.Equal(discardedBefore + 1, GpuCorrectionStatus.DiscardedSlots);
    }

    private static void SpinUntilConsumed(ValueTask pending)
    {
        // Polls without Sleep(1), which would round every read up to a timer tick.
        var spinner = new SpinWait();
        while (!pending.IsCompleted)
        {
            spinner.SpinOnce(sleep1Threshold: -1);
        }

        pending.GetAwaiter().GetResult();
    }

    private static void RunReciprocalMega(GpuWorkspace workspace, GpuBigUInt divisor, GpuBigUInt guess, int iterations) =>
        new KernelAdvancedMathHandler().ReciprocalMega(workspace, divisor, guess, marginWords: 0,
            numBits: ReciprocalWords * 32, marginBits: 0, iterations: iterations, targetBits: ReciprocalTargetBits);

    // Test-local integer square root; it checks its own result.
    private static BigInteger FloorSqrt(BigInteger value)
    {
        var x = BigInteger.One << (int)((value.GetBitLength() + 1) / 2);
        while (true)
        {
            var next = (x + value / x) >> 1;
            if (next >= x)
            {
                break;
            }

            x = next;
        }

        Assert.True(x * x <= value && value < (x + 1) * (x + 1), "test-local FloorSqrt is wrong");
        return x;
    }

    private static BigInteger FromWords(ReadOnlySpan<uint> words) =>
        new(MemoryMarshal.AsBytes(words), isUnsigned: true, isBigEndian: false);

    private static uint[] ToWords(BigInteger value, int numWords)
    {
        var words = new uint[numWords];
        if (value.IsZero)
        {
            return words;
        }

        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        if (bytes.Length > numWords * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"The test input needs more than {numWords} words.");
        }

        Buffer.BlockCopy(bytes, 0, words, 0, bytes.Length);
        return words;
    }

    // Random words with the top bit of the top word set (a normalized operand).
    private static uint[] RandomWords(int seed, int count)
    {
        var rnd = new Random(seed);
        var words = new uint[count];
        for (var i = 0; i < count; i++)
        {
            words[i] = (uint)rnd.NextInt64(0, 1L << 32);
        }

        words[^1] |= 0x80000000u;
        return words;
    }
}
