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

    [Theory]
    [InlineData(SpecialType.System_Int64, false)]
    [InlineData(SpecialType.System_Byte, true)]
    [Scenario(TestScenario.Boundary)]
    public void MayTrap_CheckedNumericConversionWrittenByTheCompiler_ThrowsOnlyWhenItNarrows(SpecialType target,
        bool mayThrow)
    {
        // A tuple element conversion is a node the compiler writes, yet ((byte, byte))(i, j) narrows and may throw
        var method = IrTestCases.BuildCase("InVariable");
        var compilation = IrTestCases.Compile(IrTestCases.BuilderCases);
        var source = IrTestCases.Result(method).Source;
        var operand = new IrVariableReference(method.Parameters[0], false, IrCategory.Value, default, source);
        var numeric = new IrOperator<IrConversionKind>(IrConversionKind.Numeric, null, IsChecked: true);
        var conversion = new IrConversion(operand, numeric, isImplicit: true, compilation.GetSpecialType(target),
            IrCategory.Value, default, source);

        Assert.Equal(mayThrow, IrTraps.MayTrap(conversion));
        Assert.Equal(mayThrow, IrTraps.MayTrap(conversion, IrTrapReading.Decided));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ImplicitNumericConversions_EveryNumericPair_MatchesRoslyn()
    {
        var compilation = IrTestCases.Compile(IrTestCases.BuilderCases);
        ITypeSymbol[] types =
        [
            .. new[]
            {
                SpecialType.System_SByte, SpecialType.System_Byte, SpecialType.System_Int16, SpecialType.System_UInt16,
                SpecialType.System_Int32, SpecialType.System_UInt32, SpecialType.System_Int64,
                SpecialType.System_UInt64, SpecialType.System_Char, SpecialType.System_Single,
                SpecialType.System_Double, SpecialType.System_Decimal
            }.Select(compilation.GetSpecialType),
            compilation.CreateNativeIntegerTypeSymbol(signed: true),
            compilation.CreateNativeIntegerTypeSymbol(signed: false)
        ];
        var mismatches = new List<string>();
        foreach (var from in types)
        {
            foreach (var to in types)
            {
                var roslyn = compilation.ClassifyConversion(from, to) is { IsImplicit: true, IsNumeric: true };
                if (roslyn != IrImplicitNumericConversions.Exists(from, to))
                {
                    mismatches.Add($"{from} -> {to}: Roslyn {roslyn}");
                }
            }
        }

        Assert.Empty(mismatches);
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
