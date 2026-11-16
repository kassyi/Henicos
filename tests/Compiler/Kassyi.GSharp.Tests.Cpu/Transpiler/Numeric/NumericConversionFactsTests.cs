#nullable enable
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Literals;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Numeric;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Numeric;

/// <summary>
/// Unit tests for <see cref="ArithmeticConversions"/> and <see cref="ConstantAtUse"/> pure type functions (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class NumericConversionFactsTests
{
    private static readonly Compilation Compilation = RoslynTestUtility.CreateCompilation("");
    private static readonly ITypeSymbol IntType = Compilation.GetSpecialType(SpecialType.System_Int32);
    private static readonly ITypeSymbol LongType = Compilation.GetSpecialType(SpecialType.System_Int64);
    private static readonly ITypeSymbol UIntType = Compilation.GetSpecialType(SpecialType.System_UInt32);
    private static readonly ITypeSymbol ByteType = Compilation.GetSpecialType(SpecialType.System_Byte);
    private static readonly ITypeSymbol ObjectType = Compilation.GetSpecialType(SpecialType.System_Object);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TryGetPromotedType_UIntPlusIntConvertedToLong_ReturnsLong()
    {
        var promoted = ArithmeticConversions.TryGetPromotedType(
            BinaryOperatorKind.Add,
            isBuiltIn: true,
            left: LongType,
            right: LongType,
            leftSource: UIntType,
            rightSource: IntType,
            out var type);

        Assert.True(promoted);
        Assert.True(SymbolEqualityComparer.Default.Equals(LongType, type));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TryGetPromotedType_IntPlusInt_ReturnsFalse()
    {
        var promoted = ArithmeticConversions.TryGetPromotedType(
            BinaryOperatorKind.Add,
            isBuiltIn: true,
            left: IntType,
            right: IntType,
            leftSource: IntType,
            rightSource: IntType,
            out var type);

        Assert.False(promoted);
        Assert.Null(type);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TryGetPromotedType_NegatedUIntAsLong_ReturnsLong()
    {
        var promoted = ArithmeticConversions.TryGetPromotedType(
            UnaryOperatorKind.Minus,
            isBuiltIn: true,
            result: LongType,
            source: UIntType,
            out var type);

        Assert.True(promoted);
        Assert.True(SymbolEqualityComparer.Default.Equals(LongType, type));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TryGetPromotedConditionalType_UIntAndIntBranches_ReturnsLong()
    {
        var promoted = ArithmeticConversions.TryGetPromotedConditionalType(
            isRef: false,
            result: LongType,
            trueSource: UIntType,
            falseSource: IntType,
            out var type);

        Assert.True(promoted);
        Assert.True(SymbolEqualityComparer.Default.Equals(LongType, type));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TryGetPromotedConditionalType_RefConditional_ReturnsFalse()
    {
        var promoted = ArithmeticConversions.TryGetPromotedConditionalType(
            isRef: true,
            result: LongType,
            trueSource: UIntType,
            falseSource: IntType,
            out var type);

        Assert.False(promoted);
        Assert.Null(type);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetTypeAtUse_NumericConversionToInt_ReturnsInt()
    {
        var result = ConstantAtUse.GetTypeAtUse(
            ownType: ByteType,
            conversionTarget: IntType,
            isNumericOrConstantConversion: true);

        Assert.True(SymbolEqualityComparer.Default.Equals(IntType, result));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetTypeAtUse_NoConversion_ReturnsOwnType()
    {
        var result = ConstantAtUse.GetTypeAtUse(
            ownType: ByteType,
            conversionTarget: null,
            isNumericOrConstantConversion: false);

        Assert.True(SymbolEqualityComparer.Default.Equals(ByteType, result));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetTypeAtUse_ConversionToNonPrimitive_ReturnsOwnType()
    {
        var result = ConstantAtUse.GetTypeAtUse(
            ownType: ByteType,
            conversionTarget: ObjectType,
            isNumericOrConstantConversion: true);

        Assert.True(SymbolEqualityComparer.Default.Equals(ByteType, result));
    }
}
