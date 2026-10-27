using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Verifies what the separate outputs of <see cref="GpuKernelGenerator"/> take from the whole compilation: the kernels of
/// a partial method, the order of the module context's warmups, and the assembly names.
/// </summary>
[Category(TestCategory.Compiler)]
public class GeneratorOutputTests
{
    private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

    // A partial method is one kernel whichever part carries the attribute; its wrapper uses that part's parameter names
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("GpuKernel_PartialDefinition.cu.cs", "PartialDefinitionKernels", "data", "value")]
    [InlineData("GpuKernel_PartialImplementation.cu.cs", "PartialImplementationKernels", "values", "fill")]
    public void Test_PartialKernel_IsOneKernel(string testCase, string className, string arrayName, string valueName)
    {
        var compilation = CreateCompilation(
            Parse(RoslynTestUtility.ReadTestCase("Generators/" + testCase), "partial.cs"));
        var driver = CreateDriver("Cuda").RunGenerators(compilation);

        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        Assert.Single(result.TrackedSteps[GpuKernelGenerator.KernelTrackingName].SelectMany(static run => run.Outputs));
        var wrapper = GetSource(result, $"{className}.g.cs");
        Assert.Contains(
            $"LaunchFillKernel(global::System.Int32[] {arrayName}, global::System.Int32 {valueName}, ",
            wrapper,
            StringComparison.Ordinal);
        Assert.DoesNotContain("__o", wrapper, StringComparison.Ordinal);
    }

    // An alias of the attribute marks a kernel as well as its spelled-out name (A-53)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cuda", "UsingAliasKernels")]
    [InlineData("Cpu", "UsingAliasKernels")]
    [InlineData("Cuda", "GlobalAliasKernels")]
    [InlineData("Cpu", "GlobalAliasKernels")]
    public void Test_AliasedAttribute_GeneratesWrapper(string dialect, string className)
    {
        var compilation = CreateCompilation(
            Parse(RoslynTestUtility.ReadTestCase("Generators/GpuKernel_GlobalAlias_Usings.cs"), "usings.cs"),
            Parse(RoslynTestUtility.ReadTestCase("Generators/GpuKernel_UsingAlias.cu.cs"), "using.cs"),
            Parse(RoslynTestUtility.ReadTestCase("Generators/GpuKernel_GlobalAlias.cu.cs"), "global.cs"));
        var result = CreateDriver(dialect).RunGenerators(compilation).GetRunResult().Results.Single();
        Assert.Null(result.Exception);

        var wrapper = GetSource(result, $"{className}.g.cs");
        Assert.Contains("LaunchFillKernel(", wrapper, StringComparison.Ordinal);
    }

    // The warmups follow the kernels, not the classes: OuterKernels is split by MiddleKernels
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ModuleContext_WarmsUpInKernelOrder()
    {
        var compilation = CreateCompilation(
            Parse(RoslynTestUtility.ReadTestCase("Generators/GpuKernel_InterleavedClasses_A1.cu.cs"), "a1.cs"),
            Parse(RoslynTestUtility.ReadTestCase("Generators/GpuKernel_InterleavedClasses_B.cu.cs"), "b.cs"),
            Parse(RoslynTestUtility.ReadTestCase("Generators/GpuKernel_InterleavedClasses_A2.cu.cs"), "c.cs"));
        var result = CreateDriver("Cuda").RunGenerators(compilation).GetRunResult().Results.Single();
        Assert.Null(result.Exception);

        var context = GetSource(result, "GSharpModuleContext_OutputTest.g.cs");
        var first = context.IndexOf("OuterKernels.WarmupFunctionPointerFirstKernel();", StringComparison.Ordinal);
        var middle = context.IndexOf("MiddleKernels.WarmupFunctionPointerMiddleKernel();", StringComparison.Ordinal);
        var last = context.IndexOf("OuterKernels.WarmupFunctionPointerLastKernel();", StringComparison.Ordinal);
        Assert.True(first >= 0 && first < middle && middle < last, context);
    }

    // Without an assembly name, the module context is named after UnknownAssembly, and the wrappers name the module
    // context after the assembly symbol's name, as before the outputs were split
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_NoAssemblyName_KeepsBothNames()
    {
        var compilation = CSharpCompilation.Create(assemblyName: null)
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(Parse(RoslynTestUtility.ReadTestCase("Generators/GpuKernel_TwoClasses.cu.cs"), "two.cs"));
        var result = CreateDriver("Cuda").RunGenerators(compilation).GetRunResult().Results.Single();
        Assert.Null(result.Exception);

        Assert.Contains(result.GeneratedSources,
            static s => s.HintName == "GSharpModuleContext_UnknownAssembly.g.cs");
        var wrapper = GetSource(result, "FirstKernels.g.cs");
        Assert.Contains(
            $"global::Kassyi.GSharp.Generated.GSharpModuleContext_{compilation.Assembly.Name}.GetOrLoadModule();",
            wrapper,
            StringComparison.Ordinal);
    }

    // The managed fallback copies the kernel body into a generated file without the user's using directives: names bound
    // to types, namespaces and the static members of using static come out fully qualified (#433), an alias (C# 12: of
    // any type) as what it names, an extension method call as the static call, and nameof as its string, which an alias
    // would otherwise change
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ManagedBody_QualifiesNamesFromUsingDirectives()
    {
        var compilation = CreateCompilation(
            Parse(RoslynTestUtility.ReadTestCase("Generators/ManagedQualification.cs"), "qualification.cs"));
        var driver = CreateDriver("Cpu").RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        // The generator reports only its own failures (GSH0048); typeof and the pointer are the analyzer's to report
        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);

        var wrapper = GetSource(result, "CaseKernels.g.cs");
        Assert.Contains("public global::ManagedQualificationCase.Types.CasePair pair;", wrapper, StringComparison.Ordinal);
        Assert.Contains("new global::ManagedQualificationCase.Types.CasePair", wrapper, StringComparison.Ordinal);
        Assert.Contains("global::ManagedQualificationCase.CaseMath.Triple(values[", wrapper, StringComparison.Ordinal);
        Assert.Contains("global::System.Math.Max(", wrapper, StringComparison.Ordinal);
        // The alias CaseAlias is written as the class it stands for
        Assert.Contains("global::ManagedQualificationCase.CaseMath.Triple(1)", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("CaseAlias.", wrapper, StringComparison.Ordinal);
        Assert.Contains("global::Kassyi.GSharp.Core.ThreadIndex.X", wrapper, StringComparison.Ordinal);
        // An extension method found through a using directive becomes a static call; a ref this receiver keeps ref
        Assert.Contains("global::ManagedQualificationCase.Types.CaseExtensions.Doubled(values[", wrapper,
            StringComparison.Ordinal);
        Assert.Contains("global::ManagedQualificationCase.Types.CaseExtensions.Bump(ref __gsharp_ctx.bumped)", wrapper,
            StringComparison.Ordinal);
        // nameof keeps the value it has in the user's file ("CaseAlias"); an interpolation hole is qualified as any name
        Assert.Contains("\"CaseAlias\".Length", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(CaseAlias)", wrapper, StringComparison.Ordinal);
        // In parentheses: {global::N.C} would read global with a format
        Assert.Contains("$\"{(global::ManagedQualificationCase.CaseMath.Triple(2))}\"", wrapper, StringComparison.Ordinal);
        // C# 12 aliases of any type: an array, a pointer and a tuple whose element names stay usable
        Assert.Contains("int[] copy = values;", wrapper, StringComparison.Ordinal);
        Assert.Contains("int* pointer = null;", wrapper, StringComparison.Ordinal);
        Assert.Contains("(int A, int B) tuple =", wrapper, StringComparison.Ordinal);
        Assert.Contains("is global::ManagedQualificationCase.Types.CasePair matched", wrapper, StringComparison.Ordinal);
        Assert.Contains("typeof(global::ManagedQualificationCase.Types.CasePair)", wrapper, StringComparison.Ordinal);
        Assert.Empty(output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    private static string GetSource(GeneratorRunResult result, string hintNameSuffix) =>
        result.GeneratedSources
            .Single(s => s.HintName.EndsWith(hintNameSuffix, StringComparison.Ordinal))
            .SourceText
            .ToString();

    private static SyntaxTree Parse(string source, string path) =>
        CSharpSyntaxTree.ParseText(source, s_parseOptions, path: path);

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] trees) =>
        CSharpCompilation.Create("OutputTest")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(trees);

    private static GeneratorDriver CreateDriver(string dialect) =>
        CSharpGeneratorDriver.Create(
            generators: [new GpuKernelGenerator().AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: s_parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(dialect),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
}
