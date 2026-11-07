using Kassyi.GSharp.Generated;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// CUDA modules and functions belong to the context they were loaded in: a launch uses the module of the context that
/// is current for it, whichever context launched the kernel first and whichever contexts were destroyed since.
/// </summary>
/// <remarks>
/// The launch tests run in a child process: they need a process in which the kernel has not been launched yet, and a
/// stale function handle crashes the process inside <c>cuLaunchKernel</c> instead of failing the test.
/// </remarks>
[Collection(TestCollections.DeviceExclusive)]
public class ModuleContextTests
{
    private const int Length = 64;

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Launch_AfterIsolatedContextIsDestroyed_UsesModuleOfGlobalContext()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        if (!IsolatedTestProcess.IsChild(nameof(Launch_AfterIsolatedContextIsDestroyed_UsesModuleOfGlobalContext)))
        {
            IsolatedTestProcess.Run(typeof(ModuleContextTests),
                nameof(Launch_AfterIsolatedContextIsDestroyed_UsesModuleOfGlobalContext));
            return;
        }

        var isolated = CreateContext();
        using (new GpuKernelLoader.CudaContextScope(isolated))
        {
            AssertAddKernelAdds(seed: 1);
        }

        GpuKernelLoader.DestroyContext(isolated);
        AssertAddKernelAdds(seed: 2);

        // The driver may hand out the destroyed context's handle again; nothing of the old context may be reused
        var second = CreateContext();
        try
        {
            using (new GpuKernelLoader.CudaContextScope(second))
            {
                AssertAddKernelAdds(seed: 3);
            }
        }
        finally
        {
            GpuKernelLoader.DestroyContext(second);
        }

        AssertAddKernelAdds(seed: 4);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Launch_InIsolatedContextAfterGlobal_UsesModuleOfIsolatedContext()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");
        if (!IsolatedTestProcess.IsChild(nameof(Launch_InIsolatedContextAfterGlobal_UsesModuleOfIsolatedContext)))
        {
            IsolatedTestProcess.Run(typeof(ModuleContextTests),
                nameof(Launch_InIsolatedContextAfterGlobal_UsesModuleOfIsolatedContext));
            return;
        }

        AssertAddKernelAdds(seed: 1);

        var isolated = CreateContext();
        try
        {
            using (new GpuKernelLoader.CudaContextScope(isolated))
            {
                AssertAddKernelAdds(seed: 2);
            }

            // Alternating contexts: each launch picks the function of its own context again
            AssertAddKernelAdds(seed: 3);
            using (new GpuKernelLoader.CudaContextScope(isolated))
            {
                AssertAddKernelAdds(seed: 4);
            }
        }
        finally
        {
            GpuKernelLoader.DestroyContext(isolated);
        }

        AssertAddKernelAdds(seed: 5);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void GetOrLoadModule_LoadsTheModuleOncePerContext()
    {
        Skip.IfNot(GpuKernelLoader.IsDriverAvailable, "CUDA driver is not available.");

        var global = GSharpModuleContext_Kassyi_GSharp_Tests_Cuda.GetOrLoadModule();
        Assert.Equal(global, GSharpModuleContext_Kassyi_GSharp_Tests_Cuda.GetOrLoadModule());

        var isolated = CreateContext();
        try
        {
            using (new GpuKernelLoader.CudaContextScope(isolated))
            {
                var inIsolated = GSharpModuleContext_Kassyi_GSharp_Tests_Cuda.GetOrLoadModule();
                Assert.NotEqual(global, inIsolated);
                Assert.Equal(inIsolated, GSharpModuleContext_Kassyi_GSharp_Tests_Cuda.GetOrLoadModule());
            }
        }
        finally
        {
            GpuKernelLoader.DestroyContext(isolated);
        }

        Assert.Equal(global, GSharpModuleContext_Kassyi_GSharp_Tests_Cuda.GetOrLoadModule());
    }

    // Launches AddKernel in the context current on this thread and checks every element; the inputs depend on seed, so
    // an output left from an earlier launch does not pass
    private static void AssertAddKernelAdds(int seed)
    {
        var a = new int[Length];
        var b = new int[Length];
        var c = new int[Length];
        var expected = new int[Length];
        for (var i = 0; i < Length; i++)
        {
            a[i] = i * 3 + seed * 1000;
            b[i] = i * 7 - seed;
            c[i] = -1;
            expected[i] = a[i] + b[i];
        }

        ModuleContextKernels.LaunchAddKernel(a, b, c, blockDimX: Length);
        Assert.Equal(expected, c);
    }

    // A new context, not left current on this thread (cuCtxCreate pushes it)
    private static IntPtr CreateContext()
    {
        CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out var device, 0));
        CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out var context, 0, device));
        CudaException.ThrowIfError(CudaDriverApi.cuCtxPopCurrent_v2(out _));
        return context;
    }
}
