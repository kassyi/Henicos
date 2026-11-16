#nullable enable
using Kassyi.GSharp.Compiler.Transpiler;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Numeric;

/// <summary>
/// Unit tests for <see cref="LiteralTranspiler.AppendNumeric"/> and <see cref="LiteralTranspiler.GetLiteralType"/>
/// (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class LiteralTranspilerTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendNumeric_LongWithSeparators_DropsThemAndWritesLl()
    {
        var writer = new SourceWriter();
        LiteralTranspiler.AppendNumeric("1_000L", SpecialType.System_Int64, writer);

        Assert.Equal("1000ll", writer.ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendNumeric_ULongWithSuffix_WritesUll()
    {
        var writer = new SourceWriter();
        LiteralTranspiler.AppendNumeric("5UL", SpecialType.System_UInt64, writer);

        Assert.Equal("5ull", writer.ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendNumeric_UnsuffixedDecimalUInt_AddsU()
    {
        var writer = new SourceWriter();
        LiteralTranspiler.AppendNumeric("3000000000", SpecialType.System_UInt32, writer);

        Assert.Equal("3000000000u", writer.ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendNumeric_HexadecimalUInt_KeepsItWithoutSeparators()
    {
        var writer = new SourceWriter();
        LiteralTranspiler.AppendNumeric("0xFFFF_FFFF", SpecialType.System_UInt32, writer);

        Assert.Equal("0xFFFFFFFF", writer.ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendNumeric_FloatWithoutFraction_AddsPointZeroF()
    {
        var writer = new SourceWriter();
        LiteralTranspiler.AppendNumeric("2f", SpecialType.System_Single, writer);

        Assert.Equal("2.0f", writer.ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendNumeric_DoubleWithDSuffix_DropsIt()
    {
        var writer = new SourceWriter();
        LiteralTranspiler.AppendNumeric("2d", SpecialType.System_Double, writer);

        Assert.Equal("2.0", writer.ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendNumeric_FloatWithExponent_KeepsIt()
    {
        var writer = new SourceWriter();
        LiteralTranspiler.AppendNumeric("1e3f", SpecialType.System_Single, writer);

        Assert.Equal("1e3f", writer.ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetLiteralType_BoxedValues_MapToTheirSpecialTypes()
    {
        Assert.Equal(SpecialType.System_Int64, LiteralTranspiler.GetLiteralType(3L));
        Assert.Equal(SpecialType.System_UInt64, LiteralTranspiler.GetLiteralType(3UL));
        Assert.Equal(SpecialType.System_UInt32, LiteralTranspiler.GetLiteralType(3u));
        Assert.Equal(SpecialType.System_Single, LiteralTranspiler.GetLiteralType(3f));
        Assert.Equal(SpecialType.System_Double, LiteralTranspiler.GetLiteralType(3d));
        Assert.Equal(SpecialType.System_Int32, LiteralTranspiler.GetLiteralType(3));
        Assert.Equal(SpecialType.None, LiteralTranspiler.GetLiteralType(3m));
    }
}
