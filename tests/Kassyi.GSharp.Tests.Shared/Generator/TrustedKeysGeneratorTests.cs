#nullable enable

using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Kassyi.GSharp.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Generator;

[Category(TestCategory.Compiler)]
public class TrustedKeysGeneratorTests
{
    // Leading bytes of Convert.FromBase64String("1111111111111111111111111111111111111111111="); '1' decodes to the repeating 0xd7 0x5d 0x75.
    private const string Base64KeyLeadingBytes = "0xd7, 0x5d, 0x75, 0xd7";

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]    public void Test_TrustedKeysGenerator_WithValidHexKey_GeneratesSource()
    {
        const string TrustedKeysContent = """
            # Test comment
            d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a,fnv1a64:5553fd74df
            """;

        var compilation = CSharpCompilation.Create("TestCompilation");
        var additionalText = new InMemoryAdditionalText("trusted_keys.txt", TrustedKeysContent);

        var generator = new TrustedKeysGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [additionalText]);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        Assert.Empty(runResult.Diagnostics);
        Assert.Single(runResult.GeneratedTrees);
        var generatedSource = runResult.GeneratedTrees[0].ToString();

        Assert.Contains("namespace Kassyi.GSharp.Runtime.Fatbin;", generatedSource);
        Assert.Contains("public static partial class GpuSecurityPolicy", generatedSource);
        Assert.Contains("s_generatedTrustedKeys", generatedSource);
        Assert.Contains("0xd7, 0x5a, 0x98, 0x01", generatedSource);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]    public void Test_TrustedKeysGenerator_WithMultipleKeysAndComments_ParsesAll()
    {
        const string TrustedKeysContent = """
            # Primary Key
            d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a,fnv1a64:5553fd74df

            # Secondary Key (Base64 for 32-byte key)
            1111111111111111111111111111111111111111111=,fnv1a64:25c9e1ea82
            """;

        var compilation = CSharpCompilation.Create("TestCompilation");
        var additionalText = new InMemoryAdditionalText("C:/project/trusted_keys.txt", TrustedKeysContent);

        var generator = new TrustedKeysGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [additionalText]);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        Assert.Empty(runResult.Diagnostics);
        Assert.Single(runResult.GeneratedTrees);
        var generatedSource = runResult.GeneratedTrees[0].ToString();

        // 64 bytes total (32 bytes * 2 keys) -> should contain both keys
        Assert.Contains("0xd7, 0x5a, 0x98, 0x01", generatedSource);
        Assert.Contains(Base64KeyLeadingBytes, generatedSource);
        Assert.Contains("GetCustomEmbeddedKeys", generatedSource);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]    public void Test_TrustedKeysGenerator_NoTrustedKeysFile_EmitsNothing()
    {
        var compilation = CSharpCompilation.Create("TestCompilation");
        var otherFile = new InMemoryAdditionalText("other_file.txt", "some content");

        var generator = new TrustedKeysGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [otherFile]);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        Assert.Empty(runResult.Diagnostics);
        Assert.Empty(runResult.GeneratedTrees);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]    public void Test_TrustedKeysGenerator_InvalidBase64_ReportsGSH0059()
    {
        const string TrustedKeysContent = """
            # Valid comment
            AAAA!!notbase64==
            """;

        const string FilePath = "C:/project/trusted_keys.txt";
        var compilation = CSharpCompilation.Create("TestCompilation");
        var additionalText = new InMemoryAdditionalText(FilePath, TrustedKeysContent);

        var generator = new TrustedKeysGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [additionalText]);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        var diag = Assert.Single(runResult.Diagnostics);
        Assert.Equal("GSH0059", diag.Id);
        var lineSpan = diag.Location.GetLineSpan();
        Assert.Equal(FilePath, lineSpan.Path);
        Assert.Equal(1, lineSpan.StartLinePosition.Line);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]    public void Test_TrustedKeysGenerator_Base64WrongLength_ReportsGSH0059()
    {
        const string TrustedKeysContent = "11111111111111111111111=";

        var compilation = CSharpCompilation.Create("TestCompilation");
        var additionalText = new InMemoryAdditionalText("trusted_keys.txt", TrustedKeysContent);

        var generator = new TrustedKeysGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [additionalText]);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        var diag = Assert.Single(runResult.Diagnostics);
        Assert.Equal("GSH0059", diag.Id);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]    public void Test_TrustedKeysGenerator_InvalidHex_ReportsGSH0059()
    {
        const string TrustedKeysContent = """
            d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511
            d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511g
            """;

        var compilation = CSharpCompilation.Create("TestCompilation");
        var additionalText = new InMemoryAdditionalText("trusted_keys.txt", TrustedKeysContent);

        var generator = new TrustedKeysGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [additionalText]);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        Assert.Equal(2, runResult.Diagnostics.Length);
        Assert.All(runResult.Diagnostics, diag => Assert.Equal("GSH0059", diag.Id));
        Assert.Equal(0, runResult.Diagnostics[0].Location.GetLineSpan().StartLinePosition.Line);
        Assert.Equal(1, runResult.Diagnostics[1].Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]    public void Test_TrustedKeysGenerator_MixedContentWithMalformedLine_EmitsDiagnosticAndValidKeys()
    {
        const string TrustedKeysContent = """
            # Primary Key
            d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a,fnv1a64:5553fd74df

            # Bad Key
            AAAA!!notbase64==

            # Secondary Key (Base64)
            1111111111111111111111111111111111111111111=,fnv1a64:25c9e1ea82
            """;

        var compilation = CSharpCompilation.Create("TestCompilation");
        var additionalText = new InMemoryAdditionalText("C:/project/trusted_keys.txt", TrustedKeysContent);

        var generator = new TrustedKeysGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [additionalText]);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        var diag = Assert.Single(runResult.Diagnostics);
        Assert.Equal("GSH0059", diag.Id);
        Assert.Equal(4, diag.Location.GetLineSpan().StartLinePosition.Line);

        Assert.Single(runResult.GeneratedTrees);
        var generatedSource = runResult.GeneratedTrees[0].ToString();
        Assert.Contains("0xd7, 0x5a, 0x98, 0x01", generatedSource);
        Assert.Contains(Base64KeyLeadingBytes, generatedSource);
        Assert.Contains("GetCustomEmbeddedKeys", generatedSource);
    }

    private static ImmutableArray<Diagnostic> RunDiagnostics(string content)
    {
        var compilation = CSharpCompilation.Create("TestCompilation");
        var additionalText = new InMemoryAdditionalText("trusted_keys.txt", content);

        var generator = new TrustedKeysGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [additionalText]);

        return driver.RunGenerators(compilation).GetRunResult().Diagnostics;
    }

    [Theory]
    [InlineData(",")]
    [InlineData(" , ")]
    [InlineData("	,	")]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TrustedKeysGenerator_FingerprintSeparatedByComma_Accepted(string separator)
    {
        var content = "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a" + separator + "fnv1a64:5553fd74df";

        Assert.Empty(RunDiagnostics(content));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TrustedKeysGenerator_HexAndBase64FormsOfSameKey_ShareOneFingerprint()
    {
        // RFC 8032 Section 7.1 Test Vector 1 in both encodings; the fingerprint is computed over normalized lowercase hex.
        const string Content = """
            d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a,fnv1a64:5553fd74df
            11qYAYKxCrfVS/7TyWQHOg7hcvPapiMlrwIaaPcHURo=,fnv1a64:5553fd74df
            """;

        Assert.Empty(RunDiagnostics(Content));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TrustedKeysGenerator_UppercaseHexKey_UsesNormalizedLowercaseFingerprint()
    {
        const string Content = "D75A980182B10AB7D54BFED3C964073A0EE172F3DAA62325AF021A68F707511A,fnv1a64:5553fd74df";

        Assert.Empty(RunDiagnostics(Content));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_TrustedKeysGenerator_WhitespaceInsteadOfComma_ReportsGSH0059()
    {
        // Whitespace is not a valid key/fingerprint separator.
        var diag = Assert.Single(RunDiagnostics(
            "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a  fnv1a64:5553fd74df"));

        Assert.Equal("GSH0059", diag.Id);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_TrustedKeysGenerator_MissingFingerprint_ReportsGSH0059()
    {
        var diag = Assert.Single(RunDiagnostics("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a"));

        Assert.Equal("GSH0059", diag.Id);
        Assert.Contains("missing fingerprint", diag.GetMessage());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_TrustedKeysGenerator_MismatchedFingerprint_ReportsGSH0059WithoutExpectedValue()
    {
        // One hex character of the key differs from the fingerprinted key (typo).
        const string Content = "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511b,fnv1a64:5553fd74df";

        var diag = Assert.Single(RunDiagnostics(Content));

        Assert.Equal("GSH0059", diag.Id);
        var message = diag.GetMessage();
        Assert.Contains("fingerprint mismatch", message);
        // The expected fingerprint must never be printed, otherwise users could paste it without checking the key.
        var expected = Kassyi.GSharp.Compiler.Helpers.HashHelper.ComputeFnv1A64ShortHash(
            "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511b");
        Assert.DoesNotContain(expected, message);
    }

    [Theory]
    [InlineData("fnv1a64:5553fd74d")]
    [InlineData("fnv1a64:")]
    [InlineData("fnv1a64:5553FD74DF")]
    [InlineData("sha256:5553fd74df")]
    [InlineData("5553fd74df")]
    [Scenario(TestScenario.Boundary)]
    public void Test_TrustedKeysGenerator_NonCanonicalFingerprintSuffix_ReportsGSH0059(string suffix)
    {
        var content = "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a," + suffix;

        var diag = Assert.Single(RunDiagnostics(content));

        Assert.Equal("GSH0059", diag.Id);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_TrustedKeysGenerator_MalformedKeyWithFingerprint_ReportsKeyFormatReason()
    {
        var diag = Assert.Single(RunDiagnostics("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511g,fnv1a64:5553fd74df"));

        Assert.Equal("GSH0059", diag.Id);
        Assert.Contains("invalid character", diag.GetMessage());
    }

    [Theory]
    // RFC 8032 Section 7.1 Test Vectors 1-3 public keys
    [InlineData("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a,fnv1a64:5553fd74df")]
    [InlineData("3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c,fnv1a64:b7819df3c0")]
    [InlineData("fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025,fnv1a64:07d9f356d7")]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TrustedKeysGenerator_Rfc8032PublicKeys_Accepted(string line)
    {
        Assert.Empty(RunDiagnostics(line));
    }

    [Theory]
    // All 8 small-order points (canonical encodings), each with a matching fingerprint.
    [InlineData("0100000000000000000000000000000000000000000000000000000000000000,fnv1a64:a05e7487b0")]
    [InlineData("ecffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f,fnv1a64:3a25be95a7")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000,fnv1a64:68497138cf")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000080,fnv1a64:682e8138ce")]
    [InlineData("26e8958fc2b227b045c3f489f2ef98f0d5dfac05d3c63339b13802886d53fc05,fnv1a64:6cae59afea")]
    [InlineData("26e8958fc2b227b045c3f489f2ef98f0d5dfac05d3c63339b13802886d53fc85,fnv1a64:6cc949afea")]
    [InlineData("c7176a703d4dd84fba3c0b760d10670f2a2053fa2c39ccc64ec7fd7792ac037a,fnv1a64:b2fcb037d2")]
    [InlineData("c7176a703d4dd84fba3c0b760d10670f2a2053fa2c39ccc64ec7fd7792ac03fa,fnv1a64:b3a3b437d3")]
    [Scenario(TestScenario.SadPath)]
    public void Test_TrustedKeysGenerator_SmallOrderKey_ReportsGSH0059(string line)
    {
        var diag = Assert.Single(RunDiagnostics(line));

        Assert.Equal("GSH0059", diag.Id);
        Assert.Contains("small-order Ed25519 public key", diag.GetMessage());
    }

    [Theory]
    // Non-canonical y (y = p, y = p + 1, y = 2^255 - 1)
    [InlineData("edffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f,fnv1a64:e30ce14a7b")]
    [InlineData("eeffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f,fnv1a64:ed80e09ecb")]
    [InlineData("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f,fnv1a64:d61789ba55")]
    // y = 2 has no square root for x^2
    [InlineData("0200000000000000000000000000000000000000000000000000000000000000,fnv1a64:0345d332d3")]
    // 0xAB repeated: well-formed but not on the curve
    [InlineData("abababababababababababababababababababababababababababababababab,fnv1a64:544cc8e1f3")]
    [Scenario(TestScenario.SadPath)]
    public void Test_TrustedKeysGenerator_NotDecompressibleKey_ReportsGSH0059(string line)
    {
        var diag = Assert.Single(RunDiagnostics(line));

        Assert.Equal("GSH0059", diag.Id);
        Assert.Contains("not a valid Ed25519 public key", diag.GetMessage());
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_TrustedKeysGenerator_TypoInKey_ReportsFingerprintMismatchBeforePointValidation()
    {
        // A typo is the likely cause, so the fingerprint is checked before the curve point.
        var diag = Assert.Single(RunDiagnostics(
            "0200000000000000000000000000000000000000000000000000000000000000,fnv1a64:5553fd74df"));

        Assert.Contains("fingerprint mismatch", diag.GetMessage());
    }
}
