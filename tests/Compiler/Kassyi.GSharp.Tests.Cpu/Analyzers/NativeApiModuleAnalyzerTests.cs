using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics.Host;
using Kassyi.GSharp.Compiler.NativeApi;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using static Kassyi.GSharp.Tests.Shared.Generators.NativeApiGeneratorTestHelpers;

#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class NativeApiModuleAnalyzerTests
{
    private const string TestCase = "NativeApi/ModuleAutoLayoutStructs.cs";

    private static readonly NativeApiModuleAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ModuleParameters_AutoLayoutStructShapes_ReportGsh0053AtYamlTypeLine()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: AutoLayoutKernels
            moduleName: ./native/auto.fatbin
            methods:
              - name: ValueParam
                parameters:
                  - name: pair
                    type: AutoPair
              - name: ArrayParam
                parameters:
                  - name: items
                    type: "AutoPair[]"
              - name: NestedParam
                parameters:
                  - name: holder
                    type: HoldsAuto
              - name: ByRefParam
                parameters:
                  - name: pair
                    type: "ref AutoPair"
            """;

        var result = await RunAnalyzerAsync(Api("auto.yml", Yaml));

        Assert.Empty(result.Generator);
        Assert.All(result.Analyzer, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.All(result.Analyzer, static d => Assert.Equal("auto.yml", d.Location.GetLineSpan().Path));
        var actual = result.Analyzer
            .Select(ToFiveTuple)
            .OrderBy(static d => d.StartLine)
            .ToArray();
        const string Id = "GSH0053";
        Assert.Equal(
            [
                (Id, 8, 1, 8, 1),
                (Id, 12, 1, 12, 1),
                (Id, 16, 1, 16, 1),
                (Id, 20, 1, 20, 1),
            ],
            actual
        );
        Assert.Contains(
            result.Analyzer,
            static d => d.GetMessage().Contains("Parameter 'pair' of kernel method 'ValueParam' has type 'AutoPair'")
        );
        Assert.Contains(
            result.Analyzer,
            static d => d.GetMessage().Contains("Parameter 'items' of kernel method 'ArrayParam' has type 'AutoPair[]'")
        );
        Assert.Contains(
            result.Analyzer,
            static d => d.GetMessage().Contains(
                "Parameter 'holder' of kernel method 'NestedParam' has type 'HoldsAuto'")
        );
        Assert.Contains(
            result.Analyzer,
            static d => d.GetMessage().Contains("Parameter 'pair' of kernel method 'ByRefParam' has type 'AutoPair'")
        );
        Assert.All(
            result.Analyzer,
            static d => Assert.Contains(ModuleKernelParameterTypes.AutoLayoutReason, d.GetMessage())
        );
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task ModuleParameters_SequentialAndExplicitStructs_ReportNoDiagnostic()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: ValidLayoutKernels
            moduleName: ./native/valid.fatbin
            methods:
              - name: ValueParam
                parameters:
                  - name: pair
                    type: SeqPair
              - name: ArrayParam
                parameters:
                  - name: items
                    type: "SeqPair[]"
              - name: ExplicitParam
                parameters:
                  - name: pair
                    type: ExplicitPair
              - name: ByRefParam
                parameters:
                  - name: pair
                    type: "ref SeqPair"
            """;

        var result = await RunAnalyzerAsync(Api("valid.yml", Yaml));

        Assert.Empty(result.Generator);
        Assert.Empty(result.Analyzer);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task ModuleParameters_UniqueOverloads_ReportGsh0053OnOffendingOverloadOnly()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: UniqueOverloadKernels
            moduleName: ./native/overloads.fatbin
            methods:
              - name: Run
                entryPoint: run_seq
                parameters:
                  - name: p
                    type: SeqPair
              - name: Run
                entryPoint: run_seq_auto
                parameters:
                  - name: p
                    type: SeqPair
                  - name: q
                    type: AutoPair
              - name: Step
                entryPoint: step_ok
                parameters:
                  - name: ok
                    type: SeqPair
              - name: Step
                entryPoint: step_bad
                parameters:
                  - name: bad
                    type: AutoPair
            """;

        var result = await RunAnalyzerAsync(Api("overloads.yml", Yaml));

        Assert.Empty(result.Generator);
        Assert.All(result.Analyzer, static d => Assert.Equal("overloads.yml", d.Location.GetLineSpan().Path));
        var actual = result.Analyzer
            .Select(ToFiveTuple)
            .OrderBy(static d => d.StartLine)
            .ToArray();
        const string Id = "GSH0053";
        Assert.Equal([(Id, 16, 1, 16, 1), (Id, 26, 1, 26, 1)], actual);
        Assert.All(
            result.Analyzer,
            static d => Assert.Contains(ModuleKernelParameterTypes.AutoLayoutReason, d.GetMessage())
        );
    }

    // Overloads sharing name, arity, and parameter names in order cannot be matched 1-to-1 without inspecting types,
    // so the analyzer skips them silently rather than guessing from declaration order.
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task ModuleParameters_AmbiguousOverloadsSameParamNames_DoNotMisreport()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: AmbiguousOverloadKernels
            moduleName: ./native/ambiguous.fatbin
            methods:
              - name: Same
                entryPoint: same_auto
                parameters:
                  - name: p
                    type: AutoPair
              - name: Same
                entryPoint: same_int
                parameters:
                  - name: p
                    type: int
            """;

        var result = await RunAnalyzerAsync(Api("ambiguous.yml", Yaml));

        Assert.Empty(result.Generator);
        Assert.Empty(result.Analyzer);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ModuleParameters_ClassParameter_ReportsGsh0053()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: ClassParamKernels
            moduleName: ./native/class.fatbin
            methods:
              - name: Run
                parameters:
                  - name: data
                    type: "float[]"
                  - name: callback
                    type: UserClass
            """;

        var result = await RunAnalyzerAsync(Api("class.yml", Yaml));

        Assert.Empty(result.Generator);
        var diagnostic = Assert.Single(result.Analyzer);
        Assert.Equal("class.yml", diagnostic.Location.GetLineSpan().Path);
        Assert.Equal(("GSH0053", 10, 1, 10, 1), ToFiveTuple(diagnostic));
        Assert.Contains("Parameter 'callback' of kernel method 'Run' has type 'UserClass'", diagnostic.GetMessage());
        Assert.Contains(ModuleKernelParameterTypes.ManagedTypeReason, diagnostic.GetMessage());
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task ModuleParameters_DuplicateClassAcrossFiles_AnalyzesFirstFileOnly()
    {
        const string FirstYaml = """
            namespace: MyApp.Native
            className: DupKernels
            moduleName: ./native/dup1.fatbin
            methods:
              - name: Run
                parameters:
                  - name: pair
                    type: AutoPair
            """;
        const string SecondYaml = """
            namespace: MyApp.Native
            className: DupKernels
            moduleName: ./native/dup2.fatbin
            methods:
              - name: Run
                parameters:
                  - name: pair
                    type: SeqPair
            """;

        var result = await RunAnalyzerAsync(Api("first.yml", FirstYaml), Api("second.yml", SecondYaml));

        var generatorError = Assert.Single(result.Generator);
        Assert.Equal("GSH0053", generatorError.Id);
        Assert.Equal("second.yml", generatorError.Location.GetLineSpan().Path);
        var diagnostic = Assert.Single(result.Analyzer);
        Assert.Equal("first.yml", diagnostic.Location.GetLineSpan().Path);
        Assert.Equal(("GSH0053", 8, 1, 8, 1), ToFiveTuple(diagnostic));
    }

    private static async Task<AnalysisResult> RunAnalyzerAsync(params TestFile[] files)
    {
        var source = RoslynTestUtility.ReadTestCase(TestCase);
        var compilation = RoslynTestUtility.CreateCompilationAt(
                LanguageVersion.Latest,
                ("ModuleAutoLayoutStructs.cs", source))
            .WithAssemblyName(AssemblyName);
        CreateDriver(files).RunGeneratorsAndUpdateCompilation(
            compilation,
            out var output,
            out var generatorDiagnostics);
        var analyzerDiagnostics = await output
            .WithAnalyzers([s_analyzer], CreateAnalyzerOptions(files))
            .GetAnalyzerDiagnosticsAsync();
        return new AnalysisResult(generatorDiagnostics, analyzerDiagnostics);
    }

    // ExternalFileLocation has IsInSource == false, so ReportedDiagnostic.From returns zeros; read GetLineSpan.
    private static (string Id, int StartLine, int StartColumn, int EndLine, int EndColumn) ToFiveTuple(
        Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        return (
            diagnostic.Id,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1,
            span.EndLinePosition.Character + 1
        );
    }

    private readonly record struct AnalysisResult(
        ImmutableArray<Diagnostic> Generator,
        ImmutableArray<Diagnostic> Analyzer);
}
