#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies <see cref="IrNonNullProof"/> parity with syntax proof (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class IrNonNullProofTests
{
    private const string TestCase = "Ir/IrDecisionNullCases.cs";
    private const string TypeName = "IrDecisionNullCases.Nulls";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsProvenNonNull_ForEveryReceiverOfTheCases_MatchesTheSyntaxProof()
    {
        var compilation = IrTestCases.Compile(TestCase);
        var compared = 0;

        foreach (var built in IrTestCases.BuildAll(TestCase, TypeName))
        {
            var context = IrDecisionTestContext.Create(compilation, built);
            var syntaxRef = built.Symbol.DeclaringSyntaxReferences.FirstOrDefault();
            Assert.NotNull(syntaxRef);
            var tree = syntaxRef.SyntaxTree;
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            foreach (var node in IrNodeChildren.DescendantsAndSelf(built.Body))
            {
                var receiver = node switch
                {
                    IrElementReference element => element.Receiver,
                    IrPropertyReference { Receiver: { } r } => r,
                    _ => null
                };
                if (receiver is null || receiver.Source.Synthesized)
                {
                    continue;
                }

                if (IrClassificationEquivalence.FindExpression(root, receiver.Source.Span) is not { } syntax)
                {
                    continue;
                }

                compared++;
                Assert.Equal(ArrayNullCheck.NeedsCheck(syntax, model), IrNonNullProof.NeedsCheck(receiver, context));
            }
        }

        Assert.True(compared > 0);
    }
}
