using System.Runtime.InteropServices;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

/// <summary>
/// Verifies that cooperative mega-kernel results for <c>InverseSqrtMega</c>, <c>Sqrt</c>, and <c>ReciprocalMega</c>
/// are bit-for-bit identical across block sizes <c>768</c>, <c>512</c>, and <c>256</c> (including inputs crossing the
/// 1024-word carry-chunk boundary), honor <c>CandidateBlockSizesOverride</c> with <c>AsyncLocal</c> isolation, and
/// match the CPU oracle.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class MegaKernelBlockSizeInvarianceTests(ITestOutputHelper output)
{
    private const string InverseSqrtKernelName = "GpuBigIntSqrtKernels.InverseSqrtNewtonRaphsonMegaKernel";
    private static readonly int[] CandidateBlockSizes = [768, 512, 256];

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(16, 4)]
    [InlineData(1024, 16)]
    [InlineData(1025, 16)]
    public void InverseSqrtMega_AcrossCandidateBlockSizes_MatchesCpuOracleBitForBit(int numWords, int marginWords)
    {
        CudaCapability.EnsureGpuAvailable();

        var operandWords = CreateNormalizedWords(seed: 7100 + numWords, numWords);
        var operand = FromWords(operandWords);
        var precisionBits = (numWords + marginWords) * 32;
        var resultWords = numWords + marginWords;

        uint[] cpuWords;
        using (CpuFallbackScope.Enter())
        {
            using var cpuWorkspace = new GpuWorkspace(resultWords * 8, GpuExecutionEngine.GSharpAot);
            using var cpuOperand = new GpuBigUInt(cpuWorkspace, operandWords);
            using var cpuResult = new GpuBigUInt(cpuWorkspace, resultWords);
            GpuBigIntDivRem.InverseSqrt(cpuOperand, cpuResult, marginWords);
            cpuWords = cpuResult.ToHostArray();
        }

        // Algebraic defining inequality: floor(2^B / sqrt(a)) satisfies a * x^2 <= 2^(2B) < a * (x + 1)^2.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.Sqrt.cs:46
        //         `ComputeInitialGuess(targetOperand, resultBuffer, targetBits, isSquareRoot: true);` -> removed
        // input: (16, 4)
        var cpuValue = FromWords(cpuWords);
        var twoTo2B = BigInteger.One << (2 * precisionBits);
        Assert.True(operand * cpuValue * cpuValue <= twoTo2B, "CPU inverse sqrt exceeds 2^B / sqrt(a)");
        Assert.True(twoTo2B < operand * (cpuValue + 1) * (cpuValue + 1), "CPU inverse sqrt below floor");

        try
        {
            foreach (var blockSize in CandidateBlockSizes)
            {
                CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = [blockSize];
                using var gpuWorkspace = new GpuWorkspace(resultWords * 8, GpuExecutionEngine.GSharpAot);
                using var gpuOperand = new GpuBigUInt(gpuWorkspace, operandWords);
                using var gpuResult = new GpuBigUInt(gpuWorkspace, resultWords);
                GpuBigIntDivRem.InverseSqrt(gpuOperand, gpuResult, marginWords);
                var gpuWords = gpuResult.ToHostArray();

                // Active CandidateBlockSizesOverride must govern the block size chosen by ResolveCooperativeBlockDim.
                // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
                //         CooperativeMegaKernelLauncher.cs:233
                //         `var overrideState = s_candidateBlockSizesOverride.Value;` -> `null`
                // input: (16, 4) with blockSize = 512
                Assert.True(CooperativeMegaKernelLauncher.TryGetCachedOverrideBlockSize(
                    gpuWorkspace,
                    static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
                    4 * numWords + 3 * marginWords,
                    out var chosenBlockSize));
                Assert.Equal(blockSize, chosenBlockSize);

                // Kernel carry chunking follows BlockDimension.X, so every candidate block size must agree bit-for-bit
                // with the CPU oracle across single-block and multi-block (>1024 words) inputs.
                // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
                //         CooperativeMegaKernelLauncher.cs:448 `ChunkSize = carryChunkSize;` -> `ChunkSize = 1024;`
                // input: (1024, 16) with blockSize = 768
                Assert.Equal(cpuWords, gpuWords);
            }
        }
        finally
        {
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = null;
        }
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(4)]
    [InlineData(33)]
    [InlineData(512)]
    [InlineData(2050)]
    public void Sqrt_AcrossCandidateBlockSizes_MatchesCpuOracleBitForBit(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        var operandWords = CreateNormalizedWords(seed: 7200 + numWords, numWords);
        var operand = FromWords(operandWords);

        uint[] cpuWords;
        using (CpuFallbackScope.Enter())
        {
            using var cpuWorkspace = new GpuWorkspace(numWords * 6, GpuExecutionEngine.GSharpAot);
            using var cpuOperand = new GpuBigUInt(cpuWorkspace, operandWords);
            using var cpuResult = new GpuBigUInt(cpuWorkspace, numWords);
            GpuBigIntDivRem.Sqrt(cpuOperand, cpuResult);
            cpuWords = cpuResult.ToHostArray();
        }

        // Algebraic defining inequality: floor(sqrt(a)) satisfies r^2 <= a < (r + 1)^2.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.Sqrt.cs:204
        //         `var marginWords = 16;` -> `var marginWords = 0;`
        // input: 4
        var cpuRoot = FromWords(cpuWords);
        Assert.True(cpuRoot * cpuRoot <= operand, "CPU sqrt exceeds exact root");
        Assert.True(operand < (cpuRoot + 1) * (cpuRoot + 1), "CPU sqrt below floor");

        try
        {
            foreach (var blockSize in CandidateBlockSizes)
            {
                CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = [blockSize];
                using var gpuWorkspace = new GpuWorkspace(numWords * 6, GpuExecutionEngine.GSharpAot);
                using var gpuOperand = new GpuBigUInt(gpuWorkspace, operandWords);
                using var gpuResult = new GpuBigUInt(gpuWorkspace, numWords);
                GpuBigIntDivRem.Sqrt(gpuOperand, gpuResult);
                var gpuWords = gpuResult.ToHostArray();

                // ComputeInitialSqrtGuess launches InverseSqrtMega with marginWords = 16, so the active override must
                // match blockSize for that kernel invocation.
                // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
                //         CooperativeMegaKernelLauncher.cs:233
                //         `var overrideState = s_candidateBlockSizesOverride.Value;` -> `null`
                // input: 4 with blockSize = 512
                Assert.True(CooperativeMegaKernelLauncher.TryGetCachedOverrideBlockSize(
                    gpuWorkspace,
                    static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
                    4 * numWords + 3 * 16,
                    out var chosenBlockSize));
                Assert.Equal(blockSize, chosenBlockSize);

                // Integer sqrt must stay bit-for-bit invariant across candidate block sizes and match CPU oracle.
                // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
                //         CooperativeMegaKernelLauncher.cs:448 `ChunkSize = carryChunkSize;` -> `ChunkSize = 1024;`
                // input: 512 with blockSize = 768
                Assert.Equal(cpuWords, gpuWords);
            }
        }
        finally
        {
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = null;
        }
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(16, 1024)]
    [InlineData(1024, 2 * 1024 * 32 + 512)]
    [InlineData(1025, 2 * 1025 * 32 + 512)]
    public void ReciprocalMega_AcrossCandidateBlockSizes_MatchesCpuOracleBitForBit(int numWords, int targetBits)
    {
        CudaCapability.EnsureGpuAvailable();

        var divisorWords = CreateNormalizedWords(seed: 7300 + numWords, numWords);
        var divisor = FromWords(divisorWords);
        var resultWords = targetBits / 32 + 1;

        uint[] cpuWords;
        using (CpuFallbackScope.Enter())
        {
            using var cpuWorkspace = new GpuWorkspace(resultWords * 4, GpuExecutionEngine.GSharpAot);
            using var cpuDivisor = new GpuBigUInt(cpuWorkspace, divisorWords);
            using var cpuResult = new GpuBigUInt(cpuWorkspace, resultWords);
            GpuBigIntDivRem.Reciprocal(cpuDivisor, cpuResult, targetBits);
            cpuWords = cpuResult.ToHostArray();
        }

        // Algebraic defining inequality: floor(2^T / b) satisfies b * x <= 2^T < b * (x + 1).
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:88
        //         `var iterations = CalculateNewtonRaphsonIterationCount(currentBits, targetBits);` -> `1`
        // input: (16, 1024)
        var cpuReciprocal = FromWords(cpuWords);
        var twoToT = BigInteger.One << targetBits;
        Assert.True(divisor * cpuReciprocal <= twoToT, "CPU reciprocal exceeds 2^T / b");
        Assert.True(twoToT < divisor * (cpuReciprocal + 1), "CPU reciprocal below floor");

        try
        {
            foreach (var blockSize in CandidateBlockSizes)
            {
                CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = [blockSize];
                using var gpuWorkspace = new GpuWorkspace(resultWords * 4, GpuExecutionEngine.GSharpAot);
                using var gpuDivisor = new GpuBigUInt(gpuWorkspace, divisorWords);
                using var gpuResult = new GpuBigUInt(gpuWorkspace, resultWords);
                GpuBigIntDivRem.Reciprocal(gpuDivisor, gpuResult, targetBits);
                var gpuWords = gpuResult.ToHostArray();

                // Active CandidateBlockSizesOverride must govern the block size chosen for ReciprocalMega.
                // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
                //         CooperativeMegaKernelLauncher.cs:233
                //         `var overrideState = s_candidateBlockSizesOverride.Value;` -> `null`
                // input: (16, 1024) with blockSize = 512
                Assert.True(CooperativeMegaKernelLauncher.TryGetCachedOverrideBlockSize(
                    gpuWorkspace,
                    static () => GpuBigIntDivRemKernels.ResolveReciprocalNewtonRaphsonMegaKernel(),
                    numWords + 2 * ((targetBits + 31) / 32),
                    out var chosenBlockSize));
                Assert.Equal(blockSize, chosenBlockSize);

                // Kernel carry chunking follows BlockDimension.X, so ReciprocalMega must match CPU bit-for-bit.
                // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
                //         CooperativeMegaKernelLauncher.cs:448 `ChunkSize = carryChunkSize;` -> `ChunkSize = 1024;`
                // input: (1024, 66048) with blockSize = 768
                Assert.Equal(cpuWords, gpuWords);
            }
        }
        finally
        {
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = null;
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void DefaultBlockSize_AfterMegaKernelLaunches_CachesSupportedBlockSize()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        var words = CreateNormalizedWords(seed: 7400, numWords: 16);
        using var operand = new GpuBigUInt(workspace, words);
        using var invSqrt = new GpuBigUInt(workspace, 20);
        using var reciprocal = new GpuBigUInt(workspace, 33);

        GpuBigIntDivRem.InverseSqrt(operand, invSqrt, marginWords: 4);
        GpuBigIntDivRem.Reciprocal(operand, reciprocal, targetBits: 1024);

        using var scope = new GpuKernelLoader.CudaContextScope(workspace.IsolatedContext.Handle);
        var sqrtFunc = GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel();
        var recipFunc = GpuBigIntDivRemKernels.ResolveReciprocalNewtonRaphsonMegaKernel();

        var sqrtSmem = CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(
            CooperativeMegaKernelLauncher.TransformLengthFor(4 * 16 + 3 * 4));
        var recipSmem = CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(
            CooperativeMegaKernelLauncher.TransformLengthFor(16 + 2 * 32));

        // Default launches without override must populate the default occupancy cache with a valid candidate.
        // mutant: src/Runtime/Kassyi.GSharp.Runtime/Aot/CooperativeLaunchOccupancy.cs:46
        //         `s_blockSizes.TryAdd(key, chosen);` -> removed
        // input: 16-word InverseSqrt + Reciprocal on device 0
        Assert.True(CooperativeMegaKernelLauncher.TryGetCachedDefaultBlockSize(
            sqrtFunc, workspace.DeviceOrdinal, sqrtSmem, out var sqrtBlockSize));
        Assert.True(CooperativeMegaKernelLauncher.TryGetCachedDefaultBlockSize(
            recipFunc, workspace.DeviceOrdinal, recipSmem, out var recipBlockSize));
        output.WriteLine(
            $"Chosen cooperative block sizes on device {workspace.DeviceOrdinal}: " +
            $"InverseSqrt={sqrtBlockSize}, Reciprocal={recipBlockSize}");
        Assert.Contains(sqrtBlockSize, new[] { 1024, 768, 512, 256 });
        Assert.Contains(recipBlockSize, new[] { 1024, 768, 512, 256 });
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public async Task CandidateBlockSizesOverride_ConcurrentAsyncFlows_IsolatesOverrideCachePerAsyncFlow()
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        const int RequiredLen = 76;
        var sharedMemBytes = CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(
            CooperativeMegaKernelLauncher.TransformLengthFor(RequiredLen));

        try
        {
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = [512];
            using var flowACaptured = new ManualResetEventSlim(false);
            using var flowBResolved = new ManualResetEventSlim(false);

            var flowATask = Task.Run(() =>
            {
                flowACaptured.Set();
                flowBResolved.Wait();

                var resolvedA = CooperativeMegaKernelLauncher.ResolveCooperativeBlockDim(
                    workspace,
                    static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
                    InverseSqrtKernelName,
                    sharedMemBytes);
                var hasCachedA = CooperativeMegaKernelLauncher.TryGetCachedOverrideBlockSize(
                    workspace,
                    static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
                    RequiredLen,
                    out var cachedA);
                return (resolvedA, hasCachedA, cachedA);
            });

            flowACaptured.Wait();
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = [768];
            var resolvedBFirst = CooperativeMegaKernelLauncher.ResolveCooperativeBlockDim(
                workspace,
                static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
                InverseSqrtKernelName,
                sharedMemBytes);
            var hasCachedB = CooperativeMegaKernelLauncher.TryGetCachedOverrideBlockSize(
                workspace,
                static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
                RequiredLen,
                out var cachedB);
            flowBResolved.Set();

            var (resolvedA, hasCachedA, cachedA) = await flowATask;
            var resolvedBSecond = CooperativeMegaKernelLauncher.ResolveCooperativeBlockDim(
                workspace,
                static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
                InverseSqrtKernelName,
                sharedMemBytes);

            // Each async flow must keep its own override cache so Flow B ([768]) cannot poison Flow A ([512]), and
            // Flow A cannot clear or overwrite Flow B's cached choice.
            // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
            //         CooperativeMegaKernelLauncher.cs:23 `new OverrideState(value)` -> shared static dictionary
            // input: Flow A [512] overlapping Flow B [768]
            Assert.Equal(768u, resolvedBFirst);
            Assert.True(hasCachedB);
            Assert.Equal(768, cachedB);
            Assert.Equal(512u, resolvedA);
            Assert.True(hasCachedA);
            Assert.Equal(512, cachedA);
            Assert.Equal(768u, resolvedBSecond);
        }
        finally
        {
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = null;
        }
    }

    private static uint[] CreateNormalizedWords(int seed, int numWords)
    {
        var rng = new Random(seed);
        var words = new uint[numWords];
        for (var i = 0; i < numWords; i++)
        {
            words[i] = (uint)rng.Next() | ((uint)rng.Next(0, 2) << 31);
        }

        words[numWords - 1] |= 0x80000000u;
        return words;
    }

    private static BigInteger FromWords(ReadOnlySpan<uint> words) =>
        new(MemoryMarshal.AsBytes(words), isUnsigned: true, isBigEndian: false);
}
