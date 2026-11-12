#nullable enable

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Generators.NativeApiGeneratorTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Generators;

/// <summary>
/// A YAML module binding and <c>[CudaImport]</c> methods of the same module in one class share one module field: the
/// YAML binding declares it and the <c>[CudaImport]</c> part leaves it out. Other modules and other classes keep their
/// own field.
/// </summary>
[Category(TestCategory.Compiler)]
public class NativeApiSharedModuleClassTests
{
    // A YAML module binding of My.Native.Shared: Fill(int[] data, <valueType> value) of <moduleName>
    private static string SharedYaml(string moduleName = "shared.gfatbin", string valueType = "int") => $$"""
        namespace: My.Native
        className: Shared
        moduleName: {{moduleName}}
        methods:
          - name: Fill
            parameters:
              - { name: data, type: "int[]" }
              - { name: value, type: "{{valueType}}" }
        """;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void YamlAndCudaImportOfOneModuleInOneClass_ShareOneModuleField()
    {
        var output = Compile(Api("shared.yml", SharedYaml()));

        Assert.Equal(new[] { "s_fatbin_other_gfatbin", "s_fatbin_shared_gfatbin" }, ModuleFields(output, "Shared"));
        Assert.Equal(new[] { "s_fatbin_shared_gfatbin" }, ModuleFields(output, "Separate"));
        var shared = output.GetTypeByMetadataName("My.Native.Shared");
        Assert.NotNull(shared);
        Assert.Single(shared.GetMembers("Fill"));
        Assert.Single(shared.GetMembers("Imported"));
    }

    // A YAML binding of another module in the class leaves the module fields of the [CudaImport] methods to them
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void YamlOfAnotherModuleInTheClass_KeepsTheCudaImportField()
    {
        var output = Compile(Api("shared.yml", SharedYaml(moduleName: "yaml.gfatbin")));

        Assert.Equal(new[] { "s_fatbin_other_gfatbin", "s_fatbin_shared_gfatbin", "s_fatbin_yaml_gfatbin" },
            ModuleFields(output, "Shared"));
    }

    // A YAML definition that is not read (GSH0053) declares nothing, so the [CudaImport] part declares the field
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void YamlThatFailsToRead_KeepsTheCudaImportField()
    {
        var yaml = SharedYaml(valueType: "int[][]");
        var compilation = CreateTestCaseCompilation();

        CreateDriverWithCudaImport(compilation, Api("shared.yml", yaml))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Equal("GSH0053", Assert.Single(diagnostics).Id);
        var errors = GetErrors(output);
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        Assert.Equal(new[] { "s_fatbin_other_gfatbin", "s_fatbin_shared_gfatbin" }, ModuleFields(output, "Shared"));
    }

    private static Compilation Compile(params TestFile[] files)
    {
        var compilation = CreateTestCaseCompilation();

        CreateDriverWithCudaImport(compilation, files)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        var errors = GetErrors(output);
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        return output;
    }

    private static Compilation CreateTestCaseCompilation() =>
        RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("NativeApi/CudaImportSharedModuleClass.cs")
        );

    // The module fields that the generated parts declare in My.Native.<className>, in name order
    private static string[] ModuleFields(Compilation compilation, string className)
    {
        var type = compilation.GetTypeByMetadataName("My.Native." + className);
        Assert.NotNull(type);
        return type.GetMembers()
            .OfType<IFieldSymbol>()
            .Select(static f => f.Name)
            .Where(static n => n.StartsWith("s_fatbin_", StringComparison.Ordinal))
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToArray();
    }
}
