#nullable enable
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// The trap rules that read the decisions (<see cref="IrTrapReading.Decided"/>): the length of an array whose receiver
/// is proven non-null does not throw, as in the syntax rules; the rules without the decisions stay as they were.
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class IrDecidedTrapsTests(ITestOutputHelper output)
{
    private const string NullCases = "Ir/IrDecisionNullCases.cs";
    private const string NullCasesType = "IrDecisionNullCases.Nulls";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void MayTrap_Undecided_IsUnchanged()
    {
        foreach (var name in new[] { "LengthOfStackArray", "LengthOfParameter" })
        {
            var length = Length(IrTestCases.Build(NullCases, NullCasesType, name));
            Assert.True(IrTraps.MayTrap(length));
            Assert.True(IrTraps.MayTrap(length, IrTrapReading.Undecided));
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void MayTrap_DecidedLengthOfAStackArray_IsFalse()
    {
        var length = Length(Decide("LengthOfStackArray"));
        Assert.Equal(IrMemberForm.StackArrayLength, length.Decided.Form);
        Assert.False(IrTraps.MayTrap(length, IrTrapReading.Decided));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void MayTrap_DecidedLengthOfAParameter_IsTrue()
    {
        var length = Length(Decide("LengthOfParameter"));
        Assert.NotNull(length.Decided.ReceiverSite);
        Assert.True(IrTraps.MayTrap(length, IrTrapReading.Decided));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void MayTrap_DecidedOnAnUndecidedTree_Throws()
    {
        var length = Length(IrTestCases.Build(NullCases, NullCasesType, "LengthOfStackArray"));
        Assert.Throws<InvalidOperationException>(() => IrTraps.MayTrap(length, IrTrapReading.Decided));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ArrayMembers_DecidedTraps_MatchTheSyntax()
    {
        var compared = 0;
        var mismatches = new ConcurrentBag<string>();
        IrCoverageInputs.ForEach((compilation, trees) =>
        {
            foreach (var (_, symbol, built) in IrCoverageInputs.GpuMethods(compilation, trees))
            {
                Interlocked.Add(ref compared, Compare(compilation, symbol, built, mismatches));
            }
        });

        output.WriteLine($"Compared {compared} members of System.Array, {mismatches.Count} mismatches");
        Assert.True(compared > 0, "No member of System.Array was compared.");
        Assert.True(mismatches.IsEmpty,
            string.Join("\n", mismatches.OrderBy(static m => m, StringComparer.Ordinal).Take(100)));
    }

    // The members of System.Array whose operands are variables, fields or constants, against ExpressionTraps
    private static int Compare(Compilation compilation, IMethodSymbol symbol, IrMethod built,
        ConcurrentBag<string> mismatches)
    {
        if (symbol.DeclaringSyntaxReferences.FirstOrDefault() is not { } reference)
        {
            return 0;
        }

        var tree = reference.SyntaxTree;
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var decided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
        var compared = 0;
        foreach (var node in IrNodeChildren.DescendantsAndSelf(decided.Body))
        {
            if (node is not IrPropertyReference
                {
                    Property.ContainingType.SpecialType: SpecialType.System_Array, Source.Synthesized: false
                } property ||
                IrInvariants.ContainsOpaque(property) ||
                !IrNodeChildren.DescendantsAndSelf(property).Skip(1)
                    .All(static child => child is IrVariableReference or IrFieldReference or IrConstant) ||
                IrClassificationEquivalence.FindExpression(root, property.Source.Span) is not { } syntax)
            {
                continue;
            }

            compared++;
            var expected = ExpressionTraps.MayTrap(syntax, model);
            var actual = IrEffects.Of(property, compilation, IrTrapReading.Decided).MayThrow;
            if (expected != actual)
            {
                mismatches.Add($"{IrCoverageInputs.Where(tree, symbol)}: syntax {expected} IR {actual}");
            }
        }

        return compared;
    }

    private static IrMethod Decide(string name)
    {
        var built = IrTestCases.Build(NullCases, NullCasesType, name);
        return IrDecisions.Decide(built, IrDecisionTestContext.Create(IrTestCases.Compile(NullCases), built));
    }

    private static IrPropertyReference Length(IrMethod method) =>
        IrNodeChildren.DescendantsAndSelf(method.Body).OfType<IrPropertyReference>().Single();
}
