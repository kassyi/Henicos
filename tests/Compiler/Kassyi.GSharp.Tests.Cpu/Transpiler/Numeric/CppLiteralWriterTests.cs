#nullable enable
using Kassyi.GSharp.Compiler.Backends.Cpp;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Numeric;

/// <summary>
/// Unit tests for <see cref="CppLiteralWriter.AppendNumeric"/> and <see cref="CppLiteralWriter.GetLiteralType"/>
/// (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class CppLiteralWriterTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendNumeric_LongWithSeparators_DropsThemAndWritesLl()
    {
        using var writer = new SourceWriter();
        CppLiteralWriter.AppendNumeric("1_000L", SpecialType.System_Int64, writer);

        Assert.Equal("1000ll", writer.ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendNumeric_ULongWithSuffix_WritesUll()
    {
        using var writer = new SourceWriter();
        CppLiteralWriter.AppendNumeric("5UL", SpecialType.System_UInt64, writer);

        Assert.Equal("5ull", writer.ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendNumeric_UnsuffixedDecimalUInt_AddsU()
    {
        using var writer = new SourceWriter();
        CppLiteralWriter.AppendNumeric("3000000000", SpecialType.System_UInt32, writer);

        Assert.Equal("3000000000u", writer.ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendNumeric_HexadecimalUInt_KeepsItWithoutSeparators()
    {
        using var writer = new SourceWriter();
        CppLiteralWriter.AppendNumeric("0xFFFF_FFFF", SpecialType.System_UInt32, writer);

        Assert.Equal("0xFFFFFFFF", writer.ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendNumeric_FloatWithoutFraction_AddsPointZeroF()
    {
        using var writer = new SourceWriter();
        CppLiteralWriter.AppendNumeric("2f", SpecialType.System_Single, writer);

        Assert.Equal("2.0f", writer.ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendNumeric_DoubleWithDSuffix_DropsIt()
    {
        using var writer = new SourceWriter();
        CppLiteralWriter.AppendNumeric("2d", SpecialType.System_Double, writer);

        Assert.Equal("2.0", writer.ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendNumeric_FloatWithExponent_KeepsIt()
    {
        using var writer = new SourceWriter();
        CppLiteralWriter.AppendNumeric("1e3f", SpecialType.System_Single, writer);

        Assert.Equal("1e3f", writer.ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetLiteralType_BoxedValues_MapToTheirSpecialTypes()
    {
        Assert.Equal(SpecialType.System_Int64, CppLiteralWriter.GetLiteralType(3L));
        Assert.Equal(SpecialType.System_UInt64, CppLiteralWriter.GetLiteralType(3UL));
        Assert.Equal(SpecialType.System_UInt32, CppLiteralWriter.GetLiteralType(3u));
        Assert.Equal(SpecialType.System_Single, CppLiteralWriter.GetLiteralType(3f));
        Assert.Equal(SpecialType.System_Double, CppLiteralWriter.GetLiteralType(3d));
        Assert.Equal(SpecialType.System_Int32, CppLiteralWriter.GetLiteralType(3));
        Assert.Equal(SpecialType.None, CppLiteralWriter.GetLiteralType(3m));
    }
}
