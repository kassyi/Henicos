#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies <see cref="IrNonNullProof"/> parity with syntax proof and post-split / barrier behavior (#1561).
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

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsProvenNonNull_AfterSpill_TreatsATemporaryAsUnproven()
    {
        var compilation = IrTestCases.Compile(TestCase);
        var preBuilt = IrTestCases.Build(TestCase, TypeName, "NotNullArrayThenShuffle");
        var preDecided = IrDecisions.Decide(preBuilt, IrDecisionTestContext.Create(compilation, preBuilt));
        var preElement = IrNodeChildren.DescendantsAndSelf(preDecided.Body).OfType<IrElementReference>().Single();
        Assert.Equal(IrElementCheck.None, preElement.Decided.Check);

        // A spilled receiver with nothing written around it (Gpu.NotNull(a)) is reported by the syntax path at the
        // whole element access: IrDecisionSiteOracleTests checks this against the generated site IDs

        var (built, machine) = IrDecisionTestContext.BuildSplitMachine(compilation, TestCase, TypeName,
            "NotNullArrayThenShuffle");
        var postDecided = IrDecisions.Decide(machine, IrDecisionTestContext.Create(compilation, machine, built));
        Assert.Empty(IrDecisionInvariants.Verify(postDecided));

        var postElements = IrNodeChildren.DescendantsAndSelf(postDecided).OfType<IrElementReference>().ToArray();
        Assert.NotEmpty(postElements);
        foreach (var element in postElements)
        {
            var receiver = Assert.IsType<IrVariableReference>(element.Receiver);
            Assert.Equal(IrVariableKind.Temporary, receiver.Variable.Kind);
            Assert.Equal(IrElementCheck.NullChecked, element.Decided.Check);
            Assert.NotNull(element.Decided.Site);
            Assert.Equal(element.Source.Span, element.Decided.Site.Value.Source.Span);
        }

        var storeAssignments = IrNodeChildren.DescendantsAndSelf(postDecided)
            .OfType<IrAssignment>()
            .Where(static a => a.Target is IrElementReference)
            .ToArray();
        var storeAssignment = Assert.Single(storeAssignments);
        var storeTarget = Assert.IsType<IrElementReference>(storeAssignment.Target);
        Assert.NotNull(storeAssignment.Decided.Store);
        Assert.NotNull(storeAssignment.Decided.Store.Value.Site);
        Assert.Equal(storeTarget.Source.Span, storeAssignment.Decided.Store.Value.Site.Value.Source.Span);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsProvenNonNull_InitializerChangedBySpill_UsesTheOriginalInitializer()
    {
        var compilation = IrTestCases.Compile(TestCase);
        var (built, machine) = IrDecisionTestContext.BuildSplitMachine(compilation, TestCase, TypeName,
            "ReadInitializerWithSplit");
        var postDecided = IrDecisions.Decide(machine, IrDecisionTestContext.Create(compilation, machine, built));
        Assert.Empty(IrDecisionInvariants.Verify(postDecided));

        var readElement = IrNodeChildren.DescendantsAndSelf(postDecided)
            .OfType<IrElementReference>()
            .Single(static e => e.Receiver is IrVariableReference { Variable.Name: "b" });
        Assert.Equal(IrElementCheck.None, readElement.Decided.Check);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsProvenNonNull_AcrossABarrier_IsProvenAsInCSharp()
    {
        var compilation = IrTestCases.Compile(TestCase);
        var (built, machine) = IrDecisionTestContext.BuildSplitMachine(compilation, TestCase, TypeName,
            "ReadNotNullAcrossBarrier");
        var postDecided = IrDecisions.Decide(machine, IrDecisionTestContext.Create(compilation, machine, built));
        Assert.Empty(IrDecisionInvariants.Verify(postDecided));

        var readElement = IrNodeChildren.DescendantsAndSelf(postDecided)
            .OfType<IrElementReference>()
            .Single(static e => e.Receiver is IrVariableReference { Variable.Name: "b" });
        Assert.Equal(IrElementCheck.None, readElement.Decided.Check);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsProvenNonNull_StackArrayAcrossABarrier_StaysProven()
    {
        var compilation = IrTestCases.Compile(TestCase);
        var (built, machine) = IrDecisionTestContext.BuildSplitMachine(compilation, TestCase, TypeName,
            "ReadStackArrayAcrossBarrier");
        var postDecided = IrDecisions.Decide(machine, IrDecisionTestContext.Create(compilation, machine, built));
        Assert.Empty(IrDecisionInvariants.Verify(postDecided));

        var readElement = IrNodeChildren.DescendantsAndSelf(postDecided)
            .OfType<IrElementReference>()
            .Single(static e => e.Receiver is IrVariableReference { Variable.Name: "s" });
        Assert.Equal(IrElementCheck.None, readElement.Decided.Check);
    }
}
