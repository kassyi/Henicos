#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
#if GSHARP_CUDA
using Kassyi.GSharp.Generated;
using Kassyi.GSharp.Runtime.Interop;
#endif
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for kernels declared with <c>[GpuKernel(AllowOversizedSharedMemory = true)]</c> on default target
/// architectures (<c>sm_86</c> = 101376 bytes).
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class OversizedSharedMemoryDifferentialTests : TranspilerTestBase
{
    private const int BlockSize = 256;
    private const uint TargetOptInBytes = 101376;
    private const string FillAndSumKernelName =
        "OversizedSharedMemoryKernels.FillAndSumOversizedKernel";
    private const string WriteThreadRankKernelName =
        "OversizedSharedMemoryKernels.WriteThreadRankOversizedKernel";

#if GSHARP_CUDA
    private const string KernelPrefix =
        "__gsharp_k_Kassyi__GSharp__Tests__Shared__Differential__OversizedSharedMemoryKernels__";

    private static int GetOptInLimit()
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuCtxGetDevice(out var device));
        CudaException.ThrowIfError(CudaDriverApi.cuDeviceGetAttribute(out var value,
            CudaDriverApi.CuDeviceAttribute.MaxSharedMemoryPerBlockOptin, device));
        return value;
    }

    private static int StaticBytes(string kernel)
    {
        var module = GSharpModuleContext_Kassyi_x00002eGSharp_x00002eTests_x00002eCuda.GetOrLoadModule();
        var hfunc = GpuKernelLoader.GetFunction(module, KernelPrefix + kernel);
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuFuncGetAttribute(out var value,
            CuFunctionAttribute.SharedSizeBytes, hfunc));
        return value;
    }
#endif

    private static void SkipIfDeviceLimitDiffersFromTarget()
    {
#if GSHARP_CUDA
        var optIn = GetOptInLimit();
        var staticBytes = StaticBytes(nameof(OversizedSharedMemoryKernels.FillAndSumOversizedKernel));
        Skip.If(optIn != (int)TargetOptInBytes || staticBytes != 0,
            $"The GPU path has MaxSharedMemoryPerBlockOptin = {optIn} and SharedSizeBytes = {staticBytes} " +
            $"(target limit is {TargetOptInBytes} with 0 static bytes).");
#endif
    }

    private static uint ElementValue(int i) => (uint)i * 2654435761u + 12345u;

    private static uint[] ExpectedSums(int n)
    {
        var expected = new uint[BlockSize];
        for (var t = 0; t < BlockSize; t++)
        {
            uint sum = 0;
            for (var j = t; j < n; j += BlockSize)
            {
                sum += ElementValue(n - 1 - j);
            }

            expected[t] = sum;
        }

        return expected;
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DynamicSharedMemoryAtTheTargetLimit_RunsWithTheWholeRegion()
    {
        SkipIfDeviceLimitDiffersFromTarget();

        const int n = (int)(TargetOptInBytes / sizeof(uint));
        var expected = ExpectedSums(n);
        foreach (var forceManaged in Paths())
        {
            var output = new uint[BlockSize];
            Run(forceManaged, () => OversizedSharedMemoryKernels.LaunchFillAndSumOversizedKernel(
                output, n, blockDimX: BlockSize, sharedMemBytes: TargetOptInBytes));
            Assert.Equal(expected, output);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void DynamicSharedMemoryAboveTheTargetLimit_ThrowsBeforeAnyThreadRuns()
    {
        SkipIfDeviceLimitDiffersFromTarget();

        const uint bytes = TargetOptInBytes + 1;
        const int n = (int)(bytes / sizeof(uint));
        foreach (var forceManaged in Paths())
        {
            var output = new uint[BlockSize];
            var exception = Run(forceManaged, () => Assert.Throws<ArgumentOutOfRangeException>(() =>
                OversizedSharedMemoryKernels.LaunchFillAndSumOversizedKernel(
                    output, n, blockDimX: BlockSize, sharedMemBytes: bytes)));

            Assert.Equal("sharedMemBytes", exception.ParamName);
            Assert.Contains(
                "sharedMemBytes 101377 plus 0 bytes of static shared memory exceeds the 101376 bytes of shared " +
                "memory per block of",
                exception.Message);
            Assert.Contains("the GPU rejects the launch with CUDA_ERROR_INVALID_VALUE", exception.Message);
            Assert.Contains(FillAndSumKernelName, exception.Message);
            Assert.Contains("sm_86", exception.Message);
            Assert.All(output, static value => Assert.Equal(0u, value));
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CSharpExecutionAboveTheTargetLimit_Throws()
    {
        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            var output = new int[16];
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                OversizedSharedMemoryKernels.LaunchWriteThreadRankOversizedKernel(
                    output, blockDimX: 16, sharedMemBytes: TargetOptInBytes + 1));

            Assert.Equal("sharedMemBytes", exception.ParamName);
            Assert.StartsWith(
                "G#: sharedMemBytes 101377 plus 0 bytes of static shared memory exceeds the 101376 bytes of shared " +
                "memory per block of the target GPU sm_86 (GSharpTargetArchitectures); the GPU rejects the launch " +
                $"with CUDA_ERROR_INVALID_VALUE ({WriteThreadRankKernelName}).",
                exception.Message);
            Assert.All(output, static value => Assert.Equal(0, value));

            OversizedSharedMemoryKernels.LaunchWriteThreadRankOversizedKernel(
                output, blockDimX: 16, sharedMemBytes: TargetOptInBytes);
            for (var i = 0; i < output.Length; i++)
            {
                Assert.Equal(i + 1, output[i]);
            }
        }
    }

    private bool[] Paths() => IsCudaDialect ? [false] : [false, true];

    private static void Run(bool forceManaged, Action launch) => Run(forceManaged, () =>
    {
        launch();
        return 0;
    });

    private static T Run<T>(bool forceManaged, Func<T> launch)
    {
        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            return launch();
        }
    }
}
