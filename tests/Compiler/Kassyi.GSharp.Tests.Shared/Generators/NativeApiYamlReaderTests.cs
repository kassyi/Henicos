using System.IO;
using Kassyi.GSharp.Compiler.NativeApi;

namespace Kassyi.GSharp.Tests.Shared.Generators;

[Category(TestCategory.Compiler)]
public class NativeApiYamlReaderTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TryRead_LibraryDefinition_MapsAllExistingKeys()
    {
        const string Yaml = """
            namespace: MyApp.Interop
            className: MyLib
            libraryName: mylib
            libraryNames:
              windows: [mylib64_2.dll, "%MYLIB_HOME%/bin/mylib64_2.dll"]
              linux:
                - libmylib.so.2
                - libmylib.so

            methods:
              # Leading comment
              - name: myFunc_v2
                entryPoint: myFunc
                stringMarshalling: Utf8
                isUnsafe: true
                summary: Does a thing.   # trailing comment
                returnType: int
                returnDescription: Zero on success.
                parameters:
                  - name: handle
                    type: IntPtr
                    description: 'The handle''s value.'
                  - name: name
                    type: string
                    description: "Escaped \"name\"."
                    defaultValue: "null"
            """;

        var success = NativeApiYamlReader.TryRead(Yaml, out var definition, out var error);

        Assert.True(success, error?.Message);
        Assert.NotNull(definition);
        Assert.Equal("MyApp.Interop", definition.Namespace);
        Assert.Equal("MyLib", definition.ClassName);
        Assert.Equal("mylib", definition.LibraryName);
        Assert.Null(definition.ModuleName);
        Assert.Equal(NativeApiBindingKind.Library, definition.BindingKind);

        Assert.NotNull(definition.LibraryNames);
        Assert.Equal(["mylib64_2.dll", "%MYLIB_HOME%/bin/mylib64_2.dll"], definition.LibraryNames.Windows.ToArray());
        Assert.Equal(["libmylib.so.2", "libmylib.so"], definition.LibraryNames.Linux.ToArray());

        var method = Assert.Single(definition.Methods);
        Assert.Equal("myFunc_v2", method.Name);
        Assert.Equal("myFunc", method.EntryPoint);
        Assert.Equal("Utf8", method.StringMarshalling);
        Assert.True(method.IsUnsafe);
        Assert.Equal("Does a thing.", method.Summary);
        Assert.Equal("int", method.ReturnType);
        Assert.Equal("Zero on success.", method.ReturnDescription);
        Assert.Equal(2, method.Parameters.Length);
        Assert.Equal("The handle's value.", method.Parameters[0].Description);
        Assert.Equal("Escaped \"name\".", method.Parameters[1].Description);
        Assert.Equal("null", method.Parameters[1].DefaultValue);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TryRead_ModuleDefinition_ResolvesModuleBindingKind()
    {
        const string Yaml = """
            namespace: MyApp.Native
            className: MyKernels
            moduleName: ./native/my_custom_kernels.fatbin
            methods:
              - name: SgemmCustom
                entryPoint: sgemm_kernel_v1
                parameters:
                  - { name: a, type: "float[]", description: "Matrix A." }
                  - { name: blockDimX, type: uint }
            """;

        var success = NativeApiYamlReader.TryRead(Yaml, out var definition, out var error);

        Assert.True(success, error?.Message);
        Assert.NotNull(definition);
        Assert.Equal(NativeApiBindingKind.Module, definition.BindingKind);
        Assert.Equal("./native/my_custom_kernels.fatbin", definition.ModuleName);

        var method = Assert.Single(definition.Methods);
        Assert.Equal("sgemm_kernel_v1", method.EntryPoint);
        Assert.Equal("void", method.ReturnType);
        Assert.Equal("float[]", method.Parameters[0].Type);
        Assert.Equal("Matrix A.", method.Parameters[0].Description);
        Assert.Equal("uint", method.Parameters[1].Type);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TryRead_IntrinsicDefinition_ParsesTypeTemplate()
    {
        const string Yaml = """
            namespace: Kassyi.GSharp.Core
            className: Gpu
            methods:
            - name: AtomicAdd
              nativeName: atomicAdd
              typeTemplate:
                placeholder: T
                types: [int, uint, ulong, float]
              returnType: T
              parameters:
              - name: address
                type: ref T
            """;

        var success = NativeApiYamlReader.TryRead(Yaml, out var definition, out var error);

        Assert.True(success, error?.Message);
        Assert.NotNull(definition);
        Assert.Equal(NativeApiBindingKind.Intrinsic, definition.BindingKind);

        var method = Assert.Single(definition.Methods);
        Assert.Equal("atomicAdd", method.NativeName);
        Assert.NotNull(method.TypeTemplate);
        Assert.Equal("T", method.TypeTemplate.Placeholder);
        Assert.Equal(["int", "uint", "ulong", "float"], method.TypeTemplate.Types.ToArray());
        Assert.Equal("ref T", method.Parameters[0].Type);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TryRead_BlockScalars_FoldAndPreserveLines()
    {
        const string Yaml = """
            namespace: MyApp.Interop
            className: MyLib
            libraryName: mylib
            methods:
              - name: f
                summary: >
                  Folded line one
                  continues here.
                returnDescription: |
                  Line one.
                  Line two.
            """;

        var success = NativeApiYamlReader.TryRead(Yaml, out var definition, out var error);

        Assert.True(success, error?.Message);
        Assert.NotNull(definition);
        Assert.Equal("Folded line one continues here.", definition.Methods[0].Summary);
        Assert.Equal("Line one.\nLine two.", definition.Methods[0].ReturnDescription);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("cuda_driver_api.yml")]
    [InlineData("ptx_intrinsics.yml")]
    [InlineData("cublas_api.yml")]
    [InlineData("curand_api.yml")]
    public void Test_TryRead_BuiltInDefinitions_ParseWithoutErrors(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../../src/NativeApi", fileName));
        var yaml = File.ReadAllText(path);

        var success = NativeApiYamlReader.TryRead(yaml, out var definition, out var error);

        Assert.True(success, error is null ? null : $"line {error.Line}: {error.Message}");
        Assert.NotNull(definition);
        Assert.NotEmpty(definition.Methods);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_TryRead_EmptyMethods_Succeeds()
    {
        const string Yaml = """
            ---
            namespace: MyApp.Interop
            className: Empty
            libraryName: mylib
            methods: []
            """;

        var success = NativeApiYamlReader.TryRead(Yaml, out var definition, out var error);

        Assert.True(success, error?.Message);
        Assert.NotNull(definition);
        Assert.Empty(definition.Methods);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_TryRead_CrLfLineEndings_AreAccepted()
    {
        const string Yaml = "namespace: A.B\r\nclassName: C\r\nlibraryName: lib\r\nmethods:\r\n  - name: f\r\n";

        var success = NativeApiYamlReader.TryRead(Yaml, out var definition, out var error);

        Assert.True(success, error?.Message);
        Assert.NotNull(definition);
        Assert.Equal("f", definition.Methods[0].Name);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_TryRead_HashInsideQuotedScalar_IsNotComment()
    {
        const string Yaml = """
            namespace: A.B
            className: C
            libraryName: "lib#1"
            methods: []
            """;

        var success = NativeApiYamlReader.TryRead(Yaml, out var definition, out var error);

        Assert.True(success, error?.Message);
        Assert.NotNull(definition);
        Assert.Equal("lib#1", definition.LibraryName);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("namespace: A.B\nclassName: C\n\tlibraryName: x\n", 3)]
    [InlineData("namespace: A.B\nclassName: C\nmethods:\n  - name: \"unterminated\n", 4)]
    [InlineData("namespace: A.B\nclassName: C\nmethods: [a, b\n", 3)]
    [InlineData("namespace: A.B\nnamespace: C.D\nclassName: C\n", 2)]
    [InlineData("namespace: A.B\nclassName: &anchor C\n", 2)]
    [InlineData("namespace: A.B\nclassName: C\n  bogus: 1\n", 3)]
    public void Test_TryRead_MalformedYaml_ReportsSyntaxErrorWithLine(string yaml, int expectedLine)
    {
        var success = NativeApiYamlReader.TryRead(yaml, out var definition, out var error);

        Assert.False(success);
        Assert.Null(definition);
        Assert.NotNull(error);
        Assert.Equal(NativeApiReadErrorKind.Syntax, error.Kind);
        Assert.Equal(expectedLine, error.Line);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("className: C\nlibraryName: x\nmethods: []\n", "namespace")]
    [InlineData("namespace: A.B\nlibraryName: x\nmethods: []\n", "className")]
    [InlineData("namespace: A.B\nclassName: C\nlibraryName: x\nmoduleName: y.fatbin\nmethods: []\n", "moduleName")]
    [InlineData("namespace: A.B\nclassName: C\nlibraryName: x\nmethods:\n  - summary: no name\n", "name")]
    [InlineData("namespace: A.B\nclassName: C\nlibraryName: x\nmethods:\n  - name: f\n    parameters:\n      - name: p\n", "type")]
    [InlineData("namespace: A.B\nclassName: 1Bad\nlibraryName: x\nmethods: []\n", "className")]
    [InlineData("namespace: A..B\nclassName: C\nlibraryName: x\nmethods: []\n", "namespace")]
    [InlineData("namespace: A.B\nclassName: C\nmoduleName: k.fatbin\nmethods:\n  - name: f\n    returnType: int\n", "returnType")]
    [InlineData("namespace: A.B\nclassName: C\nlibraryName: x\nmethods:\n  - name: f\n    isUnsafe: maybe\n", "isUnsafe")]
    [InlineData("namespace: A.B\nclassName: C\nlibraryName: x\nmethods: nope\n", "methods")]
    public void Test_TryRead_SchemaViolation_ReportsSchemaErrorNamingTheKey(string yaml, string expectedKey)
    {
        var success = NativeApiYamlReader.TryRead(yaml, out var definition, out var error);

        Assert.False(success);
        Assert.Null(definition);
        Assert.NotNull(error);
        Assert.Equal(NativeApiReadErrorKind.Schema, error.Kind);
        Assert.Contains($"'{expectedKey}'", error.Message);
        Assert.True(error.Line >= 1);
    }
}
