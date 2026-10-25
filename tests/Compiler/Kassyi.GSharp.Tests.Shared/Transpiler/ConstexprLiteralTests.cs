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

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Format_UnsupportedType_ThrowsArgumentOutOfRangeException()
    {
        var stringType = s_compilation.GetSpecialType(SpecialType.System_String);

        Assert.Throws<ArgumentOutOfRangeException>(() => ConstexprLiteral.Format(stringType, "abc"));
    }
}
