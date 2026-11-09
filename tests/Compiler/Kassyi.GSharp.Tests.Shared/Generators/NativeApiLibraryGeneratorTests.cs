#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Generators.NativeApiGeneratorTestHelper;

namespace Kassyi.GSharp.Tests.Shared.Generators;

[Category(TestCategory.Compiler)]
public class NativeApiLibraryGeneratorTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Test_LibraryDefinition_GeneratesLibraryImportsAndResolver()
    {
        var result = RunGenerator(Api("mylib.yml", LibraryYaml));

        Assert.Empty(result.Diagnostics);
        return VerifyGenerated(result, nameof(Test_LibraryDefinition_GeneratesLibraryImportsAndResolver));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Test_LibraryDefinition_WithStringMarshallingAndMultilineSummary_GeneratesDllImports()
    {
        const string Yaml = """
            namespace: MyApp.Interop
            className: Marshalled
            libraryName: marshalled
            methods:
              - name: utf8Open
                summary: |
                  Opens a path & returns <handle>.
                  Second line.
                stringMarshalling: Utf8
                returnType: int
                parameters:
                  - { name: path, type: string }
                  - { name: label, type: "string?" }
                  - { name: flags, type: int }
              - name: utf16Open
                stringMarshalling: Utf16
                returnType: int
                parameters:
                  - { name: path, type: string }
            """;

        var result = RunGenerator(Api("marshalled.yml", Yaml));

        Assert.Empty(result.Diagnostics);
        return VerifyGenerated(
            result,
            nameof(Test_LibraryDefinition_WithStringMarshallingAndMultilineSummary_GeneratesDllImports));
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
    [Scenario(TestScenario.Boundary)]
    public void Test_LibraryDefinition_RefAndStringParameters_RemainAllowed()
    {
        const string Yaml = """
            namespace: My.Interop
            className: Lib
            libraryName: mylib
            methods:
              - name: Create
                returnType: int
                parameters:
                  - { name: handle, type: out IntPtr }
                  - { name: name, type: string }
            """;

        var result = RunGenerator(Api("lib.yml", Yaml));

        Assert.Empty(result.Diagnostics);
        Assert.Single(result.GeneratedTrees);
    }
}
