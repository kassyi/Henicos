#nullable enable

using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.NativeApi;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using VerifyXunit;

namespace Kassyi.GSharp.Tests.Shared.Generators;

[Category(TestCategory.Compiler)]
public class NativeApiGeneratorTests
{
    private const string LibraryYaml = """
        namespace: MyApp.Interop
        className: MyBlas
        libraryName: myblas
        libraryNames:
          windows: [myblas64_2.dll, "%MYBLAS_HOME%/bin/myblas64_2.dll"]
          linux: [libmyblas.so.2]
        methods:
          - name: myblasCreate
            summary: Creates a <handle> & context.
            returnType: int
            returnDescription: Zero on success.
            parameters:
              - name: handle
                type: out IntPtr
                description: Receives the created handle.
          - name: myblasSgemm_v2
            entryPoint: myblasSgemm
            isUnsafe: true
            returnType: int
            parameters:
              - { name: handle, type: IntPtr }
              - { name: alpha, type: float* }
        """;

    private const string ModuleYaml = """
        namespace: MyApp.Native
        className: MyKernels
        moduleName: ./native/my-custom.kernels.fatbin
        methods:
          - name: SgemmCustom
            entryPoint: sgemm_kernel_v1
            summary: User-defined custom matrix multiplication kernel.
            parameters:
              - { name: a, type: "float[]", description: Matrix A. }
              - { name: c, type: "float[]", description: Matrix C. }
              - { name: n, type: int }
              - { name: gridDimX, type: uint }
              - { name: blockDimX, type: uint }
          - name: Fill
            parameters:
              - { name: data, type: "int[]" }
              - { name: value, type: int }
        """;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Test_LibraryDefinition_GeneratesLibraryImportsAndResolver()
    {
        var result = RunGenerator(Api("mylib.yml", LibraryYaml));

        Assert.Empty(result.Diagnostics);
        return VerifyGenerated(result, nameof(Test_LibraryDefinition_GeneratesLibraryImportsAndResolver));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Test_ModuleDefinition_GeneratesKernelLaunchers()
    {
        var result = RunGenerator(Api("kernels.yml", ModuleYaml));

        Assert.Empty(result.Diagnostics);
        return VerifyGenerated(result, nameof(Test_ModuleDefinition_GeneratesKernelLaunchers));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_GeneratedSources_CompileWithoutErrors()
    {
        var compilation = CreateCompilation();
        var driver = CreateDriver(Api("mylib.yml", LibraryYaml), Api("kernels.yml", ModuleYaml));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics);
        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_YamlWithoutGSharpApiMetadata_IsIgnored()
    {
        var result = RunGenerator(new TestFile("other.yml", LibraryYaml, IsGSharpApi: false));

        Assert.Empty(result.GeneratedTrees);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_LibraryWithoutAlternativeNames_DoesNotEmitResolver()
    {
        const string Yaml = """
            namespace: MyApp.Interop
            className: Plain
            libraryName: plainlib
            methods:
              - name: plain_init
            """;

        var result = RunGenerator(Api("plain.yml", Yaml));

        Assert.Empty(result.Diagnostics);
        var tree = Assert.Single(result.GeneratedTrees);
        Assert.DoesNotContain("SetDllImportResolver", tree.ToString());
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_RelativeLibraryPath_IsRoutedThroughResolver()
    {
        const string Yaml = """
            namespace: MyApp.Interop
            className: Local
            libraryName: ./native/locallib.dll
            methods:
              - name: local_init
            """;

        var result = RunGenerator(Api("local.yml", Yaml));

        Assert.Empty(result.Diagnostics);
        var resolver = FindSource(result, "GSharpNativeLibraryResolver");
        Assert.Contains("case \"./native/locallib.dll\":", resolver);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_MultipleLibraryDefinitions_ShareSingleResolver()
    {
        const string SecondYaml = """
            namespace: MyApp.Interop
            className: MyRand
            libraryName: myrand
            libraryNames:
              windows: [myrand64_10.dll]
            methods:
              - name: myrandCreate
            """;

        var result = RunGenerator(Api("mylib.yml", LibraryYaml), Api("myrand.yml", SecondYaml));

        Assert.Empty(result.Diagnostics);
        var resolvers = result.GeneratedTrees.Count(t => t.ToString().Contains("SetDllImportResolver"));
        Assert.Equal(1, resolvers);
        var resolver = FindSource(result, "GSharpNativeLibraryResolver");
        Assert.Contains("case \"myblas\":", resolver);
        Assert.Contains("case \"myrand\":", resolver);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_StringValues_AreEscapedInGeneratedLiterals()
    {
        const string Yaml = """
            namespace: MyApp.Interop
            className: Quoted
            libraryName: 'C:\libs\"evil".dll'
            methods:
              - name: f
            """;

        var compilation = CreateCompilation();
        var driver = CreateDriver(Api("quoted.yml", Yaml));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var source = FindSource(driver.GetRunResult(), "Quoted");
        Assert.Contains("""C:\\libs\\\"evil\".dll""", source);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_MalformedYaml_ReportsSyntaxDiagnosticAtLine()
    {
        const string Yaml = "namespace: A.B\nclassName: C\nmethods: [a, b\n";

        var result = RunGenerator(Api("broken.yml", Yaml));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(GSharpDiagnostics.NativeApiYamlSyntaxError.Id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("broken.yml", diagnostic.Location.GetLineSpan().Path);
        Assert.Equal(2, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        Assert.Empty(result.GeneratedTrees);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("namespace: A.B\nclassName: C\nlibraryName: x\nmoduleName: y.fatbin\nmethods: []\n")]
    [InlineData("namespace: A.B\nclassName: C\nmethods: []\n")]
    [InlineData("className: C\nlibraryName: x\nmethods: []\n")]
    public void Test_SchemaViolation_ReportsSchemaDiagnostic(string yaml)
    {
        var result = RunGenerator(Api("invalid.yml", yaml));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(GSharpDiagnostics.NativeApiSchemaViolation.Id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_DuplicateClassAcrossFiles_ReportsSchemaDiagnosticWithoutCrashing()
    {
        var result = RunGenerator(Api("a.yml", LibraryYaml), Api("b.yml", LibraryYaml));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(GSharpDiagnostics.NativeApiSchemaViolation.Id, diagnostic.Id);
        Assert.Equal("b.yml", diagnostic.Location.GetLineSpan().Path);
        Assert.Null(result.Results[0].Exception);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_OneInvalidFile_DoesNotSuppressValidFiles()
    {
        var result = RunGenerator(Api("good.yml", LibraryYaml), Api("bad.yml", "namespace: [\n"));

        Assert.Single(result.Diagnostics);
        Assert.Contains(result.GeneratedTrees, t => t.ToString().Contains("class MyBlas"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_UnrelatedSourceChange_KeepsNativeApiStepCached()
    {
        var compilation = CreateCompilation();
        var driver = CreateDriver(Api("mylib.yml", LibraryYaml));

        driver = driver.RunGenerators(compilation);
        var updated = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Other { class Unrelated { } }"));
        driver = driver.RunGenerators(updated);

        var steps = driver.GetRunResult().Results[0].TrackedOutputSteps;
        var outputs = steps.SelectMany(s => s.Value).SelectMany(s => s.Outputs).ToArray();
        Assert.NotEmpty(outputs);
        Assert.All(outputs, o => Assert.True(
            o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Unexpected reason: {o.Reason}"));
    }

    private sealed record TestFile(string Path, string Text, bool IsGSharpApi);

    private static TestFile Api(string path, string text) => new(path, text, IsGSharpApi: true);

    private static GeneratorDriverRunResult RunGenerator(params TestFile[] files)
    {
        var driver = CreateDriver(files).RunGenerators(CreateCompilation());
        return driver.GetRunResult();
    }

    private static GeneratorDriver CreateDriver(params TestFile[] files)
    {
        var additionalTexts = files.Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Text)).ToArray();
        var apiPaths = files.Where(f => f.IsGSharpApi).Select(f => f.Path).ToHashSet();

        return CSharpGeneratorDriver.Create(
            generators: [new NativeApiGenerator().AsSourceGenerator()],
            additionalTexts: additionalTexts,
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            optionsProvider: new PerFileOptionsProvider(apiPaths),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
    }

    private static CSharpCompilation CreateCompilation()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(GpuKernelLoader).Assembly.Location));

        return CSharpCompilation.Create(
            "NativeApiGeneratorTestAssembly",
            [],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
    }

    private static string FindSource(GeneratorDriverRunResult result, string marker)
    {
        foreach (var tree in result.GeneratedTrees)
        {
            if (tree.FilePath.Contains(marker))
            {
                return tree.ToString();
            }
        }

        throw new Xunit.Sdk.XunitException($"No generated source whose hint name contains '{marker}'.");
    }

    /// <summary>
    /// Verifies each generated file as its own snapshot so that every <c>.verified.cs</c> stays valid C#.
    /// </summary>
    private static async Task VerifyGenerated(GeneratorDriverRunResult result, string testName)
    {
        Assert.NotEmpty(result.GeneratedTrees);
        foreach (var tree in result.GeneratedTrees.OrderBy(t => Path.GetFileName(t.FilePath), StringComparer.Ordinal))
        {
            var hintName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(tree.FilePath));
            await Verifier.Verify(tree.ToString(), "cs")
                .UseDirectory("Snapshots")
                .UseFileName($"Snap.{testName}#{hintName}");
        }
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }

    private sealed class PerFileOptionsProvider(HashSet<string> apiPaths) : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions s_api = new DictionaryOptions(true);
        private static readonly AnalyzerConfigOptions s_none = new DictionaryOptions(false);

        public override AnalyzerConfigOptions GlobalOptions => s_none;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => s_none;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
            apiPaths.Contains(textFile.Path) ? s_api : s_none;
    }

    private sealed class DictionaryOptions(bool isApi) : AnalyzerConfigOptions
    {
#pragma warning disable CS8765 // NotNullWhen is ambiguous with the PolySharp copy inside Kassyi.GSharp.Compiler
        public override bool TryGetValue(string key, out string? value)
#pragma warning restore CS8765
        {
            if (isApi && key == NativeApiGenerator.GSharpApiMetadataKey)
            {
                value = "true";
                return true;
            }

            value = null;
            return false;
        }
    }
}
