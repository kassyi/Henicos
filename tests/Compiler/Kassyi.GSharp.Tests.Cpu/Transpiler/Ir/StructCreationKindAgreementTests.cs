#nullable enable
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;
using Kassyi.GSharp.Compiler.Analysis;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Over every struct creation of the GPU methods of the tests: the kind of creation and the order of its arguments
/// read from the IR (its constructor, its type, the evaluation indices of its arguments) are those the syntax path reads
/// from the creation it writes (the symbols of the semantic model and the arguments matched to the parameters).
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class StructCreationKindAgreementTests(ITestOutputHelper output)
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryStructCreation_KindAndOrderAgreeWithTheSyntax()
    {
        var mismatches = new ConcurrentBag<string>();
        var kinds = new ConcurrentDictionary<StructCreationKind, int>();
        var unordered = 0;
        IrCoverageInputs.ForEach((compilation, trees) =>
        {
            var dialect = new GSharpCudaDialect();
            foreach (var (_, symbol, built) in IrCoverageInputs.GpuMethods(compilation, trees))
            {
                foreach (var creation in IrNodeChildren.DescendantsAndSelf(built.Body).OfType<IrStructCreation>())
                {
                    var tree = SemanticModelExtensions.GetSyntaxTrees(compilation)[creation.Source.TreeOrdinal];
                    var where = IrCoverageInputs.Where(tree, symbol) + " " + creation.Source.Span;
                    if (FindCreation(tree.GetRoot(), creation.Source.Span) is not { } node)
                    {
                        mismatches.Add($"{where}: no creation syntax");
                        continue;
                    }

                    var model = compilation.GetSemanticModel(tree);
                    var type = model.GetTypeSymbol(node);
                    var constructor = model.GetSymbol(node) as IMethodSymbol;
                    if (type is null || !SymbolEqualityComparer.Default.Equals(type, creation.Type) ||
                        !SymbolEqualityComparer.Default.Equals(constructor, creation.Constructor))
                    {
                        mismatches.Add($"{where}: symbols differ");
                        continue;
                    }

                    var syntaxKind = StructCreationClassifier.Classify(type, constructor, compilation,
                        dialect, out var syntaxMapped);
                    var irKind = StructCreationClassifier.Classify(creation.Type, creation.Constructor,
                        compilation, dialect, out var irMapped);
                    kinds.AddOrUpdate(irKind, 1, static (_, count) => count + 1);
                    if (syntaxKind != irKind || syntaxMapped != irMapped)
                    {
                        mismatches.Add($"{where}: kind {syntaxKind} {syntaxMapped} IR {irKind} {irMapped}");
                    }

                    if (constructor is null || creation.Arguments.Any(static a => a.Kind == IrArgumentKind.ParamArray))
                    {
                        continue;
                    }

                    var syntaxOrdinals = SyntaxOrdinals(node, constructor);
                    var irOrdinals = IrOrdinals(creation);
                    if (!syntaxOrdinals.SequenceEqual(irOrdinals))
                    {
                        mismatches.Add($"{where}: ordinals [{string.Join(",", syntaxOrdinals)}] " +
                                       $"IR [{string.Join(",", irOrdinals)}]");
                    }

                    if (!StructCreationClassifier.IsInParameterOrder(constructor.Parameters.Length, irOrdinals))
                    {
                        Interlocked.Increment(ref unordered);
                    }
                }
            }
        });

        foreach (var pair in kinds.OrderBy(static p => p.Key))
        {
            output.WriteLine($"{pair.Key}: {pair.Value}");
        }

        output.WriteLine($"Not in parameter order: {unordered}");
        Assert.True(kinds.Values.Sum() > 0, "No struct creation was compared.");
        Assert.True(mismatches.IsEmpty,
            string.Join("\n", mismatches.OrderBy(static m => m, StringComparer.Ordinal).Take(100)));
    }

    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(new int[0], 0, true, true)]
    [InlineData(new[] { 0, 1 }, 2, true, true)]
    [InlineData(new[] { 1, 0 }, 2, false, false)]
    [InlineData(new[] { 0 }, 2, false, true)]
    [InlineData(new[] { 0, 2 }, 3, false, true)]
    [InlineData(new[] { 0, -1 }, 2, false, false)]
    public void ParameterOrder_Ordinals_MatchTheRules(int[] ordinals, int parameterCount, bool inOrder,
        bool writtenInOrder)
    {
        Assert.Equal(inOrder, StructCreationClassifier.IsInParameterOrder(parameterCount, ordinals));
        Assert.Equal(writtenInOrder, StructCreationClassifier.IsWrittenInParameterOrder(ordinals));
    }

    // The parameter of each written argument, in the written order
    private static int[] SyntaxOrdinals(BaseObjectCreationExpressionSyntax node, IMethodSymbol constructor)
    {
        var arguments = node.ArgumentList?.Arguments ?? default;
        var matched = ArgumentMatchingHelper.MatchArgumentsToParameters(arguments, constructor);
        return arguments.Select(argument => Array.IndexOf(matched, argument)).ToArray();
    }

    // The parameter of each written argument of the IR, in the C# order of evaluation
    private static int[] IrOrdinals(IrStructCreation creation) =>
        creation.Arguments
            .Where(static a => a.Kind == IrArgumentKind.Explicit)
            .OrderBy(static a => a.EvaluationIndex)
            .Select(static a => a.Parameter.Ordinal)
            .ToArray();

    private static BaseObjectCreationExpressionSyntax? FindCreation(SyntaxNode root, TextSpan span) =>
        root.FindNode(span, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .OfType<BaseObjectCreationExpressionSyntax>()
            .FirstOrDefault(node => node.Span == span);
}
