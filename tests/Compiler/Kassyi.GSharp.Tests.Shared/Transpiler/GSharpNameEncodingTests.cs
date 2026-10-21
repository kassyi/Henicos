using Kassyi.GSharp.Naming;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>Verifies that renamed identifiers decode back to their C# names.</summary>
public class GSharpNameEncodingTests
{
    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("__gsharp_id_3_template", "template")]
    [InlineData("__gsharp_id_12_x1", "x1")]
    [InlineData("__gsharp_id_template", "template")]
    [InlineData("__gsharp_mb_Length", "Length")]
    [InlineData("__gsharp_id_0__Value", "_Value")]
    [InlineData("__gsharp_id__Value", "_Value")]
    public void Test_TryDecode_RenamedIdentifier_ReturnsCSharpName(string identifier, string expected)
    {
        Assert.True(GSharpNameEncoding.TryDecode(identifier, out var name));
        Assert.Equal(expected, name);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("__gsharp_ctx")]
    [InlineData("__gsharp_id_")]
    [InlineData("__gsharp_mb_")]
    [InlineData("data")]
    [InlineData("")]
    public void Test_TryDecode_OtherIdentifier_ReturnsFalse(string identifier)
    {
        Assert.False(GSharpNameEncoding.TryDecode(identifier, out var name));
        Assert.Equal(string.Empty, name);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DecodeText_ReplacesOnlyWholeRenamedIdentifiers()
    {
        const string Generated =
            "int __gsharp_id_0_data = item.__gsharp_mb_Length; __gsharp_ctx->__gsharp_id_4_union = a__gsharp_id_1_b;";

        Assert.Equal("int data = item.Length; __gsharp_ctx->union = a__gsharp_id_1_b;",
            GSharpNameEncoding.DecodeText(Generated));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DescribeRenamedIdentifiers_ListsEachIdentifierOnce()
    {
        const string NvccOutput =
            "error: identifier \"__gsharp_id_0_data\" is undefined\nerror: \"__gsharp_id_0_data\" redefined; see __gsharp_mb_register";

        Assert.Equal("__gsharp_id_0_data = data, __gsharp_mb_register = register",
            GSharpNameEncoding.DescribeRenamedIdentifiers(NvccOutput));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DescribeRenamedIdentifiers_WithoutRenamedIdentifiers_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, GSharpNameEncoding.DescribeRenamedIdentifiers("error C2065: 'x': undeclared"));
    }
}
