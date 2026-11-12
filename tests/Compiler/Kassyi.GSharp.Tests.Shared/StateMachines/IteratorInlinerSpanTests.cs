using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Dialects;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Checks the parameter locals and the consumer copy of an <see cref="IteratorInliner"/> expansion whose call names
/// its arguments out of order: the arguments are spilled in the order written, the parameter locals map back to the
/// iterator's parameters, and the consumer copy maps back to the <c>foreach</c> in a region of its own
/// (generator-invariants §2).
/// </summary>
[Category(TestCategory.Compiler)]
public class IteratorInlinerSpanTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Expand_OutOfOrderNamedArguments_SpillsInTheOrderWritten()
    {
        var (_, _, _, expansion) = Expand();
        var declarators = Locals(expansion).Take(4).ToArray();

        var temp = GSharpInternalNames.Lowering.ArgumentTempPrefix;
        Assert.Equal(
            [$"{temp}0 = 3", $"{temp}1 = tid", $"start = {temp}1", $"count = {temp}0"],
            declarators.Select(static d => $"{d.Identifier.ValueText} = {d.Initializer?.Value}")
        );
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Expand_OutOfOrderNamedArguments_ParameterLocalsMapToTheIteratorParameters()
    {
        var (model, iterator, _, expansion) = Expand();
        var parameterLocals = Locals(expansion).Skip(2).Take(2).ToArray();

        Assert.All(
            parameterLocals.Zip(iterator.ParameterList.Parameters),
            pair => Assert.Equal(pair.Second.GetLocation(), model.GetOriginalLocation(pair.First))
        );
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Expand_OutOfOrderNamedArguments_ConsumerCopyMapsToTheForEachInItsOwnRegion()
    {
        var (model, _, forEach, expansion) = Expand();
        var loopVariable = Locals(expansion).Single(static d => d.Identifier.ValueText == "x");
        var consumerStatement = expansion.DescendantNodes()
            .OfType<ExpressionStatementSyntax>()
            .Single(static s => s.Expression is AssignmentExpressionSyntax
            {
                RawKind: (int)SyntaxKind.AddAssignmentExpression,
                Left: ElementAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.ValueText: "output" }
                },
                Right: IdentifierNameSyntax { Identifier.ValueText: "x" }
            });

        Assert.Equal(forEach.GetLocation(), model.GetOriginalLocation(loopVariable));
        Assert.Equal(
            Assert.IsType<BlockSyntax>(forEach.Statement).Statements[0].GetLocation(),
            model.GetOriginalLocation(consumerStatement)
        );

        // Region 1 is the iterator body; the consumer copy of its only yield takes region 2
        Assert.Equal(1, LoweringIdentity.GetRegion(Locals(expansion).First(static d => d.Identifier.ValueText == "i")));
        Assert.Equal(2, LoweringIdentity.GetRegion(loopVariable));
        var loopVariableReference = consumerStatement.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Single(static n => n.Identifier.ValueText == "x");
        Assert.Equal(2, LoweringIdentity.GetRegion(loopVariableReference));
    }

    private static IEnumerable<VariableDeclaratorSyntax> Locals(BlockSyntax expansion) =>
        expansion.DescendantNodes().OfType<VariableDeclaratorSyntax>();

    private static (SemanticModel Model, MethodDeclarationSyntax Iterator, ForEachStatementSyntax ForEach,
        BlockSyntax Expansion) Expand()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(
            Path.Combine("StateMachines", "NamedArgumentIterator.cs"));
        var iterator = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.ValueText == "Range");
        var forEach = tree.GetRoot().DescendantNodes().OfType<ForEachStatementSyntax>().Single();
        var invocation = Assert.IsType<InvocationExpressionSyntax>(forEach.Expression);
        var iteratorMethod = Assert.IsAssignableFrom<IMethodSymbol>(model.GetSymbolInfo(invocation).Symbol);
        var failures = new List<TranspileFailure>();

        var regionCounter = 0;
        var argumentTempCounter = 0;
        var expansion = IteratorInliner.Expand(
            forEach,
            iteratorMethod,
            invocation,
            new IteratorExpansionContext(model, new StandardCppDialect(), new Reporter(failures.Add, model)),
            ref regionCounter,
            ref argumentTempCounter);

        Assert.Empty(failures);
        Assert.Equal(2, argumentTempCounter);
        return (model, iterator, forEach, expansion);
    }
}
