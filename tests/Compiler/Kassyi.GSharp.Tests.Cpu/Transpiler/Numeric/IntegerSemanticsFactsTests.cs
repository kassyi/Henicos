#nullable enable
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Analysis.Checks;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Numeric;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Numeric;

/// <summary>
/// Unit tests for <see cref="IntegerSemantics"/> pure decision functions taking facts and ranges (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class IntegerSemanticsFactsTests
{
    private static readonly Compilation Compilation = RoslynTestUtility.CreateCompilation("");
    private static readonly ITypeSymbol IntType = Compilation.GetSpecialType(SpecialType.System_Int32);
    private static readonly ITypeSymbol ShortType = Compilation.GetSpecialType(SpecialType.System_Int16);
    private static readonly ITypeSymbol UIntType = Compilation.GetSpecialType(SpecialType.System_UInt32);
    private static readonly ITypeSymbol ULongType = Compilation.GetSpecialType(SpecialType.System_UInt64);
    private static readonly ITypeSymbol ByteType = Compilation.GetSpecialType(SpecialType.System_Byte);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetBinaryHelper_Int32AddWithFullRanges_ReturnsTheAddHelper()
    {
        var facts = new IntegerBinaryFacts(
            BinaryOperatorKind.Add,
            IntType,
            IntType,
            IsBuiltIn: true,
            HasConstantValue: false,
            OperationRanges.OfType(IntType),
            OperationRanges.OfType(IntType));

        var helper = IntegerSemantics.GetBinaryHelper(facts);

        Assert.Equal(GSharpInternalNames.IntegerSemantics.AddInt32, helper);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetBinaryHelper_Int32AddOfThreadIndices_KeepsTheOperator()
    {
        var facts = new IntegerBinaryFacts(
            BinaryOperatorKind.Add,
            IntType,
            IntType,
            IsBuiltIn: true,
            HasConstantValue: false,
            new IntegerRange(0, 1023),
            new IntegerRange(0, 1023));

        var helper = IntegerSemantics.GetBinaryHelper(facts);

        Assert.Null(helper);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetBinaryHelper_Int32AddReachingTheMinimum_KeepsTheOperator()
    {
        var facts = new IntegerBinaryFacts(
            BinaryOperatorKind.Add,
            IntType,
            IntType,
            IsBuiltIn: true,
            HasConstantValue: false,
            new IntegerRange(-1073741824, 0),
            new IntegerRange(-1073741824, 0));

        var helper = IntegerSemantics.GetBinaryHelper(facts);

        Assert.Null(helper);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetBinaryHelper_Int32DivideByPositiveDivisor_KeepsTheOperator()
    {
        var facts = new IntegerBinaryFacts(
            BinaryOperatorKind.Divide,
            IntType,
            IntType,
            IsBuiltIn: true,
            HasConstantValue: false,
            OperationRanges.OfType(IntType),
            new IntegerRange(1, 1024));

        var helper = IntegerSemantics.GetBinaryHelper(facts);

        Assert.Null(helper);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetBinaryHelper_Int32DivideByAnyInt_ReturnsTheDivideHelper()
    {
        var facts = new IntegerBinaryFacts(
            BinaryOperatorKind.Divide,
            IntType,
            IntType,
            IsBuiltIn: true,
            HasConstantValue: false,
            OperationRanges.OfType(IntType),
            OperationRanges.OfType(IntType));

        var helper = IntegerSemantics.GetBinaryHelper(facts);

        Assert.Equal(GSharpInternalNames.IntegerSemantics.DivideInt32, helper);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetBinaryHelper_UserDefinedOperator_ReturnsNull()
    {
        var facts = new IntegerBinaryFacts(
            BinaryOperatorKind.Add,
            IntType,
            IntType,
            IsBuiltIn: false,
            HasConstantValue: false,
            OperationRanges.OfType(IntType),
            OperationRanges.OfType(IntType));

        var helper = IntegerSemantics.GetBinaryHelper(facts);

        Assert.Null(helper);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetShiftMask_Int32RightShift_Returns31()
    {
        var facts = new IntegerBinaryFacts(
            BinaryOperatorKind.RightShift,
            IntType,
            IntType,
            IsBuiltIn: true,
            HasConstantValue: false,
            Left: null,
            Right: null);

        var mask = IntegerSemantics.GetShiftMask(facts);

        Assert.Equal(31, mask);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetBinaryHelper_Int32LeftShift_ReturnsTheShiftLeftHelper()
    {
        var facts = new IntegerBinaryFacts(
            BinaryOperatorKind.LeftShift,
            IntType,
            IntType,
            IsBuiltIn: true,
            HasConstantValue: false,
            Left: null,
            Right: null);

        var helper = IntegerSemantics.GetBinaryHelper(facts);

        Assert.Equal(GSharpInternalNames.IntegerSemantics.ShiftLeftInt32, helper);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetNegationHelper_Int32WithFullRange_ReturnsTheNegateHelper()
    {
        var facts = new IntegerUnaryFacts(
            UnaryOperatorKind.Minus,
            IntType,
            IsBuiltIn: true,
            HasConstantValue: false,
            Operand: OperationRanges.OfType(IntType));

        var helper = IntegerSemantics.GetNegationHelper(facts);

        Assert.Equal(GSharpInternalNames.IntegerSemantics.NegateInt32, helper);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetNegationHelper_OperandAboveTheMinimum_ReturnsNull()
    {
        var facts = new IntegerUnaryFacts(
            UnaryOperatorKind.Minus,
            IntType,
            IsBuiltIn: true,
            HasConstantValue: false,
            Operand: new IntegerRange(0, 1023));

        var helper = IntegerSemantics.GetNegationHelper(facts);

        Assert.Null(helper);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetStepHelper_IncrementOfShort_ReturnsTheInt32AddHelper()
    {
        var facts = new IntegerStepFacts(
            IsIncrement: true,
            Type: ShortType,
            TargetType: ShortType,
            IsBuiltIn: true);

        var helper = IntegerSemantics.GetStepHelper(facts, undo: false);
        var undoHelper = IntegerSemantics.GetStepHelper(facts, undo: true);

        Assert.Equal(GSharpInternalNames.IntegerSemantics.AddInt32, helper);
        Assert.Equal(GSharpInternalNames.IntegerSemantics.SubtractInt32, undoHelper);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetStepHelper_IncrementOfUInt_ReturnsNull()
    {
        var facts = new IntegerStepFacts(
            IsIncrement: true,
            Type: UIntType,
            TargetType: UIntType,
            IsBuiltIn: true);

        var helper = IntegerSemantics.GetStepHelper(facts, undo: false);

        Assert.Null(helper);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetStepNarrowing_IncrementOfShort_ReturnsShort()
    {
        var facts = new IntegerStepFacts(
            IsIncrement: true,
            Type: ShortType,
            TargetType: ShortType,
            IsBuiltIn: true);

        var narrowing = IntegerSemantics.GetStepNarrowing(facts);

        Assert.True(SymbolEqualityComparer.Default.Equals(ShortType, narrowing));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetCompoundOperatorType_UIntPlusInt_ReturnsInt64()
    {
        var operatorType = IntegerSemantics.GetCompoundOperatorType(BinaryOperatorKind.Add, UIntType, IntType);

        Assert.Equal(SpecialType.System_Int64, operatorType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetCompoundHelper_ShortPlusInt_ReturnsTheAddHelperAndNarrowsToShort()
    {
        var facts = new IntegerCompoundFacts(
            BinaryOperatorKind.Add,
            TargetType: ShortType,
            ValueType: IntType,
            IsBuiltIn: true,
            Target: null,
            Value: null);

        var helper = IntegerSemantics.GetCompoundHelper(facts);
        var narrowing = IntegerSemantics.GetCompoundNarrowing(facts);

        Assert.Equal(GSharpInternalNames.IntegerSemantics.AddInt32, helper);
        Assert.True(SymbolEqualityComparer.Default.Equals(ShortType, narrowing));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetCompoundShiftMask_ULongShiftLeft_Returns63()
    {
        var facts = new IntegerCompoundFacts(
            BinaryOperatorKind.LeftShift,
            TargetType: ULongType,
            ValueType: IntType,
            IsBuiltIn: true,
            Target: null,
            Value: null);

        var mask = IntegerSemantics.GetCompoundShiftMask(facts);

        Assert.Equal(63, mask);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetConstant_ByteConstantOfNonEnumOperation_ReturnsNull()
    {
        var result =
            IntegerSemantics.GetConstant(ByteType, new Optional<object?>((byte)3), isNarrowEnumOperation: false);

        Assert.Null(result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetConstant_IntConstant_ReturnsTypeAndValue()
    {
        var result = IntegerSemantics.GetConstant(IntType, new Optional<object?>(6), isNarrowEnumOperation: false);

        Assert.NotNull(result);
        Assert.True(SymbolEqualityComparer.Default.Equals(IntType, result.Value.Type));
        Assert.Equal(6, result.Value.Value);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void DecideDivision_UnknownRanges_SpanTheOperatorType()
    {
        var check = IntegerSemantics.DecideDivision(
            (IntegerRange?)null,
            (IntegerRange?)null,
            SpecialType.System_Int32);

        Assert.Equal(DivisionCheck.Divisor, check);
    }
}
