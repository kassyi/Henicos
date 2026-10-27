using System.Globalization;
using Kassyi.GSharp.Compiler.Transpiler.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>Verifies C++ and C# literal formatting for <c>[GpuConstexpr]</c> parameter specialization values.</summary>
[Category(TestCategory.Compiler)]
public class ConstexprLiteralTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Transpiler/ConstexprLiteralSource.cs")
    );

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(SpecialType.System_Boolean, true, "true", "true")]
    [InlineData(SpecialType.System_Boolean, false, "false", "false")]
    [InlineData(SpecialType.System_SByte, (sbyte)-5, "-5", "-5")]
    [InlineData(SpecialType.System_Int16, (short)-5, "-5", "-5")]
    [InlineData(SpecialType.System_Int32, -5, "-5", "-5")]
    [InlineData(SpecialType.System_Byte, (byte)5, "5", "5")]
    [InlineData(SpecialType.System_UInt16, (ushort)5, "5", "5")]
    [InlineData(SpecialType.System_UInt32, 5U, "5U", "5U")]
    [InlineData(SpecialType.System_Int64, 5L, "5LL", "5L")]
    [InlineData(SpecialType.System_UInt64, 5UL, "5ULL", "5UL")]
    [InlineData(SpecialType.System_Single, 1.0f, "1.0f", "1F")]
    [InlineData(SpecialType.System_Single, 1e+30f, "1e+30f", "1e+30F")]
    [InlineData(SpecialType.System_Single, 0.1f, "0.1f", "0.1F")]
    [InlineData(SpecialType.System_Double, 0.1, "0.1", "0.1D")]
    [InlineData(SpecialType.System_Double, 1.0, "1.0", "1D")]
    public void Test_Format_PrimitiveTableRows(
        SpecialType specialType,
        object value,
        string expectedCpp,
        string expectedCSharp)
    {
        var type = s_compilation.GetSpecialType(specialType);

        var (cpp, csharp) = ConstexprLiteral.Format(type, value);

        Assert.Equal(expectedCpp, cpp);
        Assert.Equal(expectedCSharp, csharp);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(SpecialType.System_Int32, int.MinValue, "(-2147483647-1)", "-2147483648")]
    [InlineData(SpecialType.System_Int64, long.MinValue, "(-9223372036854775807LL-1)", "-9223372036854775808L")]
    [InlineData(SpecialType.System_UInt64, ulong.MaxValue, "18446744073709551615ULL", "18446744073709551615UL")]
    [InlineData(SpecialType.System_Single, 0.0f, "0.0f", "0F")]
    [InlineData(SpecialType.System_Single, -0.0f, "-0.0f", "-0F")]
    [InlineData(SpecialType.System_Single, 3.1415927f, "3.1415927f", "3.1415927F")]
    [InlineData(SpecialType.System_Single, 0.100000024f, "0.100000024f", "0.100000024F")]
    [InlineData(SpecialType.System_Single, float.MaxValue, "3.4028235e+38f", "3.4028235e+38F")]
    [InlineData(SpecialType.System_Double, 0.0, "0.0", "0D")]
    [InlineData(SpecialType.System_Double, -0.0, "-0.0", "-0D")]
    [InlineData(SpecialType.System_Double, Math.PI, "3.141592653589793", "3.141592653589793D")]
    [InlineData(SpecialType.System_Double, double.MaxValue, "1.7976931348623157e+308", "1.7976931348623157e+308D")]
    [InlineData(SpecialType.System_Double, double.Epsilon, "5e-324", "5e-324D")]
    [InlineData(SpecialType.System_Single, float.Epsilon, "1e-45f", "1e-45F")]
    public void Test_Format_BoundaryValues(
        SpecialType specialType,
        object value,
        string expectedCpp,
        string expectedCSharp)
    {
        var type = s_compilation.GetSpecialType(specialType);

        var (cpp, csharp) = ConstexprLiteral.Format(type, value);

        Assert.Equal(expectedCpp, cpp);
        Assert.Equal(expectedCSharp, csharp);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Ns.Mode", 1, "1", "(global::Ns.Mode)1")]
    [InlineData("Ns.Mode", -1, "-1", "(global::Ns.Mode)(-1)")]
    [InlineData("Ns.UIntMode", 5U, "5U", "(global::Ns.UIntMode)5U")]
    public void Test_Format_EnumValues(
        string metadataName,
        object value,
        string expectedCpp,
        string expectedCSharp)
    {
        var enumType = s_compilation.GetTypeByMetadataName(metadataName)
                       ?? throw new InvalidOperationException(metadataName);

        var (cpp, csharp) = ConstexprLiteral.Format(enumType, value);

        Assert.Equal(expectedCpp, cpp);
        Assert.Equal(expectedCSharp, csharp);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Format_UnderGermanCulture_UsesInvariantDecimalPoint()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var floatType = s_compilation.GetSpecialType(SpecialType.System_Single);
            var doubleType = s_compilation.GetSpecialType(SpecialType.System_Double);

            Assert.Equal(("0.1f", "0.1F"), ConstexprLiteral.Format(floatType, 0.1f));
            Assert.Equal(("1.0f", "1F"), ConstexprLiteral.Format(floatType, 1.0f));
            Assert.Equal(("1e+30f", "1e+30F"), ConstexprLiteral.Format(floatType, 1e+30f));
            Assert.Equal(("-0.0f", "-0F"), ConstexprLiteral.Format(floatType, -0.0f));
            Assert.Equal(("0.1", "0.1D"), ConstexprLiteral.Format(doubleType, 0.1));
            Assert.Equal(("1.0", "1D"), ConstexprLiteral.Format(doubleType, 1.0));
            Assert.Equal(("-0.0", "-0D"), ConstexprLiteral.Format(doubleType, -0.0));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(SpecialType.System_Single, float.NaN)]
    [InlineData(SpecialType.System_Single, float.PositiveInfinity)]
    [InlineData(SpecialType.System_Single, float.NegativeInfinity)]
    [InlineData(SpecialType.System_Double, double.NaN)]
    [InlineData(SpecialType.System_Double, double.PositiveInfinity)]
    [InlineData(SpecialType.System_Double, double.NegativeInfinity)]
    public void Test_Format_NonFiniteFloatingPoint_ThrowsArgumentOutOfRangeException(
        SpecialType specialType,
        object value)
    {
        var type = s_compilation.GetSpecialType(specialType);

        Assert.Throws<ArgumentOutOfRangeException>(() => ConstexprLiteral.Format(type, value));
    }

    // A const field or a default is not a constexpr local: NaN and infinity come from the Prelude, a char is a number,
    // and a negative value is parenthesized so that it cannot merge with a preceding - or + operator
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(SpecialType.System_Single, float.NaN, "__gsharp_nan_f", "float.NaN")]
    [InlineData(SpecialType.System_Single, float.PositiveInfinity, "__gsharp_inf_f", "float.PositiveInfinity")]
    [InlineData(SpecialType.System_Single, float.NegativeInfinity, "(-__gsharp_inf_f)", "float.NegativeInfinity")]
    [InlineData(SpecialType.System_Double, double.NaN, "__gsharp_nan_d", "double.NaN")]
    [InlineData(SpecialType.System_Double, double.PositiveInfinity, "__gsharp_inf_d", "double.PositiveInfinity")]
    [InlineData(SpecialType.System_Double, double.NegativeInfinity, "(-__gsharp_inf_d)", "double.NegativeInfinity")]
    [InlineData(SpecialType.System_Single, 2.0f, "2.0f", "2F")]
    [InlineData(SpecialType.System_Int32, int.MinValue, "(-2147483647-1)", "-2147483648")]
    [InlineData(SpecialType.System_Int32, -5, "(-5)", "-5")]
    [InlineData(SpecialType.System_Int64, -7L, "(-7LL)", "-7L")]
    [InlineData(SpecialType.System_Single, -0.0f, "(-0.0f)", "-0F")]
    [InlineData(SpecialType.System_Double, -1.5, "(-1.5)", "-1.5D")]
    [InlineData(SpecialType.System_Char, 'A', "(char16_t)65", "'A'")]
    [InlineData(SpecialType.System_Char, '\0', "(char16_t)0", @"'\0'")]
    [InlineData(SpecialType.System_Char, '\n', "(char16_t)10", @"'\n'")]
    [InlineData(SpecialType.System_Char, '\'', "(char16_t)39", @"'\''")]
    [InlineData(SpecialType.System_Char, '\\', "(char16_t)92", @"'\\'")]
    [InlineData(SpecialType.System_Char, '\u00E9', "(char16_t)233", "'\u00E9'")]
    [InlineData(SpecialType.System_Char, '\uffff', "(char16_t)65535", @"'\uffff'")]
    [InlineData(SpecialType.System_Char, '\uD800', "(char16_t)55296", @"'\ud800'")]
    public void Test_TryFormatConstant_FormatsConstants(
        SpecialType specialType,
        object value,
        string expectedCpp,
        string expectedCSharp)
    {
        var type = s_compilation.GetSpecialType(specialType);

        Assert.True(ConstexprLiteral.TryFormatConstant(type, value, out var literal));
        Assert.Equal((expectedCpp, expectedCSharp), literal);
    }

    // Nullable<T> is formatted as T: the default of a float? parameter is a float
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_TryFormatConstant_UnwrapsNullable()
    {
        var nullable = s_compilation.GetSpecialType(SpecialType.System_Nullable_T);
        var nullableSingle = nullable.Construct(s_compilation.GetSpecialType(SpecialType.System_Single));
        var nullableChar = nullable.Construct(s_compilation.GetSpecialType(SpecialType.System_Char));

        Assert.True(ConstexprLiteral.TryFormatConstant(nullableSingle, float.NaN, out var nan));
        Assert.Equal(("__gsharp_nan_f", "float.NaN"), nan);
        Assert.True(ConstexprLiteral.TryFormatConstant(nullableChar, 'A', out var letter));
        Assert.Equal(("(char16_t)65", "'A'"), letter);
    }

    // char is not a [GpuConstexpr] parameter type
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Format_Char_ThrowsArgumentOutOfRangeException()
    {
        var charType = s_compilation.GetSpecialType(SpecialType.System_Char);

        Assert.Throws<ArgumentOutOfRangeException>(() => ConstexprLiteral.Format(charType, 'A'));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_TryFormatConstant_DecimalAndString_HaveNoLiteral()
    {
        Assert.False(ConstexprLiteral.TryFormatConstant(s_compilation.GetSpecialType(SpecialType.System_Decimal), 1.5m, out _));
        Assert.False(ConstexprLiteral.TryFormatConstant(s_compilation.GetSpecialType(SpecialType.System_String), "abc", out _));
    }

    // Without a device literal the transpiler writes the invariant text; the analyzer reports the use (GSH0003)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_FormatCppConstant_FallsBackToInvariantText()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("1.5", ConstexprLiteral.FormatCppConstant(s_compilation.GetSpecialType(SpecialType.System_Decimal), 1.5m));
            Assert.Equal("nullptr", ConstexprLiteral.FormatCppConstant(s_compilation.GetSpecialType(SpecialType.System_String), null));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Format_UnsupportedType_ThrowsArgumentOutOfRangeException()
    {
        var stringType = s_compilation.GetSpecialType(SpecialType.System_String);

        Assert.Throws<ArgumentOutOfRangeException>(() => ConstexprLiteral.Format(stringType, "abc"));
    }
}
