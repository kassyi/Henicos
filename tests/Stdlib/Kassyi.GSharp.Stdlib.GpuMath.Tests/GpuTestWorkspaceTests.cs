#nullable enable

using Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests;

[Category(TestCategory.Unit)]
public sealed class GpuTestWorkspaceTests
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.SadPath)]
    public void Constructor_WorkspaceFactoryThrows_DisposesContextScopeAndRestoresThreadContext()
    {
        CudaCapability.EnsureGpuAvailable();

        var currentBefore = new IntPtr(-1);
        var currentAfter = new IntPtr(-1);
        Exception? caught = null;
        Exception? threadFailure = null;

        var thread = new Thread(() =>
        {
            try
            {
                CudaException.ThrowIfError(CudaDriverApi.cuCtxGetCurrent(out currentBefore));
                try
                {
                    using var testWorkspace = new GpuTestWorkspace(
                        () => throw new InvalidOperationException("Simulated workspace failure."));
                }
                catch (InvalidOperationException ex)
                {
                    caught = ex;
                }
                finally
                {
                    CudaException.ThrowIfError(CudaDriverApi.cuCtxGetCurrent(out currentAfter));
                    if (currentAfter != IntPtr.Zero)
                    {
                        CudaDriverApi.cuCtxSetCurrent(IntPtr.Zero);
                    }
                }
            }
            catch (Exception ex)
            {
                threadFailure = ex;
            }
        });

        thread.Start();
        thread.Join();

        Assert.Null(threadFailure);
        Assert.Equal(IntPtr.Zero, currentBefore);
        Assert.IsType<InvalidOperationException>(caught);
        Assert.Equal(IntPtr.Zero, currentAfter);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.SadPath)]
    public void Constructor_WorkspaceFactoryThrowsInCpuFallback_RethrowsException()
    {
        using var fallback = CpuFallbackScope.Enter();

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var testWorkspace = new GpuTestWorkspace(
                () => throw new InvalidOperationException("Simulated workspace failure."));
        });

        Assert.Equal("Simulated workspace failure.", ex.Message);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Dispose_CreatedWorkspace_DisposesWorkspaceAndRestoresThreadContext()
    {
        CudaCapability.EnsureGpuAvailable();

        var currentBefore = new IntPtr(-1);
        var currentWhileAlive = IntPtr.Zero;
        var currentAfter = new IntPtr(-1);
        Exception? threadFailure = null;

        var thread = new Thread(() =>
        {
            try
            {
                CudaException.ThrowIfError(CudaDriverApi.cuCtxGetCurrent(out currentBefore));
                using var testWorkspace = new GpuTestWorkspace(64);
                CudaException.ThrowIfError(CudaDriverApi.cuCtxGetCurrent(out currentWhileAlive));

                testWorkspace.Dispose();
                testWorkspace.Dispose();

                CudaException.ThrowIfError(CudaDriverApi.cuCtxGetCurrent(out currentAfter));
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    using var temp = testWorkspace.Workspace.RentTemp(4);
                });
            }
            catch (Exception ex)
            {
                threadFailure = ex;
                CudaDriverApi.cuCtxSetCurrent(IntPtr.Zero);
            }
        });

        thread.Start();
        thread.Join();

        Assert.Null(threadFailure);
        Assert.Equal(IntPtr.Zero, currentBefore);
        Assert.NotEqual(IntPtr.Zero, currentWhileAlive);
        Assert.Equal(IntPtr.Zero, currentAfter);
    }
}
