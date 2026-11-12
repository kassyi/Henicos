#nullable enable

using Kassyi.GSharp.Compiler.NativeApi;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using VerifyXunit;
using System.IO;

namespace Kassyi.GSharp.Tests.Shared.Generators;

[Category(TestCategory.Compiler)]
public class NativeCudaKernelsGeneratorTests
{
    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    private static string GetTestCasePath(string relativeFilePath) =>
        RepositoryPaths.Combine(
            "tests",
            "Compiler",
            "Kassyi.GSharp.Tests.Shared",
            "TestCases",
            "Generators",
            relativeFilePath
        );

    private static InMemoryAdditionalText GetAdditionalText(string relativeFilePath)
    {
        var path = GetTestCasePath(relativeFilePath);
        return new InMemoryAdditionalText(path, File.ReadAllText(path));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task GeneratesConstants_FromCuFiles()
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
            .UseFileName($"Snap.{nameof(GeneratesConstants_FromCuFiles)}");
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void EmptyCuFile_DoesNotGenerate()
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

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void LaunchBoundsAndAttributes_AreSkippedBeforeTheKernelName()
    {
        var (output, errors) = RunGenerator("NativeCudaKernels/LaunchBounds.cu");

        Assert.Empty(errors);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["BoundsOne"] = "BoundsOne",
                ["BoundsTwo"] = "BoundsTwo",
                ["SplitAcrossLines"] = "SplitAcrossLines",
                ["NoInline"] = "NoInline",
                ["BoundsBeforeVoid"] = "BoundsBeforeVoid",
                ["OtherAttributes"] = "OtherAttributes",
                ["GnuAttribute"] = "GnuAttribute",
                ["CommentBeforeName"] = "CommentBeforeName",
                ["Templated"] = "Templated",
                ["StandardAttribute"] = "StandardAttribute",
            },
            ReadKernelConstants(output)
        );
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void KeywordKernelName_IsEscapedAndKeepsTheEntryPointName()
    {
        var usage = RoslynTestUtility.ReadTestCase("Generators/NativeCudaKernels/KeywordNameUsage.cs");

        var (output, errors) = RunGenerator("NativeCudaKernels/KeywordName.cu", usage);

        Assert.Empty(errors);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["checked"] = "checked",
                ["base"] = "base",
                ["PlainName"] = "PlainName",
            },
            ReadKernelConstants(output)
        );
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EditThatKeepsTheKernelNames_LeavesTheOutputCached()
    {
        var path = GetTestCasePath("NativeCudaKernels/LaunchBounds.cu");
        var text = File.ReadAllText(path);
        var original = new InMemoryAdditionalText(path, text);
        var edited = new InMemoryAdditionalText(path, text + "// An edit that declares no kernel");
        var added = new InMemoryAdditionalText(path, text + "extern \"C\" __global__ void AddedKernel(int* data) { }");
        var compilation = RoslynTestUtility.CreateCompilation();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new NativeCudaKernelsGenerator().AsSourceGenerator()],
            additionalTexts: [original],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true
            )
        );
        driver = driver.RunGenerators(compilation);

        driver = driver.ReplaceAdditionalText(original, edited).RunGenerators(compilation);
        var afterEdit = OutputReasons(driver);
        driver = driver.ReplaceAdditionalText(edited, added).RunGenerators(compilation);
        var afterAdd = OutputReasons(driver);

        Assert.NotEmpty(afterEdit);
        Assert.All(afterEdit, static reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
        Assert.Contains(IncrementalStepRunReason.Modified, afterAdd);
    }

    private static List<IncrementalStepRunReason> OutputReasons(GeneratorDriver driver) =>
        driver.GetRunResult().Results[0].TrackedOutputSteps
            .SelectMany(static step => step.Value)
            .SelectMany(static run => run.Outputs)
            .Select(static output => output.Reason)
            .ToList();

    // Runs the generator on one .cu file and returns the errors of the generated code and of the given sources.
    private (Compilation Output, List<Diagnostic> Errors) RunGenerator(string relativeFilePath, params string[] sources)
    {
        var compilation = RoslynTestUtility.CreateCompilation(sources);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new NativeCudaKernelsGenerator().AsSourceGenerator()],
            additionalTexts: [GetAdditionalText(relativeFilePath)],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview)
        );
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics);
        var errors = driver.GetRunResult().GeneratedTrees
            .Concat(compilation.SyntaxTrees.TakeLast(sources.Length))
            .SelectMany(tree => output.GetSemanticModel(tree).GetDiagnostics())
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        return (output, errors);
    }

    private static Dictionary<string, string> ReadKernelConstants(Compilation compilation)
    {
        var type = compilation.GetTypeByMetadataName("NativeCudaKernels");
        Assert.NotNull(type);
        return type.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(static f => f.IsConst)
            .ToDictionary(static f => f.Name, static f => Assert.IsType<string>(f.ConstantValue));
    }
}
