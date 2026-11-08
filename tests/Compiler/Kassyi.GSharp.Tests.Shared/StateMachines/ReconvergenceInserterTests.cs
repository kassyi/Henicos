#nullable enable
using System;
using System.IO;
using System.Linq;
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 tests for <see cref="ReconvergenceInserter"/> (#844): the lanes of a warp that took different ways through a
/// thread-dependent branch or loop of a method that reaches <c>Warp.ActiveMask()</c> wait for each other where the
/// branch ends. That place must be the immediate post-dominator of the branch in the control flow graph that
/// <see cref="UniformityAnalysis"/> builds, which is where the GPU runs them together again.
/// </summary>
[Category(TestCategory.Compiler)]
public class ReconvergenceInserterTests
{
    private const string TestCase = "ReconvergenceCases.cs";

    // The user calls (Mark, Next) that are the first call after each reconvergence point of the lowered body, and the
    // ones that UniformityAnalysis finds at the immediate post-dominator of each thread-variant branch that holds a split
    // point. A branch that a lane leaves by a jump (continue) also reconverges where it ends for the lanes that did not
    // jump, before a call to Warp.ActiveMask() here, which costs a round and changes no group
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("IfKernel", "Mark(1)")]
    [InlineData("IfElseKernel", "Mark(2)")]
    [InlineData("DivergentForKernel", "Mark(3)")]
    [InlineData("BreakKernel", "Mark(4)")]
    [InlineData("ContinueKernel", "Next(k)")]
    [InlineData("NestedKernel", "Mark(5)", "Mark(6)")]
    public void Insert_ThreadDependentBranch_ReconvergesAtItsImmediatePostDominator(string kernel,
        params string[] expected)
    {
        var (lowered, joins) = LowerAndAnalyze(kernel);

        Assert.Equal(expected, lowered);
        Assert.Equal(expected, joins);
    }

    // A uniform branch, a branch without a split point, and a branch that ends the kernel get no reconvergence point
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("UniformAndPlainKernel")]
    [InlineData("LastStatementKernel")]
    public void Insert_UniformBranchOrKernelEnd_AddsNoReconvergencePoint(string kernel)
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(Path.Combine("StateMachines", TestCase));
        var method = FindMethod(tree, kernel);
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method)
                     ?? throw new InvalidOperationException($"{kernel} has no symbol.");

        var lowered = AstLoweringPipeline.Lower(method, model, symbol, isDeviceFunction: false);

        Assert.DoesNotContain(
            lowered.LoweredBody.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergeArrive));
        Assert.Equal(0, lowered.WarpSiteCount - CountWarpCalls(lowered.LoweredBody));
    }

    private static (string[] Lowered, string[] Joins) LowerAndAnalyze(string kernel)
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(Path.Combine("StateMachines", TestCase));
        var method = FindMethod(tree, kernel);
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method)
                     ?? throw new InvalidOperationException($"{kernel} has no symbol.");

        var lowered = AstLoweringPipeline.Lower(method, model, symbol, isDeviceFunction: false);
        var afterReconvergence = lowered.LoweredBody
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(static call => Calls(call, GSharpInternalNames.Prelude.WarpReconvergePass))
            .Select(static pass => FirstUserCallAfter(pass))
            .OfType<string>()
            .OrderBy(static call => call, StringComparer.Ordinal)
            .ToArray();

        Assert.True(new MethodFlowGraph(model, method).TryGetUniformity(out var uniformity));
        var joins = uniformity.GetDivergentBranches()
            .Where(static branch => branch.HoldsSplitPoint)
            .SelectMany(static branch => branch.JoinCalls)
            .OfType<InvocationExpressionSyntax>()
            .Select(Describe)
            .OfType<string>()
            .Distinct()
            .OrderBy(static call => call, StringComparer.Ordinal)
            .ToArray();
        return (afterReconvergence, joins);
    }

    private static MethodDeclarationSyntax FindMethod(SyntaxTree tree, string name) =>
        tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == name);

    // Mark(<id>) or Next(k) for a user call, whatever the lowering made of the local k; null for any other call
    private static string? Describe(InvocationExpressionSyntax call) =>
        call switch
        {
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "Mark" },
                ArgumentList.Arguments: [{ Expression: LiteralExpressionSyntax { Token.ValueText: var id } }]
            } => $"Mark({id})",
            { Expression: IdentifierNameSyntax { Identifier.ValueText: "Next" } } => "Next(k)",
            _ => null
        };

    private static bool Calls(InvocationExpressionSyntax call, string name) =>
        call.Expression is IdentifierNameSyntax { Identifier.ValueText: var callee } && callee == name;

    // The first call after the pass of a reconvergence point, when it is a user call. The states of the lowered body
    // follow each other in the order of the source, so the next call in the text is the next one that runs (a state
    // that only jumps to the next state has none)
    private static string? FirstUserCallAfter(InvocationExpressionSyntax pass)
    {
        var body = pass.Ancestors().OfType<BlockSyntax>().Last();
        var call = body.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(c => c.SpanStart > pass.Span.End);
        return call != null ? Describe(call) : null;
    }

    // The warp collectives of a lowered body: one warp site each, besides the reserved sites of the regions
    private static int CountWarpCalls(BlockSyntax body) =>
        body.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(static call => Calls(call, GSharpInternalNames.Prelude.WarpPublish));
}
