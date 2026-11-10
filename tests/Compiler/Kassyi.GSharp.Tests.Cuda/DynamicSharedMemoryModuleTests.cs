using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// Dynamic shared memory above the default 48 KB per block for a kernel of an external module launched through
/// <c>[CudaImport]</c> (#820): the launcher opts the function in the same way as a <c>[GpuKernel]</c> launch (#545),
/// and a request above the device's opt-in limit is an <see cref="ArgumentOutOfRangeException"/> before the launch.
/// </summary>
[Collection(TestCollections.DeviceExclusive)]
public class DynamicSharedMemoryModuleTests : TranspilerTestBase
{
    private const int BlockSize = 256;
    private const uint DefaultLimitBytes = 48 * 1024;

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

    // The launcher resolves the same CUfunction through the same cache
    private static IntPtr GetFunction()
    {
        var fatbin = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, DynamicSharedMemoryModuleInvoker.FatbinName));
        return GpuKernelLoader.LoadAotKernel(fatbin, DynamicSharedMemoryModuleInvoker.EntryPoint);
    }

    private static int FunctionAttribute(IntPtr hfunc, CuFunctionAttribute attribute)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuFuncGetAttribute(out var value, attribute, hfunc));
        return value;
    }

    private static int DeviceAttribute(CudaDriverApi.CuDeviceAttribute attribute)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuCtxGetDevice(out var device));
        CudaException.ThrowIfError(CudaDriverApi.cuDeviceGetAttribute(out var value, attribute, device));
        return value;
    }

    private static int OptInLimit => DeviceAttribute(CudaDriverApi.CuDeviceAttribute.MaxSharedMemoryPerBlockOptin);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Launch_ModuleKernelDynamicSharedMemoryAt64Kb_UsesWholeRegionAndOptsInOnce()
    {
        Skip.If(OptInLimit < 64 * 1024, "The GPU allows less than 64 KB of shared memory per block.");
        const uint bytes = 64 * 1024;
        const int n = (int)(bytes / 4);
        var output = new uint[BlockSize];

        DynamicSharedMemoryModuleInvoker.LaunchFillAndSum(output, n, BlockSize, bytes);

        Assert.Equal(ExpectedSums(n), output);
        var hfunc = GetFunction();
        Assert.Equal(1, DynamicSharedMemoryOptIn.GetOptInCount(hfunc));
        Assert.True(FunctionAttribute(hfunc, CuFunctionAttribute.MaxDynamicSharedSizeBytes) > DefaultLimitBytes);

        // A second launch finds the function opted in and makes no further cuFuncSetAttribute call
        Array.Clear(output);
        DynamicSharedMemoryModuleInvoker.LaunchFillAndSum(output, n, BlockSize, bytes);

        Assert.Equal(ExpectedSums(n), output);
        Assert.Equal(1, DynamicSharedMemoryOptIn.GetOptInCount(hfunc));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Launch_ModuleKernelAboveOptInLimit_ThrowsBeforeLaunching()
    {
        var limit = OptInLimit;
        var staticBytes = FunctionAttribute(GetFunction(), CuFunctionAttribute.SharedSizeBytes);
        var bytes = (uint)(limit - staticBytes + 1);
        var output = new uint[BlockSize];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DynamicSharedMemoryModuleInvoker.LaunchFillAndSum(output, (int)(bytes / 4), BlockSize, bytes));

        Assert.Equal("sharedMemBytes", ex.ParamName);
        Assert.StartsWith(
            $"G#: sharedMemBytes {bytes} plus {staticBytes} bytes of static shared memory exceeds the {limit} bytes of " +
            "shared memory per block of this GPU (sm_", ex.Message);
        Assert.Contains("(DynamicSharedMemoryModuleInvoker.LaunchFillAndSum).", ex.Message);

        // Nothing was launched or copied back. This cannot tell whether the check ran before the device allocations
        // (they are disposed either way); NativeApiGeneratorTests asserts that placement in the generated source.
        Assert.All(output, static value => Assert.Equal(0u, value));
    }

    // The [CudaImport] launcher rejects a launch beyond the limits of every GPU with the same exception as the
    // launcher of a [GpuKernel] method (#799), not the driver's CUDA_ERROR_INVALID_VALUE
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0u, "is 0")]
    [InlineData(1025u, "at most 1024 threads in x")]
    [InlineData(2048u, "at most 1024 threads in x")]
    public void Launch_ModuleKernelBlockDimXBeyondLimits_ThrowsBeforeLaunching(uint blockDimX, string limit)
    {
        var output = new uint[BlockSize];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DynamicSharedMemoryModuleInvoker.LaunchFillAndSum(output, BlockSize, blockDimX, 1024));

        Assert.Equal("blockDimX", ex.ParamName);
        Assert.Contains(limit, ex.Message);
        Assert.Contains("(DynamicSharedMemoryModuleInvoker.LaunchFillAndSum).", ex.Message);
        Assert.All(output, static value => Assert.Equal(0u, value));
    }

    // The largest block runs: the check does not reject a launch that every GPU accepts
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Launch_ModuleKernelBlockDimXAt1024Limit_ProducesExpectedSums()
    {
        const int n = 4096;
        var output = new uint[1024];

        DynamicSharedMemoryModuleInvoker.LaunchFillAndSum(output, n, 1024, n * 4);

        for (var t = 0; t < output.Length; t++)
        {
            uint sum = 0;
            for (var j = t; j < n; j += output.Length)
            {
                sum += ElementValue(n - 1 - j);
            }

            Assert.Equal(sum, output[t]);
        }
    }
}
