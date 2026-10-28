using Kassyi.GSharp.Generated;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// Dynamic shared memory above the default 48 KB per block on the CUDA path (#545): the launch's <c>sharedMemBytes</c>
/// opts the function in (<c>cuFuncSetAttribute(MAX_DYNAMIC_SHARED_SIZE_BYTES)</c>) once, and a request above the
/// device's opt-in limit (static plus dynamic) is an <see cref="ArgumentOutOfRangeException"/> before the launch.
/// </summary>
/// <remarks>
/// Properties: (1) every request with static + dynamic &lt;= the opt-in limit launches and the kernel sees the whole
/// region (the sums match the host); (2) every request above it throws, naming the kernel, the bytes and the limit,
/// and launches nothing; (3) the opt-in happens at most once per function and only for a request above the default.
/// The limits are read from the device, so the boundaries hold on any GPU.
/// </remarks>
[Collection(TestCollections.DeviceExclusive)]
public class DynamicSharedMemoryLimitTests : TranspilerTestBase
{
    private const int BlockSize = 256;
    private const uint DefaultLimitBytes = 48 * 1024;
    private const int StaticElements = 1024;
    private const string KernelPrefix = "__gsharp_k_Kassyi__GSharp__Tests__Cuda__DynamicSharedMemoryLimitKernels__";

    private delegate void Launcher(uint[] output, int n, uint sharedMemBytes);

    private static readonly Launcher s_fillAndSum = static (output, n, bytes) =>
        DynamicSharedMemoryLimitKernels.LaunchFillAndSumKernel(output, n, blockDimX: BlockSize, sharedMemBytes: bytes);

    private static readonly Launcher s_staticPlusDynamic = static (output, n, bytes) =>
        DynamicSharedMemoryLimitKernels.LaunchStaticPlusDynamicKernel(output, n, blockDimX: BlockSize,
            sharedMemBytes: bytes);

    private static uint ElementValue(int i) => (uint)i * 2654435761u + 12345u;

    // The sums of FillAndSumKernel (staticElements = 0) or StaticPlusDynamicKernel for n dynamic elements
    private static uint[] ExpectedSums(int n, int staticElements)
    {
        var expected = new uint[BlockSize];
        for (var t = 0; t < BlockSize; t++)
        {
            uint sum = 0;
            for (var j = t; j < staticElements; j += BlockSize)
            {
                sum += ElementValue(staticElements - 1 - j);
            }

            for (var j = t; j < n; j += BlockSize)
            {
                sum += ElementValue(n - 1 - j);
            }

            expected[t] = sum;
        }

        return expected;
    }

    private static IntPtr GetFunction(string kernel)
    {
        var module = GSharpModuleContext_Kassyi_GSharp_Tests_Cuda.GetOrLoadModule();
        return GpuKernelLoader.GetFunction(module, KernelPrefix + kernel);
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

    private static int StaticBytes(string kernel) =>
        FunctionAttribute(GetFunction(kernel), CuFunctionAttribute.SharedSizeBytes);

    private static void AssertLaunchSums(Launcher launch, uint bytes, int staticElements)
    {
        var n = (int)(bytes / 4);
        var output = new uint[BlockSize];
        launch(output, n, bytes);
        Assert.Equal(ExpectedSums(n, staticElements), output);
    }

    private static void SkipIfOptInNotAboveDefault() =>
        Skip.If(OptInLimit <= DefaultLimitBytes, "The GPU allows no shared memory above 48 KB per block.");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DynamicSharedMemory48KB_LaunchesWithoutOptIn()
    {
        var bytes = DefaultLimitBytes - (uint)StaticBytes(nameof(DynamicSharedMemoryLimitKernels.DefaultLimitKernel));
        var n = (int)(bytes / 4);
        var output = new uint[BlockSize];
        DynamicSharedMemoryLimitKernels.LaunchDefaultLimitKernel(output, n, blockDimX: BlockSize, sharedMemBytes: bytes);

        Assert.Equal(ExpectedSums(n, 0), output);
        var hfunc = GetFunction(nameof(DynamicSharedMemoryLimitKernels.DefaultLimitKernel));
        Assert.Equal(0, DynamicSharedMemoryOptIn.GetOptInCount(hfunc));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DynamicSharedMemory48KBPlus1_Launches()
    {
        SkipIfOptInNotAboveDefault();

        // n * 4 = 48 KB, one byte of the request is unused
        const uint bytes = DefaultLimitBytes + 1;
        var output = new uint[BlockSize];
        s_fillAndSum(output, (int)(DefaultLimitBytes / 4), bytes);
        Assert.Equal(ExpectedSums((int)(DefaultLimitBytes / 4), 0), output);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DynamicSharedMemory64KB_LaunchesAndUsesTheWholeRegion()
    {
        SkipIfOptInNotAboveDefault();
        AssertLaunchSums(s_fillAndSum, 64 * 1024, 0);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DynamicSharedMemoryAtOptInLimit_Launches()
    {
        SkipIfOptInNotAboveDefault();
        var bytes = (uint)(OptInLimit - StaticBytes(nameof(DynamicSharedMemoryLimitKernels.FillAndSumKernel)));
        AssertLaunchSums(s_fillAndSum, bytes, 0);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_StaticPlusDynamicAtOptInLimit_Launches()
    {
        SkipIfOptInNotAboveDefault();
        var staticBytes = StaticBytes(nameof(DynamicSharedMemoryLimitKernels.StaticPlusDynamicKernel));
        Assert.True(staticBytes >= StaticElements * sizeof(uint), $"static shared memory is {staticBytes} bytes");

        AssertLaunchSums(s_staticPlusDynamic, (uint)(OptInLimit - staticBytes), StaticElements);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_DynamicSharedMemoryAboveOptInLimit_ThrowsBeforeLaunch()
    {
        var limit = OptInLimit;
        var staticBytes = StaticBytes(nameof(DynamicSharedMemoryLimitKernels.FillAndSumKernel));
        var bytes = (uint)(limit - staticBytes + 1);
        var output = new uint[BlockSize];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => s_fillAndSum(output, (int)(bytes / 4), bytes));

        Assert.Equal("sharedMemBytes", ex.ParamName);
        Assert.StartsWith(
            $"G#: sharedMemBytes {bytes} plus {staticBytes} bytes of static shared memory exceeds the {limit} bytes of " +
            $"shared memory per block of this GPU (sm_{DeviceComputeCapability()}) " +
            "(DynamicSharedMemoryLimitKernels.FillAndSumKernel).", ex.Message);
        Assert.All(output, static value => Assert.Equal(0u, value));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_StaticPlusDynamicAboveOptInLimit_ThrowsBecauseStaticCounts()
    {
        SkipIfOptInNotAboveDefault();
        var limit = OptInLimit;
        var staticBytes = StaticBytes(nameof(DynamicSharedMemoryLimitKernels.StaticPlusDynamicKernel));
        var bytes = (uint)(limit - staticBytes + 1);
        var output = new uint[BlockSize];

        // The dynamic part alone is below the limit
        Assert.True(bytes < limit);
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => s_staticPlusDynamic(output, (int)(bytes / 4), bytes));

        Assert.Equal("sharedMemBytes", ex.ParamName);
        Assert.Contains($"plus {staticBytes} bytes of static shared memory exceeds the {limit} bytes", ex.Message);
        Assert.Contains("(DynamicSharedMemoryLimitKernels.StaticPlusDynamicKernel)", ex.Message);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_DynamicSharedMemoryMaxValue_ThrowsWithoutOverflow()
    {
        var output = new uint[BlockSize];
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DynamicSharedMemoryLimitKernels.LaunchFillAndSumKernel(output, 1, blockDimX: BlockSize,
                sharedMemBytes: uint.MaxValue));
        Assert.Contains($"sharedMemBytes {uint.MaxValue} plus", ex.Message);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_RepeatedLaunches_OptInOnce()
    {
        SkipIfOptInNotAboveDefault();
        var hfunc = GetFunction(nameof(DynamicSharedMemoryLimitKernels.FillAndSumKernel));
        var staticBytes = FunctionAttribute(hfunc, CuFunctionAttribute.SharedSizeBytes);

        for (var i = 0; i < 3; i++)
        {
            AssertLaunchSums(s_fillAndSum, 64 * 1024, 0);
            AssertLaunchSums(s_fillAndSum, 1024, 0);
            AssertLaunchSums(s_fillAndSum, 80 * 1024, 0);
        }

        // Any other test of this kernel opts in at most once as well
        Assert.Equal(1, DynamicSharedMemoryOptIn.GetOptInCount(hfunc));
        Assert.Equal(OptInLimit - staticBytes,
            FunctionAttribute(hfunc, CuFunctionAttribute.MaxDynamicSharedSizeBytes));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_OptIn_KeepsOccupancyOfSmallLaunches()
    {
        SkipIfOptInNotAboveDefault();
        var hfunc = GetFunction(nameof(DynamicSharedMemoryLimitKernels.OccupancyKernel));
        Assert.Equal(0, DynamicSharedMemoryOptIn.GetOptInCount(hfunc));
        var before = Occupancy(hfunc, 1024);

        var output = new uint[BlockSize];
        DynamicSharedMemoryLimitKernels.LaunchOccupancyKernel(output, 64 * 1024 / 4, blockDimX: BlockSize,
            sharedMemBytes: 64 * 1024);
        Assert.Equal(ExpectedSums(64 * 1024 / 4, 0), output);
        Assert.Equal(1, DynamicSharedMemoryOptIn.GetOptInCount(hfunc));

        Assert.Equal(before, Occupancy(hfunc, 1024));
    }

    private static int Occupancy(IntPtr hfunc, nuint dynamicBytes)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(
            CudaDriverApi.cuOccupancyMaxActiveBlocksPerMultiprocessor(out var blocks, hfunc, BlockSize, dynamicBytes));
        return blocks;
    }

    private static string DeviceComputeCapability() =>
        $"{DeviceAttribute(CudaDriverApi.CuDeviceAttribute.ComputeCapabilityMajor)}" +
        $"{DeviceAttribute(CudaDriverApi.CuDeviceAttribute.ComputeCapabilityMinor)}";
}
