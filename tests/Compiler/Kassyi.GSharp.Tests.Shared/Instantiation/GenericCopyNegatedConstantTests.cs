#nullable enable

using static Kassyi.GSharp.Tests.Shared.Instantiation.GenericCopyTestSupport;

namespace Kassyi.GSharp.Tests.Shared.Instantiation;

/// <summary>
/// The closed copy of an operator on a constant that overflows or divides by zero: C# folds a constant expression and
/// reports the overflow at compile time (also outside <c>checked</c>), where the declaration throws, or wraps, when it
/// runs. The copy writes such a constant so that C# cannot fold it.
/// </summary>
[Category(TestCategory.Compiler)]
public class GenericCopyNegatedConstantTests
{
    // -c, checked(-c), -(c) and -unchecked(c) of the minimum and of one
    public static TheoryData<string, string> NegationCases { get; } = new()
    {
        { "CheckedNegateMinValue", "System.SByte" },
        { "CheckedNegateMinValue", "System.Int16" },
        { "CheckedNegateMinValue", "System.Int32" },
        { "CheckedNegateMinValue", "System.Int64" },
        { "CheckedNegateMinValue", "System.IntPtr" },
        { "NegateMinValue", "System.Int32" },
        { "NegateMinValue", "System.Int64" },
        { "CheckedNegateParenthesizedMinValue", "System.Int32" },
        { "CheckedNegateUncheckedMinValue", "System.Int32" },
        { "CheckedNegateOne", "System.Byte" },
        { "CheckedNegateOne", "System.Char" },
        { "CheckedNegateOne", "System.UInt32" },
        { "CheckedNegateOne", "System.UInt64" },
        { "CheckedNegateOne", "System.UIntPtr" },
    };

    // A binary operator with a negated constant as an operand
    public static TheoryData<string, string> NegatedOperandCases { get; } = new()
    {
        { "MinValueOverNegatedOne", "System.Int32" },
        { "MinValueOverNegatedOne", "System.Int64" },
        { "CheckedMinValueTimesNegatedOne", "System.Int32" },
        { "CheckedNegatedMaxValueMinusTwo", "System.Int32" },
    };

    // A constant under ~ or unary plus, one operator further from the operation that overflows
    public static TheoryData<string, string> UnaryChainCases { get; } = new()
    {
        { "CheckedNegateComplementMaxValue", "System.Int32" },
        { "CheckedNegateComplementMaxValue", "System.Int64" },
        { "CheckedNegateUnaryPlusMinValue", "System.Int32" },
        { "CheckedComplementMaxValueMinusOne", "System.Int32" },
        { "OneModuloComplementMaxValue", "System.UInt32" },
    };

    // The forms whose copy is run, for the types that narrow or wrap
    public static TheoryData<string, string> SmallTypeValueCases { get; } = new()
    {
        { "CheckedNegateMinValue", "System.SByte" },
        { "CheckedNegateMinValue", "System.Int16" },
        { "NegateMinValue", "System.SByte" },
        { "CheckedNegateOne", "System.Byte" },
        { "CheckedNegateOne", "System.Char" },
    };

    public static TheoryData<string, string> WideTypeValueCases { get; } = new()
    {
        { "CheckedNegateMinValue", "System.Int32" },
        { "CheckedNegateMinValue", "System.Int64" },
        { "NegateMinValue", "System.Int32" },
        { "NegateMinValue", "System.Int64" },
        { "CheckedNegateOne", "System.UInt32" },
        { "CheckedNegateOne", "System.UInt64" },
        { "MinValueOverNegatedOne", "System.Int32" },
        { "CheckedMinValueTimesNegatedOne", "System.Int32" },
        { "CheckedNegatedMaxValueMinusTwo", "System.Int32" },
        { "CheckedNegateUncheckedMinValue", "System.Int32" },
        { "CheckedNegateComplementMaxValue", "System.Int32" },
        { "CheckedNegateComplementMaxValue", "System.Int64" },
        { "CheckedNegateUnaryPlusMinValue", "System.Int32" },
        { "CheckedComplementMaxValueMinusOne", "System.Int32" },
        { "OneModuloComplementMaxValue", "System.UInt32" },
    };

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(NegationCases))]
    [MemberData(nameof(NegatedOperandCases))]
    [MemberData(nameof(UnaryChainCases))]
    public void Create_OverflowingConstantOperation_GivesACopyThatCompiles(string method, string typeArgument)
    {
        var (copy, set) = CreateCopy(method, typeArgument);

        Assert.True(copy.IsFaithful, Describe(copy));
        var errors = ModelOf(set, copy.Declaration!)
            .GetDiagnostics(copy.Declaration!.Span)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id);
        Assert.Empty(errors);
    }

    // The copy throws what the declaration throws, or wraps as it wraps
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(SmallTypeValueCases))]
    [MemberData(nameof(WideTypeValueCases))]
    public void Emit_OverflowingConstantOperation_ComputesWhatTheDeclarationComputes(
        string method,
        string typeArgument) =>
        GenericCopyValueSupport.AssertCopyComputesWhatTheDeclarationComputes(method, typeArgument);
}
