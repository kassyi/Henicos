#nullable enable
using System.Collections.Concurrent;
using System.IO;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Semantics;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Numeric;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Corpus-wide comparison of facts extracted from the IR and from IOperation (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class IrNumericFactsEquivalenceTests(ITestOutputHelper output)
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryGpuMethod_NumericFactsAgreeWithOperations()
    {
        var mismatches = new ConcurrentBag<string>();
        int binaryCount = 0;
        int unaryCount = 0;
        int stepCount = 0;
        int compoundCount = 0;

        IrCoverageInputs.ForEach((compilation, trees) =>
        {
            foreach (var (_, symbol, method) in IrCoverageInputs.GpuMethods(compilation, trees))
            {
                var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
                if (syntaxRef == null)
                {
                    continue;
                }

                var tree = syntaxRef.SyntaxTree;
                var model = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();
                var where = $"{Path.GetFileName(tree.FilePath)} {symbol.ContainingType?.Name}.{symbol.Name}";

                foreach (var node in IrNodeChildren.DescendantsAndSelf(method.Body))
                {
                    switch (node)
                    {
                        case IrBinary binary when !binary.Source.Synthesized && !IrInvariants.ContainsOpaque(binary):
                        {
                            if (FindExpression(root, binary.Source.Span) is { } syntax &&
                                model.GetOperation(syntax) is IBinaryOperation op)
                            {
                                Interlocked.Increment(ref binaryCount);
                                var irFacts = IrNumericFacts.Binary(binary);
                                var opFacts = IntegerBinaryFacts.Of(op);

                                if (irFacts.Kind != opFacts.Kind ||
                                    !SymbolEqualityComparer.Default.Equals(irFacts.Type, opFacts.Type) ||
                                    !SymbolEqualityComparer.Default.Equals(irFacts.LeftType, opFacts.LeftType) ||
                                    irFacts.IsBuiltIn != opFacts.IsBuiltIn ||
                                    irFacts.HasConstantValue != opFacts.HasConstantValue ||
                                    irFacts.Left != opFacts.Left ||
                                    irFacts.Right != opFacts.Right ||
                                    IntegerSemantics.GetBinaryHelper(irFacts) !=
                                    IntegerSemantics.GetBinaryHelper(opFacts) ||
                                    IntegerSemantics.GetShiftMask(irFacts) !=
                                    IntegerSemantics.GetShiftMask(opFacts))
                                {
                                    mismatches.Add($"{where}: {syntax}: syntax {opFacts} IR {irFacts}");
                                }
                            }
                            break;
                        }

                        case IrUnary unary when !unary.Source.Synthesized && !IrInvariants.ContainsOpaque(unary):
                        {
                            if (FindExpression(root, unary.Source.Span) is { } syntax &&
                                model.GetOperation(syntax) is IUnaryOperation op)
                            {
                                Interlocked.Increment(ref unaryCount);
                                var irFacts = IrNumericFacts.Unary(unary);
                                var opFacts = IntegerUnaryFacts.Of(op);

                                if (irFacts.Kind != opFacts.Kind ||
                                    !SymbolEqualityComparer.Default.Equals(irFacts.Type, opFacts.Type) ||
                                    irFacts.IsBuiltIn != opFacts.IsBuiltIn ||
                                    irFacts.HasConstantValue != opFacts.HasConstantValue ||
                                    irFacts.Operand != opFacts.Operand ||
                                    IntegerSemantics.GetNegationHelper(irFacts) !=
                                    IntegerSemantics.GetNegationHelper(opFacts))
                                {
                                    mismatches.Add($"{where}: {syntax}: syntax {opFacts} IR {irFacts}");
                                }
                            }
                            break;
                        }

                        case IrIncrement increment when !increment.Source.Synthesized &&
                                                        !IrInvariants.ContainsOpaque(increment):
                        {
                            if (FindExpression(root, increment.Source.Span) is { } syntax &&
                                model.GetOperation(syntax) is IIncrementOrDecrementOperation op)
                            {
                                Interlocked.Increment(ref stepCount);
                                var irFacts = IrNumericFacts.Step(increment);
                                var opFacts = IntegerStepFacts.Of(op);

                                if (irFacts.IsIncrement != opFacts.IsIncrement ||
                                    !SymbolEqualityComparer.Default.Equals(irFacts.Type, opFacts.Type) ||
                                    !SymbolEqualityComparer.Default.Equals(irFacts.TargetType, opFacts.TargetType) ||
                                    irFacts.IsBuiltIn != opFacts.IsBuiltIn ||
                                    IntegerSemantics.GetStepHelper(irFacts, false) !=
                                    IntegerSemantics.GetStepHelper(opFacts, false) ||
                                    IntegerSemantics.GetStepHelper(irFacts, true) !=
                                    IntegerSemantics.GetStepHelper(opFacts, true) ||
                                    !SymbolEqualityComparer.Default.Equals(
                                        IntegerSemantics.GetStepNarrowing(irFacts),
                                        IntegerSemantics.GetStepNarrowing(opFacts)))
                                {
                                    mismatches.Add($"{where}: {syntax}: syntax {opFacts} IR {irFacts}");
                                }
                            }
                            break;
                        }

                        case IrCompoundAssignment compound when !compound.Source.Synthesized &&
                                                                !IrInvariants.ContainsOpaque(compound):
                        {
                            if (FindExpression(root, compound.Source.Span) is { } syntax &&
                                model.GetOperation(syntax) is ICompoundAssignmentOperation op)
                            {
                                Interlocked.Increment(ref compoundCount);
                                var irFacts = IrNumericFacts.Compound(compound);
                                var opFacts = IntegerCompoundFacts.Of(op);

                                if (irFacts.Kind != opFacts.Kind ||
                                    !SymbolEqualityComparer.Default.Equals(irFacts.TargetType, opFacts.TargetType) ||
                                    !SymbolEqualityComparer.Default.Equals(irFacts.ValueType, opFacts.ValueType) ||
                                    irFacts.IsBuiltIn != opFacts.IsBuiltIn ||
                                    irFacts.Target != opFacts.Target ||
                                    irFacts.Value != opFacts.Value ||
                                    IntegerSemantics.GetCompoundHelper(irFacts) !=
                                    IntegerSemantics.GetCompoundHelper(opFacts) ||
                                    IntegerSemantics.GetCompoundShiftMask(irFacts) !=
                                    IntegerSemantics.GetCompoundShiftMask(opFacts) ||
                                    !SymbolEqualityComparer.Default.Equals(
                                        IntegerSemantics.GetCompoundNarrowing(irFacts),
                                        IntegerSemantics.GetCompoundNarrowing(opFacts)))
                                {
                                    mismatches.Add($"{where}: {syntax}: syntax {opFacts} IR {irFacts}");
                                }
                            }
                            break;
                        }
                    }
                }
            }
        });

        int totalCount = binaryCount + unaryCount + stepCount + compoundCount;
        output.WriteLine(
            $"Compared facts: binary {binaryCount}, unary {unaryCount}, step {stepCount}, " +
            $"compound {compoundCount}, total {totalCount}");

        Assert.True(mismatches.IsEmpty,
            string.Join("\n", mismatches.OrderBy(static m => m, StringComparer.Ordinal).Take(100)));
        Assert.True(totalCount > 0, "No expressions compared across the corpus.");
    }

    private static ExpressionSyntax? FindExpression(SyntaxNode root, TextSpan span)
    {
        var node = root.FindNode(span, getInnermostNodeForTie: true);
        if (node.Span != span || node is not ExpressionSyntax expression)
        {
            return null;
        }

        while (expression.Parent is ExpressionSyntax parent && parent.Span == span)
        {
            expression = parent;
        }

        return expression;
    }
}
