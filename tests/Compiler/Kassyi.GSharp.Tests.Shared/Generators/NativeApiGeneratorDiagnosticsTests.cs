#nullable enable

using System;
using System.Linq;
using Kassyi.GSharp.Compiler.NativeApi;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Generators.NativeApiGeneratorTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Generators;

[Category(TestCategory.Compiler)]
public class NativeApiGeneratorDiagnosticsTests
{
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void YamlWithoutGSharpApiMetadata_IsIgnored()
    {
        var result = RunGenerator(new TestFile("other.yml", LibraryYaml, IsGSharpApi: false));

        Assert.Empty(result.GeneratedTrees);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void MalformedYaml_ReportsSyntaxDiagnosticAtLine()
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
    public void SchemaViolation_ReportsSchemaDiagnostic(string yaml)
    {
        var result = RunGenerator(Api("invalid.yml", yaml));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(GSharpDiagnostics.NativeApiSchemaViolation.Id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("\"int?\"", ModuleKernelParameterTypes.NullableValueReason)]
    [InlineData("\"float[][]\"", ModuleKernelParameterTypes.JaggedArrayReason)]
    [InlineData("\"float [ ] [ ]\"", ModuleKernelParameterTypes.JaggedArrayReason)]
    [InlineData("string", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("\"string[]\"", ModuleKernelParameterTypes.ManagedElementReason)]
    [InlineData("global::System.Object", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("String", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("Object", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("System.Array", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("\"ref float[]\"", ModuleKernelParameterTypes.ByReferenceReason)]
    [InlineData("\"out float*\"", ModuleKernelParameterTypes.ByReferenceReason)]
    [InlineData("ref string", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("readonly int", ModuleKernelParameterTypes.ModifiersReason)]
    [InlineData("\"float[\"", ModuleKernelParameterTypes.NotATypeReason)]
    [InlineData("\"(byte, double)\"", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("\"System.ValueTuple<int, float>[]\"", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("DateTime", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("\"Nullable<float>\"", ModuleKernelParameterTypes.NullableValueReason)]
    [InlineData("\"System.Nullable<int>[]\"", ModuleKernelParameterTypes.NullableValueReason)]
    [InlineData("\"System . String\"", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("\"scoped Span<float>\"", ModuleKernelParameterTypes.RefStructReason)]
    public void ModuleDefinition_ParameterTheLauncherCannotPass_ReportsSchemaDiagnostic(
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

    // A launch setting is chosen by its name; one of another type is an error instead of a silent conversion to the
    // stream handle or a missing kernel argument
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("stream", "int", ModuleKernelParameterTypes.StreamTypeReason)]
    [InlineData("blockDimX", "int", ModuleKernelParameterTypes.DimensionTypeReason)]
    public void ModuleDefinition_LaunchSettingOfAnotherType_ReportsSchemaDiagnostic(
        string name,
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
                  - { name: {{name}}, type: {{type}} }
            """;

        var result = RunGenerator(Api("k.yml", yaml));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(GSharpDiagnostics.NativeApiSchemaViolation.Id, diagnostic.Id);
        var span = diagnostic.Location.GetLineSpan();
        Assert.Equal(new LinePosition(7, 0), span.StartLinePosition);
        Assert.Equal(new LinePosition(7, 0), span.EndLinePosition);
        Assert.Contains($"Parameter '{name}' of kernel method 'Run' has type '{type}'", diagnostic.GetMessage());
        Assert.Contains(reason, diagnostic.GetMessage());
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void DuplicateClassAcrossFiles_ReportsSchemaDiagnosticWithoutCrashing()
    {
        var result = RunGenerator(Api("a.yml", LibraryYaml), Api("b.yml", LibraryYaml));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(GSharpDiagnostics.NativeApiSchemaViolation.Id, diagnostic.Id);
        Assert.Equal("b.yml", diagnostic.Location.GetLineSpan().Path);
        Assert.Null(result.Results[0].Exception);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void OneInvalidFile_DoesNotSuppressValidFiles()
    {
        var result = RunGenerator(Api("good.yml", LibraryYaml), Api("bad.yml", "namespace: [\n"));

        Assert.Single(result.Diagnostics);
        Assert.Contains(result.GeneratedTrees, t => t.ToString().Contains("class MyBlas"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void UnrelatedSourceChange_KeepsNativeApiStepCached()
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
