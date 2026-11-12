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
    [InlineData("__gsharp_bf_Scale", "Scale.field")]
    [InlineData("__gsharp_id_0__Value", "_Value")]
    [InlineData("__gsharp_id__Value", "_Value")]
    public void TryDecode_RenamedIdentifier_ReturnsCSharpName(string identifier, string expected)
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
    [InlineData("__gsharp_bf_")]
    [InlineData("data")]
    [InlineData("")]
    public void TryDecode_OtherIdentifier_ReturnsFalse(string identifier)
    {
        Assert.False(GSharpNameEncoding.TryDecode(identifier, out var name));
        Assert.Equal(string.Empty, name);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void DecodeText_PartialIdentifierMatches_ReplacesOnlyWholeRenamedIdentifiers()
    {
        const string Generated =
            "int __gsharp_id_0_data = item.__gsharp_mb_Length; __gsharp_ctx->__gsharp_id_4_union = a__gsharp_id_1_b;";

        Assert.Equal("int data = item.Length; __gsharp_ctx->union = a__gsharp_id_1_b;",
            GSharpNameEncoding.DecodeText(Generated));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void DescribeRenamedIdentifiers_MultipleOccurrences_ListsEachIdentifierOnce()
    {
        const string NvccOutput =
            "error: identifier \"__gsharp_id_0_data\" is undefined\nerror: \"__gsharp_id_0_data\" redefined; see __gsharp_mb_register";

        Assert.Equal("__gsharp_id_0_data = data, __gsharp_mb_register = register",
            GSharpNameEncoding.DescribeRenamedIdentifiers(NvccOutput));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void DescribeRenamedIdentifiers_WithoutRenamedIdentifiers_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, GSharpNameEncoding.DescribeRenamedIdentifiers("error C2065: 'x': undeclared"));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void DecodeText_WithSymbolMap_DecodesShortenedAndStandardIdentifiers()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["__gsharp_ty_VeryLongType_h0123456789"] = "VeryLongType"
        };

        const string Log = "error: in __gsharp_ty_VeryLongType_h0123456789 and __gsharp_id_0_data";
        var decoded = GSharpNameEncoding.DecodeText(Log, map);

        Assert.Equal("error: in VeryLongType and data", decoded);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void ParseSymbolFile_MismatchedHash_ThrowsInvalidOperationException()
    {
        const string GsymContent = """
            {"gsharp_symbols":"0123456789abcdef","dialect":"Cuda"}
            {"cpp":"__gsharp_id_0_data","kind":"id","cs":"data","symbols":"0123456789abcdef","dialect":"Cuda"}
            """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            GSharpNameEncoding.ParseSymbolFile(GsymContent, expectedHash: "fedcba9876543210"));

        Assert.Contains("Symbol file hash '0123456789abcdef' does not match source hash 'fedcba9876543210'.", ex.Message);
    }

    // The module context class and the fatbin field name the module escaped injectively, and the demangle tool turns
    // them back into the module name, for every character an assembly or file name may hold
    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Henicos.Benchmarks")]
    [InlineData("Kassyi.GSharp.Tests.Cuda")]
    [InlineData("a_b_")]
    [InlineData("my-lib")]
    [InlineData("./native/my-custom.kernels.fatbin")]
    [InlineData("a{b}\"c")]
    [InlineData("été")]
    [InlineData("日本")]
    [InlineData("🚀")]
    [InlineData("1lib")]
    public void ModuleNames_EncodedThenDecoded_GiveTheModuleNameBack(string moduleName)
    {
        var contextName = GSharpNameEncoding.GetModuleContextName(moduleName);
        string fieldName;
        using (var writer = new SourceWriter())
        {
            GSharpNameEncoding.AppendFatbinFieldName(writer, moduleName);
            fieldName = writer.ToString();
        }

        Assert.True(SyntaxFacts.IsValidIdentifier(contextName), contextName);
        Assert.True(SyntaxFacts.IsValidIdentifier(fieldName), fieldName);
        Assert.True(GSharpNameEncoding.TryDecode(contextName, out var decodedContext));
        Assert.Equal($"GSharpModuleContext({moduleName})", decodedContext);
        Assert.True(GSharpNameEncoding.TryDecode(fieldName, out var decodedField));
        Assert.Equal($"s_fatbin({moduleName})", decodedField);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void DecodeText_ModuleContextAndFatbinField_ReplacesBothWithTheModuleName()
    {
        const string Log = "CS0103: GSharpModuleContext_Henicos_x00002eBenchmarks and s_fatbin_a_x00002db.Value; " +
            "MyGSharpModuleContext_a stays";
        const string Expected =
            "CS0103: GSharpModuleContext(Henicos.Benchmarks) and s_fatbin(a-b).Value; MyGSharpModuleContext_a stays";

        Assert.Equal(Expected, GSharpNameEncoding.DecodeText(Log));
        Assert.Equal(Expected, GSharpNameEncoding.DecodeText(Log, new Dictionary<string, string>()));
    }

    // A name in the replacing spelling of earlier versions ('_' for '.') is not an escaped name and stays as it is
    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("GSharpModuleContext_")]
    [InlineData("GSharpModuleContext_Henicos_Benchmarks")]
    [InlineData("s_fatbin_a_x00002")]
    [InlineData("s_fatbin__x000041")]
    public void TryDecode_NotAnEscapedModuleName_ReturnsFalse(string identifier)
    {
        Assert.False(GSharpNameEncoding.TryDecode(identifier, out var name));
        Assert.Equal(string.Empty, name);
    }
}
