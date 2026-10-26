namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

/// <summary>
/// Which graph implementation <see cref="GSharpKernelProvider"/> uses in CPU fallback mode.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public class GSharpKernelProviderTests
{
    // In CPU fallback mode the graph members are no-ops: there is no CUDA stream to capture.
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void GraphMembers_InCpuFallback_AreNoOps()
    {
        // ForceCpuFallback is AsyncLocal, so the override cannot leak into other tests.
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            var provider = GSharpKernelProvider.Instance;

            // mutant: GSharpKernelProvider.cs:44 graph provider always `_graph` (cuStreamBeginCapture(0) fails)
            provider.BeginCapture(IntPtr.Zero);
            var graph = provider.EndCapture(IntPtr.Zero);
            Assert.Equal(IntPtr.Zero, graph);

            var exec = provider.InstantiateGraph(graph);
            Assert.Equal(IntPtr.Zero, exec);

            provider.LaunchGraph(exec, IntPtr.Zero);
            provider.DestroyGraph(graph, exec);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }
}
