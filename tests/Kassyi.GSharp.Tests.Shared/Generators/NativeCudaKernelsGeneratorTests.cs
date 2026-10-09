using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using VerifyXunit;
using System.IO;

namespace Kassyi.GSharp.Tests.Shared.Generators;

[Category(TestCategory.Compiler)]
public class NativeCudaKernelsGeneratorTests
{
    private class FileAdditionalText : AdditionalText
    {
        private readonly string _path;
        public override string Path => _path;

        public FileAdditionalText(string path)
        {
            _path = path;
        }

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(File.ReadAllText(_path));
    }

    private FileAdditionalText GetAdditionalText(string relativeFilePath)
    {
        var basePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
            "../../../../../tests/Kassyi.GSharp.Tests.Shared/TestCases/Generators"));
        return new FileAdditionalText(System.IO.Path.Combine(basePath, relativeFilePath));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_GeneratesConstants_FromCuFiles()
    {
        var additionalFile = GetAdditionalText("NativeCudaKernels/HappyPath.cu");

        var compilation = CSharpCompilation.Create("TestComp")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new NativeCudaKernelsGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: new[] { additionalFile });

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var result = driver.GetRunResult();
        var generatedSource = result.GeneratedTrees.FirstOrDefault()?.GetText().ToString() ?? "";

        await Verifier.Verify(generatedSource, "cs")
            .UseDirectory("Snapshots")
            .UseFileName($"Snap.{nameof(Test_GeneratesConstants_FromCuFiles)}");
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_EmptyCuFile_DoesNotGenerate()
    {
        var additionalFile = GetAdditionalText("NativeCudaKernels/Empty.cu");

        var compilation = CSharpCompilation.Create("TestComp")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new NativeCudaKernelsGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: new[] { additionalFile });

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var result = driver.GetRunResult();
        Assert.Empty(result.GeneratedTrees);
    }
}
