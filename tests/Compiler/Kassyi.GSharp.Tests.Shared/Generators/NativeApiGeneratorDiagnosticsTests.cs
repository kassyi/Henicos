#nullable enable

using System;
using System.Linq;
using Kassyi.GSharp.Compiler.NativeApi;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Generators.NativeApiGeneratorTestHelper;

namespace Kassyi.GSharp.Tests.Shared.Generators;

[Category(TestCategory.Compiler)]
public class NativeApiGeneratorDiagnosticsTests
{
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_YamlWithoutGSharpApiMetadata_IsIgnored()
    {
        var result = RunGenerator(new TestFile("other.yml", LibraryYaml, IsGSharpApi: false));

        Assert.Empty(result.GeneratedTrees);
        Assert.Empty(result.Diagnostics);
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

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("\"float[]?\"", "nullable types")]
    [InlineData("\"int?\"", "nullable types")]
    [InlineData("\"float[,]\"", "one-dimensional arrays")]
    [InlineData("\"float[][]\"", "one-dimensional arrays")]
    [InlineData("string", "reference types")]
    [InlineData("\"string[]\"", "reference types")]
    [InlineData("global::System.Object", "reference types")]
    [InlineData("String", "reference types")]
    [InlineData("Object", "reference types")]
    [InlineData("System.Array", "reference types")]
    [InlineData("ref int", "'ref', 'out', 'in', 'scoped' and 'params'")]
    [InlineData("out int", "'ref', 'out', 'in', 'scoped' and 'params'")]
    [InlineData("in int", "'ref', 'out', 'in', 'scoped' and 'params'")]
    [InlineData("scoped ref int", "'ref', 'out', 'in', 'scoped' and 'params'")]
    public void Test_ModuleDefinition_ParameterTheLauncherCannotPass_ReportsSchemaDiagnostic(
        string type,
        string reason)
    {
        var yaml = $$"""
            namespace: My.Kernels
            className: K
            moduleName: k.fatbin
            methods:
              - name: Run
                parameters:
                  - { name: data, type: "float[]" }
                  - { name: p, type: {{type}} }
                  - { name: gridDimX, type: uint }
            """;

        var result = RunGenerator(Api("k.yml", yaml));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(GSharpDiagnostics.NativeApiSchemaViolation.Id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        var span = diagnostic.Location.GetLineSpan();
        Assert.Equal("k.yml", span.Path);
        Assert.Equal(new LinePosition(7, 0), span.StartLinePosition);
        Assert.Equal(new LinePosition(7, 0), span.EndLinePosition);
        var message = diagnostic.GetMessage();
        Assert.Contains("Parameter 'p' of kernel method 'Run'", message);
        Assert.Contains(reason, message);
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
        var updated = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { class Unrelated { } }"));
        driver = driver.RunGenerators(updated);

        var steps = driver.GetRunResult().Results[0].TrackedOutputSteps;
        var outputs = steps.SelectMany(s => s.Value).SelectMany(s => s.Outputs).ToArray();
        Assert.NotEmpty(outputs);
        Assert.All(outputs, o => Assert.True(
            o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Unexpected reason: {o.Reason}"));
    }
}
