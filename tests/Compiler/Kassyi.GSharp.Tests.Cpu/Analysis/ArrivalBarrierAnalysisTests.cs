#nullable enable
using Kassyi.GSharp.Compiler.Marshaling;
namespace Kassyi.GSharp.Tests.Shared.Analysis;

/// <summary>
/// Verifies which kernels wait at a barrier that counts a fixed number of arrivals
/// (<see cref="SuspensionAnalysis.ReachesArrivalBarrier"/>, <see cref="KernelHostModel.WaitsForArrivals"/>): a captured
/// launch of such a kernel gets an abort word and a node in the graph that clears it, any other captured launch does
/// not.
/// </summary>
[Category(TestCategory.Compiler)]
public class ArrivalBarrierAnalysisTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Analysis/ArrivalBarrierCases.cs")
    );

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("MBarrierKernel", true)]
    [InlineData("MBarrierThroughFunctionsKernel", true)]
    [InlineData("GridSyncKernel", true)]
    [InlineData("WaitAtMBarrier", true)]
    [InlineData("CooperativeWithoutBarrierKernel", false)]
    [InlineData("BlockBarrierKernel", false)]
    [InlineData("SyncsTheBlock", false)]
    public void ReachesArrivalBarrier_Method_FollowsTheCalls(string name, bool expected)
    {
        Assert.Equal(expected, SuspensionAnalysis.ReachesArrivalBarrier(GetMethod(name), s_compilation));
    }

    // A cooperative kernel waits for arrivals also without a Gpu.SyncGrid() call (an AsParallel() grid aggregate)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("MBarrierKernel", true)]
    [InlineData("MBarrierThroughFunctionsKernel", true)]
    [InlineData("GridSyncKernel", true)]
    [InlineData("CooperativeWithoutBarrierKernel", true)]
    [InlineData("BlockBarrierKernel", false)]
    public void WaitsForArrivals_Kernel_IsSetForCooperativeAndMBarrierKernels(string name, bool expected)
    {
        var kernel = GetMethod(name);
        var model = s_compilation.GetSemanticModel(kernel.DeclaringSyntaxReferences[0].SyntaxTree);

        Assert.Equal(expected, KernelHostModel.Create(kernel, default, model).WaitsForArrivals);
    }

    private static IMethodSymbol GetMethod(string name) =>
        (s_compilation.GetTypeByMetadataName("ArrivalBarrierCases.Kernels") ??
         throw new InvalidOperationException("Type 'Kernels' not found."))
        .GetMembers(name)
        .OfType<IMethodSymbol>()
        .First();
}
