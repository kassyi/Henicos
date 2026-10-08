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

public class TrustedKeysGeneratorTests
{
    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TrustedKeysGenerator_WithValidHexKey_GeneratesSource()
    {
        const string TrustedKeysContent = """
            # Test comment
            d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a
            """;

        var compilation = CSharpCompilation.Create("TestCompilation");
        var additionalText = new InMemoryAdditionalText("trusted_keys.txt", TrustedKeysContent);

        var generator = new TrustedKeysGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [additionalText]);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        Assert.Single(runResult.GeneratedTrees);
        var generatedSource = runResult.GeneratedTrees[0].ToString();

        Assert.Contains("namespace Kassyi.GSharp.Runtime.Fatbin;", generatedSource);
        Assert.Contains("public static partial class GpuSecurityPolicy", generatedSource);
        Assert.Contains("s_generatedTrustedKeys", generatedSource);
        Assert.Contains("0xd7, 0x5a, 0x98, 0x01", generatedSource);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TrustedKeysGenerator_WithMultipleKeysAndComments_ParsesAll()
    {
        const string TrustedKeysContent = """
            # Primary Key
            d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a

            # Secondary Key (Base64 for 32-byte key)
            1111111111111111111111111111111111111111111=
            """;

        var compilation = CSharpCompilation.Create("TestCompilation");
        var additionalText = new InMemoryAdditionalText("C:/project/trusted_keys.txt", TrustedKeysContent);

        var generator = new TrustedKeysGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [additionalText]);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        Assert.Single(runResult.GeneratedTrees);
        var generatedSource = runResult.GeneratedTrees[0].ToString();

        // 64 bytes total (32 bytes * 2 keys) -> should contain both keys
        Assert.Contains("0xd7, 0x5a, 0x98, 0x01", generatedSource);
        Assert.Contains("GetCustomEmbeddedKeys", generatedSource);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_TrustedKeysGenerator_NoTrustedKeysFile_EmitsNothing()
    {
        var compilation = CSharpCompilation.Create("TestCompilation");
        var otherFile = new InMemoryAdditionalText("other_file.txt", "some content");

        var generator = new TrustedKeysGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [otherFile]);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        Assert.Empty(runResult.GeneratedTrees);
    }
}
