#nullable enable

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Generators.NativeApiGeneratorTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Generators;

/// <summary>
/// The names and strings of a native API definition reach the generated C# as C# spells them: names that are C#
/// keywords with <c>@</c>, strings as escaped literals whose value is the string written; and the library candidates
/// of every definition with the same <c>libraryName</c> reach its one resolver.
/// </summary>
[Category(TestCategory.Compiler)]
public class NativeApiEscapingTests
{
    // U+2028 (line separator), U+2029 (paragraph separator) and U+0085 (next line) end a line in C# source
    private const string LineSeparator = "\u2028";
    private const string ParagraphSeparator = "\u2029";
    private const string NextLine = "\u0085";

    // Native C APIs name parameters 'in', 'out', 'base' or 'params'; the YAML reader accepts them and the binding
    // declares them with '@', in the namespace, the class, the method and the parameters
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LibraryDefinition_KeywordNames_DeclareEscapedNames()
    {
        const string Yaml = """
            namespace: My.class
            className: base
            libraryName: fx
            methods:
              - name: checked
                isUnsafe: true
                returnType: int
                parameters:
                  - { name: in, type: float* }
                  - { name: out, type: float* }
                  - { name: params, type: int }
            """;

        var output = Compile(Api("fx.yml", Yaml));

        var type = output.GetTypeByMetadataName("My.class.base");
        Assert.NotNull(type);
        var method = Assert.Single(type.GetMembers("checked").OfType<IMethodSymbol>());
        Assert.Equal(new[] { "in", "out", "params" }, method.Parameters.Select(static p => p.Name));
        Assert.Equal("checked", method.GetDllImportData()?.EntryPointName);
    }

    // The launcher of a module kernel reads and stages each parameter by its escaped name: an array, a nullable array,
    // a value passed by 'out' and by 'ref', a multidimensional array and a plain value
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ModuleDefinition_KeywordNames_LauncherCompiles()
    {
        const string Yaml = """
            namespace: My.event
            className: fixed
            moduleName: k.gfatbin
            methods:
              - name: checked
                parameters:
                  - { name: in, type: "float[]" }
                  - { name: base, type: "float[]?" }
                  - { name: out, type: out int }
                  - { name: ref, type: ref int }
                  - { name: this, type: "float[,]" }
                  - { name: params, type: int }
                  - { name: gridDimX, type: uint }
            """;

        var output = Compile(Api("k.yml", Yaml));

        var type = output.GetTypeByMetadataName("My.event.fixed");
        Assert.NotNull(type);
        var method = Assert.Single(type.GetMembers("checked").OfType<IMethodSymbol>());
        Assert.Equal(new[] { "in", "base", "out", "ref", "this", "params", "gridDimX" },
            method.Parameters.Select(static p => p.Name));
    }

    // A pointer parameter of a module kernel makes the launcher an unsafe method
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ModuleDefinition_PointerParameter_LauncherCompiles()
    {
        const string Yaml = """
            namespace: My.Native
            className: PointerKernels
            moduleName: k.gfatbin
            methods:
              - name: Fill
                parameters:
                  - { name: data, type: float* }
                  - { name: n, type: int }
            """;

        var output = Compile(Api("pointer.yml", Yaml));

        var method = output.GetTypeByMetadataName("My.Native.PointerKernels")?.GetMembers("Fill").Single();
        Assert.NotNull(method);
    }

    // The library name, an entry point and a library candidate with line separators compile to literals of the same
    // strings: the DllImport of the binding and the case of the resolver name the library as written
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LibraryDefinition_LineSeparatorsInStrings_EscapedInLiterals()
    {
        const string LibraryName = "lib" + LineSeparator + "name";
        const string EntryPoint = "f" + ParagraphSeparator + "x";
        const string Candidate = "c" + NextLine + ".dll";
        var yaml = $$"""
            namespace: My.Native
            className: Separated
            libraryName: "{{LibraryName}}"
            libraryNames:
              windows: ["{{Candidate}}"]
            methods:
              - name: f
                entryPoint: "{{EntryPoint}}"
            """;

        var output = Compile(Api("separated.yml", yaml));

        var method = output.GetTypeByMetadataName("My.Native.Separated")?.GetMembers("f").OfType<IMethodSymbol>()
            .Single();
        Assert.NotNull(method);
        Assert.Equal(LibraryName, method.GetDllImportData()?.ModuleName);
        Assert.Equal(EntryPoint, method.GetDllImportData()?.EntryPointName);
        var resolver = Assert.Single(output.SyntaxTrees,
            static t => t.FilePath.EndsWith("GSharpNativeLibraryResolver.g.cs", StringComparison.Ordinal));
        var strings = StringValues(output, resolver);
        Assert.Contains(LibraryName, strings);
        Assert.Contains(Candidate, strings);
    }

    // The module name and the entry point of a YAML module kernel with line separators
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ModuleDefinition_LineSeparatorsInStrings_EscapedInLiterals()
    {
        const string ModuleName = "m" + LineSeparator + ".gfatbin";
        const string EntryPoint = "k" + ParagraphSeparator + NextLine;
        var yaml = $$"""
            namespace: My.Native
            className: SeparatedKernels
            moduleName: "{{ModuleName}}"
            methods:
              - name: Run
                entryPoint: "{{EntryPoint}}"
                parameters:
                  - { name: data, type: "float[]" }
            """;

        var output = Compile(Api("separated.yml", yaml));

        var tree = Assert.Single(output.SyntaxTrees,
            static t => t.FilePath.EndsWith("SeparatedKernels.NativeApi.g.cs", StringComparison.Ordinal));
        var strings = StringValues(output, tree);
        Assert.Contains(ModuleName, strings);
        Assert.Contains(EntryPoint, strings);
    }

    // The module name and the entry point of a [CudaImport] method with line separators
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CudaImport_LineSeparatorsInStrings_EscapedInLiterals()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("NativeApi/CudaImportLineSeparators.cs")
        );

        CreateDriverWithCudaImport(compilation)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        var errors = GetErrors(output);
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        var tree = Assert.Single(output.SyntaxTrees,
            static t => t.FilePath.EndsWith("_CudaImport.g.cs", StringComparison.Ordinal));
        var strings = StringValues(output, tree);
        Assert.Contains("m" + LineSeparator + "odule.gfatbin", strings);
        Assert.Contains("k" + ParagraphSeparator + "er" + NextLine + "nel", strings);
    }

    // Two definitions of one library name (two classes binding the same DLL under different file names) share the
    // resolver of that name, which probes the candidates of both, whatever order the files come in: those of the file
    // whose path sorts first come first, each list keeps its order, and the logical name comes last
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SameLibraryNameInTwoDefinitions_ResolverProbesTheCandidatesOfBoth()
    {
        const string First = """
            namespace: My.A
            className: FftA
            libraryName: cufft
            libraryNames:
              windows:
                - cufft64_11.dll
            methods:
              - name: cufftGetVersion
                isUnsafe: true
                parameters:
                  - { name: version, type: int* }
            """;
        const string Second = """
            namespace: My.B
            className: FftB
            libraryName: cufft
            libraryNames:
              windows:
                - cufft64_12.dll
                - cufft64_11.dll
              linux: [libcufft.so.12]
            methods:
              - name: cufftGetVersion
                isUnsafe: true
                parameters:
                  - { name: version, type: int* }
            """;

        var inOrder = FindSource(RunGenerator(Api("a.yml", First), Api("b.yml", Second)),
            "GSharpNativeLibraryResolver");
        var reversed = FindSource(RunGenerator(Api("b.yml", Second), Api("a.yml", First)),
            "GSharpNativeLibraryResolver");

        Assert.Equal(inOrder, reversed);
        const string Fields = "private static readonly string[] s_candidates0";
        Assert.Contains(Fields + "Windows = [\"cufft64_11.dll\", \"cufft64_12.dll\", \"cufft\"];", inOrder);
        Assert.Contains(Fields + "Linux = [\"libcufft.so.12\", \"cufft\"];", inOrder);
        Assert.Contains(Fields + "OSX = [\"cufft\"];", inOrder);
        const string Case = "case \"cufft\":";
        Assert.Equal(inOrder.IndexOf(Case, StringComparison.Ordinal),
            inOrder.LastIndexOf(Case, StringComparison.Ordinal));
        Assert.DoesNotContain("s_candidates1", inOrder);
    }

    // A YAML class may be named 'required', 'scoped' or 'file', which C# does not allow as a type name unless written
    // with '@' (CS9029, CS9062, CS9056)
    [Theory]
    [InlineData("required")]
    [InlineData("scoped")]
    [InlineData("file")]
    [Scenario(TestScenario.Boundary)]
    public void ClassNamedByContextualKeyword_LibraryAndModuleBindingsCompile(string className)
    {
        var library = $"""
            namespace: My.Lib
            className: {className}
            libraryName: fx
            methods:
              - name: f
            """;
        var module = $"""
            namespace: My.Module
            className: {className}
            moduleName: k.gfatbin
            methods:
              - name: Run
                parameters:
                  - {"{"} name: data, type: "float[]" {"}"}
            """;

        var output = Compile(Api("lib.yml", library), Api("module.yml", module));

        Assert.NotNull(output.GetTypeByMetadataName("My.Lib." + className));
        Assert.NotNull(output.GetTypeByMetadataName("My.Module." + className));
    }

    // U+2028, U+2029 and U+0085 end a line of C# source: in a 'summary', a 'description', a 'returnDescription'
    // or an 'xmlDoc' they continue the '///' comment instead of ending it, so the rest of the text is not read as code
    [Theory]
    [InlineData(LineSeparator)]
    [InlineData(ParagraphSeparator)]
    [InlineData(NextLine)]
    [Scenario(TestScenario.Boundary)]
    public void LineBreaksInDocumentation_StayInTheComment(string lineBreak)
    {
        var library = $$"""
            namespace: My.Lib
            className: Documented
            libraryName: fx
            methods:
              - name: f
                summary: "first{{lineBreak}}second line"
                returnDescription: "a{{lineBreak}}b c"
                returnType: int
                parameters:
                  - { name: x, type: int, description: "one{{lineBreak}}two three" }
              - name: g
                xmlDoc: "<summary>first{{lineBreak}}second line</summary>"
            """;
        var module = $$"""
            namespace: My.Module
            className: DocumentedKernels
            moduleName: k.gfatbin
            methods:
              - name: Run
                summary: "first{{lineBreak}}second line"
                parameters:
                  - { name: data, type: "float[]", description: "one{{lineBreak}}two three" }
              - name: Other
                xmlDoc: "<summary>first{{lineBreak}}second line</summary>"
            """;

        var output = Compile(Api("lib.yml", library), Api("module.yml", module));

        Assert.NotNull(output.GetTypeByMetadataName("My.Lib.Documented")?.GetMembers("g").SingleOrDefault());
        Assert.NotNull(output.GetTypeByMetadataName("My.Module.DocumentedKernels")?.GetMembers("Other")
            .SingleOrDefault());
    }

    // Library definitions are merged in the order of their paths with '\' read as '/': 'dir\a.yml' comes before
    // 'dir0.yml' as 'dir/a.yml' does, although '\' (92) sorts after '0' (48) and '/' (47) before it
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SameLibraryNameInTwoDefinitions_PathSeparatorDoesNotChangeTheOrder()
    {
        static string Definition(string className, string candidate) => $"""
            namespace: My.Order
            className: {className}
            libraryName: cufft
            libraryNames:
              windows: [{candidate}]
            methods:
              - name: f
            """;

        var backslash = FindSource(RunGenerator(Api(@"dir\a.yml", Definition("A", "first.dll")),
            Api("dir0.yml", Definition("B", "second.dll"))), "GSharpNativeLibraryResolver");
        var slash = FindSource(RunGenerator(Api("dir/a.yml", Definition("A", "first.dll")),
            Api("dir0.yml", Definition("B", "second.dll"))), "GSharpNativeLibraryResolver");

        Assert.Contains("s_candidates0Windows = [\"first.dll\", \"second.dll\", \"cufft\"];", backslash);
        Assert.Equal(slash, backslash);
    }

    // The generated sources compile without errors
    private static Compilation Compile(params TestFile[] files)
    {
        CreateDriver(files).RunGeneratorsAndUpdateCompilation(CreateCompilation(), out var output, out var diagnostics);

        Assert.Empty(diagnostics);
        var errors = GetErrors(output);
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        return output;
    }

    // The values of the string literals of a generated tree, as the compiler reads them
    private static string[] StringValues(Compilation compilation, SyntaxTree tree)
    {
        var model = compilation.GetSemanticModel(tree);
        return tree.GetRoot()
            .DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Where(static l => l.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(l => model.GetConstantValue(l).Value as string ?? string.Empty)
            .ToArray();
    }
}
