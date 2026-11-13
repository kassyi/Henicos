#nullable enable
using Kassyi.GSharp.Compiler.NativeApi;
using Kassyi.GSharp.Tests.Shared.Generators;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Generated C# does not depend on the names that the user's scope declares: the generated parts reopen the namespace
/// of the kernel class, so a name written without <c>global::</c> binds to a user type of that name, a <c>var</c>
/// local becomes a local of a user type named <c>var</c>, and <c>nameof(p)</c> calls a parameter named <c>nameof</c>.
/// Each generator runs on a namespace that declares such types and parameters, and its output compiles.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ScopeIndependentNameGeneratorTests
{
    private const string ShadowingSource = "Generators/ShadowingScopeKernels.cs";

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu", false)]
    [InlineData("Cuda", false)]
    [InlineData("Cuda", true)]
    public void KernelGenerator_NamespaceShadowsRuntimeNames_OutputCompiles(string dialect, bool isCheckedBuild)
    {
        var (driver, output, diagnostics) = RunKernelGenerator(dialect, isCheckedBuild, ShadowingSource);

        AssertNoErrors(diagnostics, output);
        AssertNoScopeDependentNames(output, driver.GetRunResult().GeneratedTrees);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu", false)]
    [InlineData("Cuda", false)]
    [InlineData("Cuda", true)]
    public void KernelGenerator_KeywordAndGenericNames_WritesNoScopeDependentName(string dialect, bool isCheckedBuild)
    {
        var (driver, output, diagnostics) = RunKernelGenerator(dialect, isCheckedBuild,
            "Generators/KeywordNamespaceKernels.cs", "Generators/GenericSuspendingKernels.cs");

        AssertNoErrors(diagnostics, output);
        AssertNoScopeDependentNames(output, driver.GetRunResult().GeneratedTrees);
    }

    // The generated argument checks name the parameter with a string literal, which no user name can shadow: the
    // pointer launcher (CUDA) checks the proxy of the array; the array launcher passes the array as a C# call does
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void KernelGenerator_ParameterNamedNameof_ChecksNameTheParameterByLiteral(string dialect)
    {
        var (driver, _, _) = RunKernelGenerator(dialect, isCheckedBuild: false, ShadowingSource);
        var text = GetText(driver.GetRunResult().GeneratedTrees);

        if (dialect == "Cuda")
        {
            Assert.Contains(
                "throw new global::System.ArgumentOutOfRangeException(\"nameof\", nameof.Length, ",
                text,
                StringComparison.Ordinal);
        }

        Assert.DoesNotContain("nameof(", text, StringComparison.Ordinal);
    }

    // A suspending device function of a generic class gets its frames in a part of G<T>, which a kernel of another
    // class names as G<int>.<frame> (CS0426 when the part declared a non-generic G)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void KernelGenerator_SuspendingFunctionOfGenericClass_DeclaresTheGenericPart(string dialect)
    {
        var (driver, output, diagnostics) = RunKernelGenerator(dialect, isCheckedBuild: false,
            "Generators/GenericSuspendingKernels.cs");

        AssertNoErrors(diagnostics, output);
        Assert.Contains("partial class G<T>", GetText(driver.GetRunResult().GeneratedTrees), StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CudaImportGenerator_NamespaceShadowsRuntimeNames_OutputCompiles()
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(ShadowingSource));
        var driver = CSharpGeneratorDriver.Create(
            generators: [new CudaImportGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview)
        ).RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        AssertNoErrors(diagnostics, output, static d => d.Id != "CS8795");
        var text = GetText(driver.GetRunResult().GeneratedTrees);
        Assert.Contains("throw new global::System.ArgumentNullException(\"nameof\");", text, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void NativeApiGenerator_ModuleInShadowingNamespace_OutputCompiles()
    {
        const string Yaml = """
            namespace: ShadowingScope
            className: ShadowedModule
            moduleName: shadow.fatbin
            methods:
              - name: Fill
                parameters:
                  - { name: nameof, type: "int[]" }
                  - { name: optional, type: "float[]?" }
                  - { name: count, type: int }
                  - { name: gridDimX, type: uint }
                  - { name: blockDimX, type: uint }
            """;
        // The driver of the YAML generator parses with the latest language version, which every tree must share
        var compilation = NativeApiGeneratorTestHelpers.CreateCompilation().AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(
                RoslynTestUtility.ReadTestCase(ShadowingSource),
                new CSharpParseOptions(LanguageVersion.Latest)
            )
        );
        var driver = NativeApiGeneratorTestHelpers.CreateDriver(NativeApiGeneratorTestHelpers.Api("shadow.yml", Yaml))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        AssertNoErrors(diagnostics, output, static d => d.Id != "CS8795");
        var text = GetText(driver.GetRunResult().GeneratedTrees);
        Assert.Contains("throw new global::System.ArgumentNullException(\"nameof\");", text, StringComparison.Ordinal);
    }

    private static (GeneratorDriver Driver, Compilation Output, ImmutableArray<Diagnostic> Diagnostics)
        RunKernelGenerator(string dialect, bool isCheckedBuild, params string[] testCases) =>
        GeneratorTestRunner.RunGenerator(
            RoslynTestUtility.CreateCompilation(testCases.Select(RoslynTestUtility.ReadTestCase).ToArray()),
            dialect,
            isCheckedBuild ? new Dictionary<string, string> { ["build_property.GSharpCheckedBuild"] = "true" } : null
        );

    // CS8795 (a partial method without an implementation) is the [CudaImport] declarations of the shared test case,
    // which only the [CudaImport] generator implements
    private static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics, Compilation output,
        Func<Diagnostic, bool>? keep = null)
    {
        var errors = diagnostics
            .Concat(output.GetDiagnostics())
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => keep == null ? d.Id != "CS8795" : keep(d))
            .Select(static d => d.ToString())
            .ToList();

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    // Every name of the generated trees that C# looks up in the enclosing scopes: a nameof call, a var that declares a
    // local the compiler made (__gsharp_), and a type or namespace of a referenced assembly (the runtime, the BCL)
    // written without global::. A var of the user's own locals, which the managed bodies copy, binds as it does in the
    // user's source
    private static void AssertNoScopeDependentNames(Compilation output, ImmutableArray<SyntaxTree> generatedTrees)
    {
        var found = new List<string>();
        foreach (var tree in generatedTrees)
        {
            var model = output.GetSemanticModel(tree);
            foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                if (DescribeScopeDependentName(model, output, name) is { } description)
                {
                    var position = name.GetLocation().GetLineSpan().StartLinePosition;
                    found.Add($"{Path.GetFileName(tree.FilePath)}({position}): {description}");
                }
            }
        }

        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found.Distinct()));
    }

    private static string? DescribeScopeDependentName(SemanticModel model, Compilation output, SimpleNameSyntax name)
    {
        if (name is IdentifierNameSyntax { Identifier.ValueText: "nameof", Parent: InvocationExpressionSyntax })
        {
            return "nameof";
        }

        if (name is IdentifierNameSyntax { IsVar: true } && DeclaresGeneratedLocal(name.Parent))
        {
            return "var";
        }

        // The right side of a qualified name, a declared namespace, and a using directive of the compilation unit,
        // which C# binds in the global namespace
        if (name.Parent is MemberAccessExpressionSyntax access && access.Name == name ||
            name.Parent is QualifiedNameSyntax qualified && qualified.Right == name ||
            name.Parent is AliasQualifiedNameSyntax ||
            name.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>() is { } declaration &&
            declaration.Name.Span.Contains(name.Span) ||
            name.FirstAncestorOrSelf<UsingDirectiveSyntax>() is { Parent: CompilationUnitSyntax })
        {
            return null;
        }

        var symbol = model.GetSymbolInfo(name).Symbol;
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
        {
            symbol = constructor.ContainingType;
        }

        return symbol is INamespaceSymbol { IsGlobalNamespace: false } or INamedTypeSymbol &&
               !SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, output.Assembly)
            ? name.Identifier.ValueText
            : null;
    }

    // A declaration whose type is the given var and that declares a __gsharp_ local
    private static bool DeclaresGeneratedLocal(SyntaxNode? declaration) => declaration switch
    {
        VariableDeclarationSyntax variables => variables.Variables.Any(static v => IsGenerated(v.Identifier)),
        ForEachStatementSyntax loop => IsGenerated(loop.Identifier),
        DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax single } =>
            IsGenerated(single.Identifier),
        _ => false
    };

    private static bool IsGenerated(SyntaxToken identifier) =>
        identifier.ValueText.StartsWith("__gsharp_", StringComparison.Ordinal);

    private static string GetText(ImmutableArray<SyntaxTree> trees) =>
        string.Concat(trees.Select(static tree => tree.GetText().ToString()));
}
