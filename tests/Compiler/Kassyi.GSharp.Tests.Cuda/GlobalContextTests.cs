using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// The global context is the primary context of device 0. A reset of the primary context by another component of the
/// process frees the page-locked memory G# keeps in it, so G# refuses to use the context afterwards instead of reading
/// freed memory, also after the context has been initialized again under the same handle.
/// </summary>
/// <remarks>
/// The reset test runs in a child process: a reset cannot be undone for G#, and a missing check ends the process with
/// an access violation.
/// </remarks>
[Collection(TestCollections.DeviceExclusive)]
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class GlobalContextTests
{
    private const int Length = 64;

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void GlobalContext_AfterInitialization_IsThePrimaryContext()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        var global = GpuKernelLoader.GlobalContext;

        CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out var device, 0));
        CudaException.ThrowIfError(CudaDriverApi.cuDevicePrimaryCtxRetain(out var primary, device));
        CudaException.ThrowIfError(CudaDriverApi.cuDevicePrimaryCtxRelease_v2(device));

        Assert.Equal(primary, global);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Launch_AfterPrimaryContextReset_ThrowsInvalidOperation()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        const string Name = nameof(Launch_AfterPrimaryContextReset_ThrowsInvalidOperation);
        if (!IsolatedTestProcess.IsChild(Name))
        {
            IsolatedTestProcess.Run(typeof(GlobalContextTests), Name);
            return;
        }

        // The sequence of a component using the CUDA Runtime beside G#: cudaMalloc, cudaDeviceReset, cudaMalloc. The
        // Runtime calls are written as their driver equivalents, so the test needs no CUDA Runtime library.
        AssertAddKernelAdds();
        CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out var device, 0));
        AllocateAndFreeInPrimaryContext(device);

        CudaException.ThrowIfError(CudaDriverApi.cuDevicePrimaryCtxReset_v2(device));
        AssertRefused();

        // The Runtime initializes the primary context again on its next call; the handle stays the same
        var reinitialized = AllocateAndFreeInPrimaryContext(device);
        Assert.Equal(GpuKernelLoader.GlobalContext, reinitialized);
        AssertRefused();
        AssertRefused();
    }

    private static void AssertRefused()
    {
        var thrown = Record.Exception(AssertAddKernelAdds);
        var refused = Assert.IsType<InvalidOperationException>(thrown);
        Assert.Contains("was reset", refused.Message, StringComparison.Ordinal);
    }

    // cudaMalloc + cudaFree: retains the primary context (initializing it again after a reset) and keeps the retain, as
    // the CUDA Runtime does, makes it current and allocates in it; returns the primary context handle
    private static IntPtr AllocateAndFreeInPrimaryContext(int device)
    {
        CudaException.ThrowIfError(CudaDriverApi.cuDevicePrimaryCtxRetain(out var primary, device));
        CudaException.ThrowIfError(CudaDriverApi.cuCtxPushCurrent_v2(primary));
        try
        {
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out var pointer, 256));
            CudaException.ThrowIfError(CudaDriverApi.cuMemFree_v2(pointer));
        }
        finally
        {
            CudaException.ThrowIfError(CudaDriverApi.cuCtxPopCurrent_v2(out _));
        }

        return primary;
    }

    private static void AssertAddKernelAdds()
    {
        var a = new int[Length];
        var b = new int[Length];
        var c = new int[Length];
        var expected = new int[Length];
        for (var i = 0; i < Length; i++)
        {
            a[i] = i * 3;
            b[i] = i * 7;
            c[i] = -1;
            expected[i] = a[i] + b[i];
        }

        ModuleContextKernels.LaunchAddKernel(a, b, c, blockDimX: Length);
        Assert.Equal(expected, c);
    }
}
