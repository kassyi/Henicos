using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

/// <summary>
/// Verifies the block and grid size that <c>InverseSqrtMega</c> and <c>ReciprocalMega</c> launch with, by default
/// and under <c>CandidateBlockSizesOverride</c>, and that the result still matches the CPU oracle.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class MegaKernelLaunchShapeTests
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void InverseSqrtMega_ThreeSmsDefaultRule_LaunchesWithRuleBlockAndGridDim()
    {
        CudaCapability.EnsureGpuAvailable();
        AssertInverseSqrtMegaLaunchShape(
            numWords: 60,
            marginWords: 6,
            smCount: 3,
            candidateOverride: [],
            expectedShape: new CooperativeMegaKernelLauncher.LaunchShape(256u, 3u, 8704u));
    }

    // InverseSqrtMega must launch with the rule-clamped GridDim (up to DeviceSmCount), not a single block;
    // otherwise numerical output still matches via grid-stride loops while multi-SM parallelism is lost.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
    //         CooperativeMegaKernelLauncher.cs:75
    //         `gridDimX: shape.GridDim` -> `gridDimX: 1u`
    // input: numWords = 60, marginWords = 6, smCount = 3, override [256] (or [768] / [512])
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1024, 1u)]
    [InlineData(768, 2u)]
    [InlineData(512, 2u)]
    [InlineData(256, 3u)]
    public void InverseSqrtMega_ThreeSmsWithCandidateOverride_LaunchesWithExpectedBlockAndGridDim(
        int candidateBlockSize, uint expectedGridDim)
    {
        CudaCapability.EnsureGpuAvailable();
        AssertInverseSqrtMegaLaunchShape(
            numWords: 60,
            marginWords: 6,
            smCount: 3,
            candidateOverride: [candidateBlockSize],
            expectedShape: new CooperativeMegaKernelLauncher.LaunchShape(
                (uint)candidateBlockSize, expectedGridDim, 8704u));
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ReciprocalMega_ThreeSmsDefaultRule_LaunchesWithRuleBlockAndGridDim()
    {
        CudaCapability.EnsureGpuAvailable();
        AssertReciprocalMegaLaunchShape(
            numWords: 58,
            targetBits: 3200,
            smCount: 3,
            candidateOverride: [],
            expectedShape: new CooperativeMegaKernelLauncher.LaunchShape(256u, 3u, 8704u));
    }

    // ReciprocalMega must launch with the rule-clamped GridDim (up to DeviceSmCount), not a single block;
    // otherwise numerical output still matches via grid-stride loops while multi-SM parallelism is lost.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
    //         CooperativeMegaKernelLauncher.cs:120
    //         `gridDimX: shape.GridDim` -> `gridDimX: 1u`
    // input: numWords = 58, targetBits = 3200, smCount = 3, override [256] (or [768] / [512])
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1024, 1u)]
    [InlineData(768, 2u)]
    [InlineData(512, 2u)]
    [InlineData(256, 3u)]
    public void ReciprocalMega_ThreeSmsWithCandidateOverride_LaunchesWithExpectedBlockAndGridDim(
        int candidateBlockSize, uint expectedGridDim)
    {
        CudaCapability.EnsureGpuAvailable();
        AssertReciprocalMegaLaunchShape(
            numWords: 58,
            targetBits: 3200,
            smCount: 3,
            candidateOverride: [candidateBlockSize],
            expectedShape: new CooperativeMegaKernelLauncher.LaunchShape(
                (uint)candidateBlockSize, expectedGridDim, 8704u));
    }

    private static void AssertInverseSqrtMegaLaunchShape(
        int numWords,
        int marginWords,
        int smCount,
        int[] candidateOverride,
        CooperativeMegaKernelLauncher.LaunchShape expectedShape)
    {
        var operandWords = MegaKernelBlockSizeInvarianceTests.CreateNormalizedWords(seed: 7100 + numWords, numWords);
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

        try
        {
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride =
                candidateOverride.Length == 0 ? null : candidateOverride;
            using var gpuWorkspace = new GpuWorkspace(resultWords * 8, GpuExecutionEngine.GSharpAot)
            {
                DeviceSmCount = smCount,
            };
            using var gpuOperand = new GpuBigUInt(gpuWorkspace, operandWords);
            using var gpuResult = new GpuBigUInt(gpuWorkspace, resultWords);
            GpuBigIntDivRem.InverseSqrt(gpuOperand, gpuResult, marginWords);
            var gpuWords = gpuResult.ToHostArray();

            Assert.Equal(expectedShape, gpuWorkspace.LastInverseSqrtMegaLaunchShape);
            Assert.Equal(cpuWords, gpuWords);
        }
        finally
        {
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = null;
        }
    }

    private static void AssertReciprocalMegaLaunchShape(
        int numWords,
        int targetBits,
        int smCount,
        int[] candidateOverride,
        CooperativeMegaKernelLauncher.LaunchShape expectedShape)
    {
        var divisorWords = MegaKernelBlockSizeInvarianceTests.CreateNormalizedWords(seed: 7300 + numWords, numWords);
        var resultWords = (targetBits + 31) / 32;

        uint[] cpuWords;
        using (CpuFallbackScope.Enter())
        {
            using var cpuWorkspace = new GpuWorkspace(resultWords * 4, GpuExecutionEngine.GSharpAot);
            using var cpuDivisor = new GpuBigUInt(cpuWorkspace, divisorWords);
            using var cpuResult = new GpuBigUInt(cpuWorkspace, resultWords);
            GpuBigIntDivRem.Reciprocal(cpuDivisor, cpuResult, targetBits);
            cpuWords = cpuResult.ToHostArray();
        }

        try
        {
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride =
                candidateOverride.Length == 0 ? null : candidateOverride;
            using var gpuWorkspace = new GpuWorkspace(resultWords * 4, GpuExecutionEngine.GSharpAot)
            {
                DeviceSmCount = smCount,
            };
            using var gpuDivisor = new GpuBigUInt(gpuWorkspace, divisorWords);
            using var gpuResult = new GpuBigUInt(gpuWorkspace, resultWords);
            GpuBigIntDivRem.Reciprocal(gpuDivisor, gpuResult, targetBits);
            var gpuWords = gpuResult.ToHostArray();

            Assert.Equal(expectedShape, gpuWorkspace.LastReciprocalMegaLaunchShape);
            Assert.Equal(cpuWords, gpuWords);
        }
        finally
        {
            CooperativeMegaKernelLauncher.CandidateBlockSizesOverride = null;
        }
    }
}
