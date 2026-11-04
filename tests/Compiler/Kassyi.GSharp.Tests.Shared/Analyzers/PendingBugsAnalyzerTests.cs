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
    [Fact(Skip = "#926: Extension-form recursion does not recognize recursive suspending functions.")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_RecursiveExtensionDeviceFunctionWithBarrier_ReportsGSH0061()
    {
        var (compilation, tree, model) =
            RoslynTestUtility.GetTestCaseContext("Analyzers/PendingBugs/RecursiveExtensionBarrierKernels.cu.cs");

        var methodSyntaxes = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .ToDictionary(m => m.Identifier.ValueText);

        var methods = methodSyntaxes
            .ToDictionary(
                kvp => kvp.Key,
                kvp => model.GetDeclaredSymbol(kvp.Value) ?? throw new InvalidOperationException($"No symbol for {kvp.Key}.")
            );

        Assert.True(SuspensionAnalysis.IsRecursiveSuspendingFunction(methods["DirectRec"], compilation));
        Assert.True(SuspensionAnalysis.IsRecursiveSuspendingFunction(methods["MutualA"], compilation));
        Assert.True(SuspensionAnalysis.IsRecursiveSuspendingFunction(methods["MutualB"], compilation));

        var diagnostics = await TestAnalyzer(
                "Analyzers/PendingBugs/RecursiveExtensionBarrierKernels.cu.cs",
                s_barrierAnalyzer)
            .GetDiagnosticsAsync();

        var gsh0061 = diagnostics
            .Where(d => d.Id == GSharpDiagnostics.RecursiveDeviceFunctionBarrierProhibited.Id)
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ToArray();

        Assert.Equal(3, gsh0061.Length);
        Assert.All(gsh0061, d => Assert.Equal(GSharpDiagnostics.RecursiveDeviceFunctionBarrierProhibited.Id, d.Id));

        var expectedDirect = methodSyntaxes["DirectRec"].Identifier.GetLocation().GetLineSpan();
        var expectedMutualA = methodSyntaxes["MutualA"].Identifier.GetLocation().GetLineSpan();
        var expectedMutualB = methodSyntaxes["MutualB"].Identifier.GetLocation().GetLineSpan();

        Assert.Equal(expectedDirect.Span, gsh0061[0].Location.GetLineSpan().Span);
        Assert.Equal(expectedMutualA.Span, gsh0061[1].Location.GetLineSpan().Span);
        Assert.Equal(expectedMutualB.Span, gsh0061[2].Location.GetLineSpan().Span);
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
        var (_, tree, _) =
            RoslynTestUtility.GetTestCaseContext("Analyzers/PendingBugs/GridStrideLoopBarrierKernels.cu.cs");

        var syncThreadsInvocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(inv => inv.Expression.ToString() == "Gpu.SyncThreads");
        var expectedSpan = syncThreadsInvocation.GetLocation().GetLineSpan();

        var diagnostics = await TestAnalyzer(
                "Analyzers/PendingBugs/GridStrideLoopBarrierKernels.cu.cs",
                s_barrierAnalyzer)
            .GetDiagnosticsAsync();

        var gsh0037 = diagnostics
            .Where(d => d.Id == GSharpDiagnostics.DivergentBarrierProhibited.Id)
            .ToArray();
        var gsh0062 = diagnostics
            .Where(d => d.Id == GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id)
            .ToArray();

        var diagnostic = Assert.Single(gsh0037);
        Assert.Equal(GSharpDiagnostics.DivergentBarrierProhibited.Id, diagnostic.Id);
        Assert.Equal(expectedSpan.Span, diagnostic.Location.GetLineSpan().Span);
        Assert.Empty(gsh0062);
    }

    // #1082: Expect 3 GSH0003 diagnostics for ToArray(), CopyTo(Span<T>), and SumSequential() once StackLinqHelper.IsHostOnly includes them.
    [Fact(Skip = "#1082: StackLinqHelper.IsHostOnly does not include ToArray, CopyTo, SumSequential.")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_HostOnlyStackLinqTerminalsInKernel_ReportsGSH0003()
    {
        var (_, tree, _) =
            RoslynTestUtility.GetTestCaseContext("Analyzers/PendingBugs/HostOnlyStackLinqProhibitedKernels.cu.cs");

        var invocations = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv => inv.Expression is MemberAccessExpressionSyntax ma &&
                          (ma.Name.Identifier.ValueText == "ToArray" ||
                           ma.Name.Identifier.ValueText == "CopyTo" ||
                           ma.Name.Identifier.ValueText == "SumSequential"))
            .OrderBy(inv => inv.SpanStart)
            .ToArray();

        Assert.Equal(3, invocations.Length);

        var diagnostics = await TestAnalyzer(
                "Analyzers/PendingBugs/HostOnlyStackLinqProhibitedKernels.cu.cs",
                s_kernelAnalyzer)
            .GetDiagnosticsAsync();

        var gsh0003 = diagnostics
            .Where(d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id)
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ToArray();

        Assert.Equal(3, gsh0003.Length);
        Assert.All(gsh0003, d => Assert.Equal(GSharpDiagnostics.UnsupportedSyntax.Id, d.Id));
        Assert.Equal(invocations[0].GetLocation().GetLineSpan().Span, gsh0003[0].Location.GetLineSpan().Span);
        Assert.Equal(invocations[1].GetLocation().GetLineSpan().Span, gsh0003[1].Location.GetLineSpan().Span);
        Assert.Equal(invocations[2].GetLocation().GetLineSpan().Span, gsh0003[2].Location.GetLineSpan().Span);
    }

    // #1083: Expect 2 SLQ0001 diagnostics once StackLinqChainAnalyzer counts StackEnumerable.OfType<T>() and static StackLinqExtensions calls.
    [Fact(Skip = "#1083: StackLinqChainAnalyzer does not count OfType or static-form calls.")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_StackLinqChainExceedingMaxDepthWithOfTypeOrStaticCall_ReportsSLQ0001()
    {
        var (_, tree, _) =
            RoslynTestUtility.GetTestCaseContext("Analyzers/PendingBugs/StackLinqChainDepthBypassKernels.cu.cs");

        var sumInvocations = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv => inv.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Sum" })
            .OrderBy(inv => inv.SpanStart)
            .ToArray();

        Assert.Equal(2, sumInvocations.Length);

        var diagnostics = await TestAnalyzer(
                "Analyzers/PendingBugs/StackLinqChainDepthBypassKernels.cu.cs",
                s_chainAnalyzer)
            .GetDiagnosticsAsync();

        var slq0001 = diagnostics
            .Where(d => d.Id == GSharpDiagnostics.StackLinqChainTooDeep.Id)
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ToArray();

        Assert.Equal(2, slq0001.Length);
        Assert.All(slq0001, d => Assert.Equal(GSharpDiagnostics.StackLinqChainTooDeep.Id, d.Id));
        Assert.Equal(sumInvocations[0].GetLocation().GetLineSpan().Span, slq0001[0].Location.GetLineSpan().Span);
        Assert.Equal(sumInvocations[1].GetLocation().GetLineSpan().Span, slq0001[1].Location.GetLineSpan().Span);
    }
}
