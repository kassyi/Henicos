#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Host;
using Kassyi.GSharp.Compiler.Diagnostics.Kernel;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// Analyzer and static-analysis tests pinning current behavior for open issues (#926, #1082, #1083)
/// and regression coverage for barrier reachability and grid-stride loop barrier diagnostics (#490).
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class PendingBugsAnalyzerTests : TranspilerTestBase
{
    private static readonly BarrierUsageAnalyzer s_barrierAnalyzer = new();
    private static readonly GpuKernelAnalyzer s_kernelAnalyzer = new();
    private static readonly StackLinqChainAnalyzer s_chainAnalyzer = new();

    // #926: Expect IsRecursiveSuspendingFunction to return true and 3 GSH0061 diagnostics to be reported once ReducedFrom is normalized.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_RecursiveExtensionDeviceFunctionWithBarrier_DoesNotReportGSH0061()
    {
        var (compilation, tree, model) =
            RoslynTestUtility.GetTestCaseContext("Analyzers/PendingBugs/RecursiveExtensionBarrierKernels.cu.cs");

        var methods = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Select(m => model.GetDeclaredSymbol(m))
            .OfType<IMethodSymbol>()
            .ToDictionary(m => m.Name);

        Assert.False(SuspensionAnalysis.IsRecursiveSuspendingFunction(methods["DirectRec"], compilation));
        Assert.False(SuspensionAnalysis.IsRecursiveSuspendingFunction(methods["MutualA"], compilation));
        Assert.False(SuspensionAnalysis.IsRecursiveSuspendingFunction(methods["MutualB"], compilation));

        var diagnostics = await TestAnalyzer(
                "Analyzers/PendingBugs/RecursiveExtensionBarrierKernels.cu.cs",
                s_barrierAnalyzer)
            .GetDiagnosticsAsync();

        var gsh0061 = diagnostics
            .Where(d => d.Id == GSharpDiagnostics.RecursiveDeviceFunctionBarrierProhibited.Id)
            .ToArray();
        Assert.Empty(gsh0061);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_MutualRecursionBarrierReachability_IsOrderIndependent()
    {
        var source = RoslynTestUtility.ReadTestCase(
            "Analyzers/PendingBugs/MutualRecursionBarrierOrderKernels.cu.cs");

        // Fresh compilation 1: query CycleA first, then CycleB
        var compAB = RoslynTestUtility.CreateCompilation(source);
        var treeAB = compAB.SyntaxTrees.Last();
        var modelAB = compAB.GetSemanticModel(treeAB);
        var methodsAB = treeAB.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Select(m => modelAB.GetDeclaredSymbol(m))
            .OfType<IMethodSymbol>()
            .ToDictionary(m => m.Name);

        var aFirstA = SuspensionAnalysis.ReachesBlockBarrier(methodsAB["CycleA"], compAB);
        var aFirstB = SuspensionAnalysis.ReachesBlockBarrier(methodsAB["CycleB"], compAB);
        var aFirstEntryA = SuspensionAnalysis.IsSuspendingFunction(methodsAB["EntryToA"], compAB);
        var aFirstEntryB = SuspensionAnalysis.IsSuspendingFunction(methodsAB["EntryToB"], compAB);

        // Fresh compilation 2: query CycleB first, then CycleA
        var compBA = RoslynTestUtility.CreateCompilation(source);
        var treeBA = compBA.SyntaxTrees.Last();
        var modelBA = compBA.GetSemanticModel(treeBA);
        var methodsBA = treeBA.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Select(m => modelBA.GetDeclaredSymbol(m))
            .OfType<IMethodSymbol>()
            .ToDictionary(m => m.Name);

        var bFirstB = SuspensionAnalysis.ReachesBlockBarrier(methodsBA["CycleB"], compBA);
        var bFirstA = SuspensionAnalysis.ReachesBlockBarrier(methodsBA["CycleA"], compBA);
        var bFirstEntryB = SuspensionAnalysis.IsSuspendingFunction(methodsBA["EntryToB"], compBA);
        var bFirstEntryA = SuspensionAnalysis.IsSuspendingFunction(methodsBA["EntryToA"], compBA);

        Assert.True(aFirstA);
        Assert.True(aFirstB);
        Assert.True(aFirstEntryA);
        Assert.True(aFirstEntryB);
        Assert.Equal((bFirstA, bFirstB, bFirstEntryA, bFirstEntryB), (aFirstA, aFirstB, aFirstEntryA, aFirstEntryB));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DirectSyncThreadsInGridStrideLoop_ReportsGSH0037()
    {
        var diagnostics = await TestAnalyzer(
                "Analyzers/PendingBugs/GridStrideLoopBarrierKernels.cu.cs",
                s_barrierAnalyzer)
            .GetDiagnosticsAsync();

        var gsh0037 = diagnostics
            .Where(d => d.Id == GSharpDiagnostics.DivergentBarrierProhibited.Id)
            .ToArray();
        var gsh0062 = diagnostics
            .Where(d => d.Id == "GSH0062")
            .ToArray();

        Assert.Single(gsh0037);
        Assert.Empty(gsh0062);
    }

    // #1082: Expect 3 GSH0003 diagnostics for ToArray(), CopyTo(Span<T>), and SumSequential() once StackLinqHelper.IsHostOnly includes them.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_HostOnlyStackLinqTerminalsInKernel_DoesNotReportGSH0003()
    {
        var diagnostics = await TestAnalyzer(
                "Analyzers/PendingBugs/HostOnlyStackLinqProhibitedKernels.cu.cs",
                s_kernelAnalyzer)
            .GetDiagnosticsAsync();

        var gsh0003 = diagnostics
            .Where(d => d.Id == "GSH0003")
            .ToArray();

        Assert.Empty(gsh0003);
    }

    // #1083: Expect 2 SLQ0001 diagnostics once StackLinqChainAnalyzer counts StackEnumerable.OfType<T>() and static StackLinqExtensions calls.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_StackLinqChainExceedingMaxDepthWithOfTypeOrStaticCall_DoesNotReportSLQ0001()
    {
        var diagnostics = await TestAnalyzer(
                "Analyzers/PendingBugs/StackLinqChainDepthBypassKernels.cu.cs",
                s_chainAnalyzer)
            .GetDiagnosticsAsync();

        var slq0001 = diagnostics
            .Where(d => d.Id == GSharpDiagnostics.StackLinqChainTooDeep.Id)
            .ToArray();

        Assert.Empty(slq0001);
    }
}
