using Kassyi.GSharp.Compiler.Marshaling;
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
    [InlineData("__gsharp_gi_")]
    [InlineData("__gsharp_gi_Scale__Run")]
    [InlineData("__gsharp_gi_Scale_g1_bSystem__Int32")]
    [InlineData("data")]
    [InlineData("")]
    public void TryDecode_OtherIdentifier_ReturnsFalse(string identifier)
    {
        Assert.False(GSharpNameEncoding.TryDecode(identifier, out var name));
        Assert.Equal(string.Empty, name);
    }

    // What the demangle mode prints for a log that names generic instances: the entry point of a kernel instance, a
    // device function instance and the closed copy of the kernel on the host.
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void DecodeText_GenericInstances_ReplacesThemWithTheirTypeArguments()
    {
        const string Log = "fault in __gsharp_k_App__Kernels__Scale_g1_bSystem__Int32_e called from " +
                           "__gsharp_fn_App__Outer_g1_bApp__Vec_e__Kernels__Map_g1_bSystem__Single_r1_e_o2 " +
                           "(__gsharp_gi_Scale_g1_bSystem__Int32_e)";

        Assert.Equal("fault in App.Kernels.Scale<System.Int32> called from " +
                     "App.Outer<App.Vec>.Kernels.Map<System.Single[]>#2 (Scale<System.Int32>)",
            GSharpNameEncoding.DecodeText(Log));
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

    // The module context class and the module field name the module escaped injectively, and the demangle tool turns
    // them back into the module name, for every character an assembly or module name may hold
    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Henicos.Benchmarks")]
    [InlineData("Kassyi.GSharp.Tests.Cuda")]
    [InlineData("a_b_")]
    [InlineData("my-lib")]
    [InlineData("a{b}\"c")]
    [InlineData("été")]
    [InlineData("日本")]
    [InlineData("🚀")]
    [InlineData("1lib")]
    public void ModuleNames_EncodedThenDecoded_GiveTheModuleNameBack(string moduleName)
    {
        AssertModuleNamesRoundTrip(moduleName);
    }

    // The empty name (a [CudaImport] module named "") and lone UTF-16 surrogates, which the encoder escapes as their
    // code unit, decode as well; a surrogate pair stays one code point
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ModuleNames_EmptyOrLoneSurrogates_GiveTheModuleNameBack()
    {
        Assert.Equal("GSharpModuleContext_", GSharpNameEncoding.GetModuleContextName(""));
        Assert.Equal("GSharpModuleContext__x00d800", GSharpNameEncoding.GetModuleContextName("\uD800"));
        Assert.Equal("GSharpModuleContext_a_x00dc00b", GSharpNameEncoding.GetModuleContextName("a\uDC00b"));
        Assert.Equal("GSharpModuleContext__x01f680", GSharpNameEncoding.GetModuleContextName("🚀"));
        foreach (var moduleName in (string[])["", "\uD800", "a\uDC00b", "\uDC00\uD800", "\uD800x\uDBFF"])
        {
            AssertModuleNamesRoundTrip(moduleName);
        }
    }

    private static void AssertModuleNamesRoundTrip(string moduleName)
    {
        var contextName = GSharpNameEncoding.GetModuleContextName(moduleName);
        var fieldName = AotModuleNames.GetModuleFieldName(moduleName);

        Assert.True(SyntaxFacts.IsValidIdentifier(contextName), contextName);
        Assert.True(GSharpNameEncoding.TryDecode(contextName, out var decodedContext));
        Assert.Equal($"GSharpModuleContext({moduleName})", decodedContext);
        Assert.True(SyntaxFacts.IsValidIdentifier(fieldName), fieldName);
        Assert.True(GSharpNameEncoding.TryDecode(fieldName, out var decodedField));
        Assert.Equal($"__gsharp_module({moduleName})", decodedField);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void DecodeText_ModuleContextAndModuleField_ReplacesThemWithTheModuleName()
    {
        const string Log = "CS0103: GSharpModuleContext_Henicos_x00002eBenchmarks.GetOrLoadModule(); " +
            "__gsharp_module__x00002e_x00002fk_x00002efatbin.Value; MyGSharpModuleContext_a stays";
        const string Expected = "CS0103: GSharpModuleContext(Henicos.Benchmarks).GetOrLoadModule(); " +
            "__gsharp_module(./k.fatbin).Value; MyGSharpModuleContext_a stays";

        Assert.Equal(Expected, GSharpNameEncoding.DecodeText(Log));
        Assert.Equal(Expected, GSharpNameEncoding.DecodeText(Log, new Dictionary<string, string>()));
    }

    // A name in the replacing spelling of earlier versions ('_' for '.') is not an escaped name and stays as it is;
    // neither is a surrogate pair escaped as two code units, which would be a second spelling of the pair
    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("GSharpModuleContext__x00d83d_x00de80")]
    [InlineData("GSharpModuleContext_Henicos_Benchmarks")]
    [InlineData("GSharpModuleContext_a_x00002")]
    [InlineData("GSharpModuleContext__x000041")]
    public void TryDecode_NotAnEscapedModuleName_ReturnsFalse(string identifier)
    {
        Assert.False(GSharpNameEncoding.TryDecode(identifier, out var name));
        Assert.Equal(string.Empty, name);
    }
}
