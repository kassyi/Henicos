using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
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
    public void PartialKernel_MultiplePartialDeclarations_GeneratesSingleKernel(
        string testCase,
        string className,
        string arrayName,
        string valueName)
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

    // An alias of the attribute marks a kernel as well as its spelled-out name
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cuda", "UsingAliasKernels")]
    [InlineData("Cpu", "UsingAliasKernels")]
    [InlineData("Cuda", "GlobalAliasKernels")]
    [InlineData("Cpu", "GlobalAliasKernels")]
    public void AliasedAttribute_UsingAliasDirective_GeneratesWrapper(string dialect, string className)
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
    public void ModuleContext_KernelSequence_WarmsUpInKernelOrder()
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

    // The module context class and the wrappers that name it take one module name: the assembly name, or
    // UnknownAssembly without one. Its identifier keeps [A-Za-z0-9_] and turns every other character into '_', while
    // the artifact files keep the assembly name with only '.' replaced, as the native build names them
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("my-lib", "GSharpModuleContext_my_lib", "Kassyi.GSharp.Kernels_my-lib")]
    [InlineData("My.Lib-2", "GSharpModuleContext_My_Lib_2", "Kassyi.GSharp.Kernels_My_Lib-2")]
    [InlineData(null, "GSharpModuleContext_UnknownAssembly", "Kassyi.GSharp.Kernels_UnknownAssembly")]
    public void ModuleContext_AssemblyNameNotAnIdentifier_WrappersNameTheGeneratedContext(
        string? assemblyName,
        string contextName,
        string artifactStem)
    {
        var (result, errors) = RunAndCompile(assemblyName, "Cuda");

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        var context = GetSource(result, $"{contextName}.g.cs");
        Assert.Contains($"public static class {contextName}", context, StringComparison.Ordinal);
        Assert.Contains($"\"{artifactStem}.gfatbin\"", context, StringComparison.Ordinal);
        Assert.Contains(
            $"global::Kassyi.GSharp.Generated.{contextName}.GetFatbin);",
            GetSource(result, "FirstKernels.g.cs"),
            StringComparison.Ordinal);
    }

    // The CPU wrappers load the native library that the artifact of the same module name is built into
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("my-lib", "Kassyi.GSharp.Kernels_my-lib")]
    [InlineData(null, "Kassyi.GSharp.Kernels_UnknownAssembly")]
    public void NativeLibraryPath_AssemblyNameNotAnIdentifier_NamesTheArtifactLibrary(
        string? assemblyName,
        string artifactStem)
    {
        var (result, errors) = RunAndCompile(assemblyName, "Cpu");

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        var dllLine = GetSource(result, "FirstKernels.g.cs")
            .Split('\n')
            .First(static line => line.Contains("Kassyi.GSharp.Kernels_", StringComparison.Ordinal));
        Assert.Contains(
            $"$\"{artifactStem}{{{GSharpInternalNames.Launcher.ArchSuffix}}}.dll\"",
            dllLine,
            StringComparison.Ordinal);
    }

    // The managed fallback copies the kernel body into a generated file without the user's using directives: names bound
    // to types, namespaces and the static members of using static come out fully qualified (#433), an alias (C# 12: of
    // any type) as what it names, an extension method call as the static call, and nameof as its string, which an alias
    // would otherwise change
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ManagedBody_UsingDirectives_QualifiesNamesFromUsingDirectives()
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
        Assert.Contains("global::ManagedQualificationCase.CaseMath.Triple(__gsharp_ctx->values[", wrapper, StringComparison.Ordinal);
        Assert.Contains("global::System.Math.Max(", wrapper, StringComparison.Ordinal);
        // The alias CaseAlias is written as the class it stands for
        Assert.Contains("global::ManagedQualificationCase.CaseMath.Triple(1)", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("CaseAlias.", wrapper, StringComparison.Ordinal);
        Assert.Contains("global::Kassyi.GSharp.Core.ThreadIndex.X", wrapper, StringComparison.Ordinal);
        // An extension method found through a using directive becomes a static call; a ref this receiver keeps ref
        Assert.Contains("global::ManagedQualificationCase.Types.CaseExtensions.Doubled(__gsharp_ctx->values[", wrapper,
            StringComparison.Ordinal);
        Assert.Contains("global::ManagedQualificationCase.Types.CaseExtensions.Bump(ref __gsharp_ctx->bumped)", wrapper,
            StringComparison.Ordinal);
        // nameof keeps the value it has in the user's file ("CaseAlias"); an interpolation hole is qualified as any name
        Assert.Contains("\"CaseAlias\".Length", wrapper, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(CaseAlias)", wrapper, StringComparison.Ordinal);
        // In parentheses: {global::N.C} would read global with a format
        Assert.Contains("$\"{(global::ManagedQualificationCase.CaseMath.Triple(2))}\"", wrapper, StringComparison.Ordinal);
        // C# 12 aliases of any type: an array, a pointer and a tuple whose element names stay usable
        Assert.Contains("int[] copy = __gsharp_ctx->values;", wrapper, StringComparison.Ordinal);
        Assert.Contains("int* pointer = null;", wrapper, StringComparison.Ordinal);
        Assert.Contains("(int A, int B) tuple =", wrapper, StringComparison.Ordinal);
        Assert.Contains("is global::ManagedQualificationCase.Types.CasePair matched", wrapper, StringComparison.Ordinal);
        Assert.Contains("typeof(global::ManagedQualificationCase.Types.CasePair)", wrapper, StringComparison.Ordinal);
        Assert.Empty(output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    // The managed expansion of an operator macro passes each operand that has storage by reference and any other one as a
    // value (#711): an auto-property through its backing field (an [UnsafeAccessor] field stub), a field of a value and an
    // auto-property of a value without 'in', an element returned by reference as it is. The generated code compiles
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ManagedOperatorMacro_VariousOperandForms_PassesEachFormThatCompiles()
    {
        var compilation = CreateCompilation(
            Parse(RoslynTestUtility.ReadTestCase("Generators/ManagedOperatorMacroForms.cs"), "forms.cs"));
        var driver = CreateDriver("Cpu").RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);

        var errors = output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        var wrapper = GetSource(result, "ManagedOperatorMacroFormKernels.g.cs");
        Assert.Contains("UnsafeAccessorKind.Field, Name = \"<P>k__BackingField\"", wrapper, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TypeKeyedSharedMemorySites_PathMapOption_NormalizesSourcePaths()
    {
        var baseDirectory = Path.GetFullPath(@"C:\test_project");
        var filePath = Path.Combine(baseDirectory, "Source.cs");
        const string mappedPrefix = "/_";
        const string mappedPath = "/_/Source.cs";

        var pathMap = ImmutableArray.Create(new KeyValuePair<string, string>(baseDirectory, mappedPrefix));
        var resolver = new SourceFileResolver(ImmutableArray<string>.Empty, baseDirectory, pathMap);

        var syntaxTree = Parse(
            RoslynTestUtility.ReadTestCase("Generators/TypeKeyedSharedMemory_PathMap.cs"),
            filePath);

        var compilation = CSharpCompilation.Create("PathMapTest")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true)
                .WithSourceReferenceResolver(resolver))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(syntaxTree);

        var result = CreateDriver("Cpu").RunGenerators(compilation).GetRunResult().Results.Single();
        Assert.Null(result.Exception);

        var registration = GetSource(result, "GSharpSharedTypeKeyedSites.g.cs");

        // The site is at line 9 of TypeKeyedSharedMemory_PathMap.cs
        var hash = new Kassyi.GSharp.Hashing.Fnv1A64();
        hash.Append(mappedPath);
        hash.Append(9u);
        hash.Append(".ctor");
        var expectedKey = hash.Value;

        Assert.Contains($"0x{expectedKey:x16}UL", registration, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TypeKeyedSharedMemorySites_LineDirective_UsesMappedLineNumber()
    {
        const string filePath = "source.cs";
        const string mappedFile = "mapped_file.cs";
        const uint mappedLine = 500u;

        var syntaxTree = Parse(
            RoslynTestUtility.ReadTestCase("Generators/TypeKeyedSharedMemory_LineDirective.cs"),
            filePath);

        var compilation = CreateCompilation(syntaxTree);
        var result = CreateDriver("Cpu").RunGenerators(compilation).GetRunResult().Results.Single();
        Assert.Null(result.Exception);

        var registration = GetSource(result, "GSharpSharedTypeKeyedSites.g.cs");

        var hash = new Kassyi.GSharp.Hashing.Fnv1A64();
        hash.Append(mappedFile);
        hash.Append(mappedLine);
        hash.Append(".ctor");
        var expectedKey = hash.Value;

        Assert.Contains($"0x{expectedKey:x16}UL", registration, StringComparison.Ordinal);
    }

    private static string GetSource(GeneratorRunResult result, string hintNameSuffix) =>
        result.GeneratedSources
            .Single(s => s.HintName.EndsWith(hintNameSuffix, StringComparison.Ordinal))
            .SourceText
            .ToString();

    // Runs the generator on the two-class kernel file in an assembly of the given name and compiles its output
    private static (GeneratorRunResult Result, List<string> Errors) RunAndCompile(string? assemblyName, string dialect)
    {
        var compilation = CSharpCompilation.Create(assemblyName)
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(Parse(RoslynTestUtility.ReadTestCase("Generators/GpuKernel_TwoClasses.cu.cs"), "two.cs"));
        var driver = CreateDriver(dialect).RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);

        var errors = output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();
        return (result, errors);
    }

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
