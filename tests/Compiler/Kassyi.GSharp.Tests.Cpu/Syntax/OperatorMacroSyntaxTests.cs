#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Cpu.Syntax;

/// <summary>
/// Verifies that <see cref="OperatorMacroSyntax.GetOperator"/> returns the truth test only on the original
/// <c>&amp;&amp;</c> / <c>||</c> node and drops it on the synthesized <c>&amp;</c> / <c>|</c> node after lowering.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class OperatorMacroSyntaxTests
{
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("AndMaskKernel", SyntaxKind.LogicalAndExpression, SyntaxKind.BitwiseAndExpression, "op_False")]
    [InlineData("OrMaskKernel", SyntaxKind.LogicalOrExpression, SyntaxKind.BitwiseOrExpression, "op_True")]
    public void GetOperator_SynthesizedBitwiseAfterConditionalLowering_DropsTruthTest(
        string kernelName,
        SyntaxKind originalKind,
        SyntaxKind synthesizedKind,
        string expectedTestName)
    {
        string kernelPath = RepositoryPaths.Combine(
            "tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "Syntax", "OperatorMacroConditionalSplitKernels.cu.cs");
        (Compilation compilation, SyntaxTree tree) = RoslynTestUtility.GetCallerSourceContext(kernelPath);
        SemanticModel model = compilation.GetSemanticModel(tree);
        MethodDeclarationSyntax method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == kernelName);
        BlockSyntax body = method.Body ?? throw new InvalidOperationException($"{kernelName} has no body.");
        BlockSyntax prepared = AstLoweringPipeline.Prepare(body, returnsValue: false, model);

        BinaryExpressionSyntax originalConditional = prepared.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .Single(b => b.IsKind(originalKind));
        (IMethodSymbol? Method, bool IsLifted, bool IsIncrementOrDecrement, IMethodSymbol? Test) originalOperator =
            OperatorMacroSyntax.GetOperator(originalConditional, model);
        Assert.NotNull(originalOperator.Method);
        Assert.False(originalOperator.IsLifted);
        Assert.Equal(expectedTestName, originalOperator.Test?.Name);

        BlockSyntax lowered = new ConditionalEvaluationRewriter(new LoweringTempNames(), model).Rewrite(prepared);

        BinaryExpressionSyntax synthesizedBitwise = lowered.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .Single(b => b.IsKind(synthesizedKind));
        (IMethodSymbol? Method, bool IsLifted, bool IsIncrementOrDecrement, IMethodSymbol? Test) synthesizedOperator =
            OperatorMacroSyntax.GetOperator(synthesizedBitwise, model);
        Assert.NotNull(synthesizedOperator.Method);
        Assert.False(synthesizedOperator.IsLifted);
        Assert.Null(synthesizedOperator.Test);

        InvocationExpressionSyntax truthTestCall = lowered.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(inv => inv.Expression is IdentifierNameSyntax id &&
                           id.Identifier.ValueText == GSharpInternalNames.Lowering.TruthTest);
        IMethodSymbol? truthTest = OperatorMacroSyntax.GetTruthTest(truthTestCall, model);
        Assert.Equal(expectedTestName, truthTest?.Name);
    }
}
