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
    // The module fields: __gsharp_module_ and the module name with '.' escaped as _x00002e
    private const string SharedField = "__gsharp_module_shared_x00002egfatbin";
    private const string OtherField = "__gsharp_module_other_x00002egfatbin";
    private const string YamlField = "__gsharp_module_yaml_x00002egfatbin";

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

        Assert.Equal(new[] { OtherField, SharedField }, ModuleFields(output, "Shared"));
        Assert.Equal(new[] { SharedField }, ModuleFields(output, "Separate"));
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

        Assert.Equal(new[] { OtherField, SharedField, YamlField }, ModuleFields(output, "Shared"));
    }

    // A YAML definition that is not read (GSH0053) declares nothing, so the [CudaImport] part declares the field
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void YamlThatFailsToRead_KeepsTheCudaImportField()
    {
        var yaml = SharedYaml(valueType: "int[][]");
        var compilation = CreateTestCaseCompilation("NativeApi/CudaImportSharedModuleClass.cs");

        CreateDriverWithCudaImport(compilation, Api("shared.yml", yaml))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Equal("GSH0053", Assert.Single(diagnostics).Id);
        var errors = GetErrors(output);
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        Assert.Equal(new[] { OtherField, SharedField }, ModuleFields(output, "Shared"));
    }

    // a.gfatbin and a_gfatbin get fields of their own (the module name is escaped injectively): the YAML binding
    // declares the one of a.gfatbin, which the [CudaImport] method of a.gfatbin uses, and the [CudaImport] part
    // declares the one of a_gfatbin
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ModulesDifferingInPunctuation_GetFieldsOfTheirOwn()
    {
        const string Yaml = """
            namespace: My.Native
            className: Dotted
            moduleName: a.gfatbin
            methods:
              - name: Fill
                parameters:
                  - { name: data, type: "int[]" }
            """;

        var output = Compile(Api("dotted.yml", Yaml), "NativeApi/CudaImportEscapedModuleNames.cs");

        Assert.Equal(new[] { "__gsharp_module_a_ugfatbin", "__gsharp_module_a_x00002egfatbin" },
            ModuleFields(output, "Dotted"));
    }

    private static Compilation Compile(params TestFile[] files) =>
        Compile(files, "NativeApi/CudaImportSharedModuleClass.cs");

    private static Compilation Compile(TestFile file, string testCase) => Compile([file], testCase);

    private static Compilation Compile(TestFile[] files, string testCase)
    {
        var compilation = CreateTestCaseCompilation(testCase);

        CreateDriverWithCudaImport(compilation, files)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        var errors = GetErrors(output);
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        return output;
    }

    private static Compilation CreateTestCaseCompilation(string testCase) =>
        RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(testCase));

    // The module fields that the generated parts declare in My.Native.<className>, in name order
    private static string[] ModuleFields(Compilation compilation, string className)
    {
        var type = compilation.GetTypeByMetadataName("My.Native." + className);
        Assert.NotNull(type);
        return type.GetMembers()
            .OfType<IFieldSymbol>()
            .Select(static f => f.Name)
            .Where(static n => n.StartsWith("__gsharp_module_", StringComparison.Ordinal))
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToArray();
    }
}
