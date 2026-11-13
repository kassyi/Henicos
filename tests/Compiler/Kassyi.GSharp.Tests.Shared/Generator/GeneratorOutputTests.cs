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
            ("partial.cs", RoslynTestUtility.ReadTestCase("Generators/" + testCase)));
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
            ("usings.cs", RoslynTestUtility.ReadTestCase("Generators/GpuKernel_GlobalAlias_Usings.cs")),
            ("using.cs", RoslynTestUtility.ReadTestCase("Generators/GpuKernel_UsingAlias.cu.cs")),
            ("global.cs", RoslynTestUtility.ReadTestCase("Generators/GpuKernel_GlobalAlias.cu.cs")));
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
            ("a1.cs", RoslynTestUtility.ReadTestCase("Generators/GpuKernel_InterleavedClasses_A1.cu.cs")),
            ("b.cs", RoslynTestUtility.ReadTestCase("Generators/GpuKernel_InterleavedClasses_B.cu.cs")),
            ("c.cs", RoslynTestUtility.ReadTestCase("Generators/GpuKernel_InterleavedClasses_A2.cu.cs")));
        var result = CreateDriver("Cuda").RunGenerators(compilation).GetRunResult().Results.Single();
        Assert.Null(result.Exception);

        var context = GetSource(result, "GSharpModuleContext_OutputTest.g.cs");
        var first = context.IndexOf("OuterKernels.WarmupFunctionPointerFirstKernel();", StringComparison.Ordinal);
        var middle = context.IndexOf("MiddleKernels.WarmupFunctionPointerMiddleKernel();", StringComparison.Ordinal);
        var last = context.IndexOf("OuterKernels.WarmupFunctionPointerLastKernel();", StringComparison.Ordinal);
        Assert.True(first >= 0 && first < middle && middle < last, context);
    }

    // The module context class and the wrappers that name it take one module name: the assembly name, or
    // UnknownAssembly without one. Its identifier keeps ASCII letters and digits and escapes every other character
    // injectively ('_' as _u, any other as _x and six hex digits, as the C++ names), and the artifact files, which the
    // native build names after the generated sources, escape it the same way
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("my-lib", "GSharpModuleContext_my_x00002dlib", "\"Kassyi.GSharp.Kernels_my_x00002dlib.gfatbin\"")]
    [InlineData("my_lib", "GSharpModuleContext_my_ulib", "\"Kassyi.GSharp.Kernels_my_ulib.gfatbin\"")]
    [InlineData(
        "My.Lib-2",
        "GSharpModuleContext_My_x00002eLib_x00002d2",
        "\"Kassyi.GSharp.Kernels_My_x00002eLib_x00002d2.gfatbin\""
    )]
    [InlineData(null, "GSharpModuleContext_UnknownAssembly", "\"Kassyi.GSharp.Kernels_UnknownAssembly.gfatbin\"")]
    [InlineData(
        "\u65e5\u672c",
        "GSharpModuleContext__x0065e5_x00672c",
        "\"Kassyi.GSharp.Kernels__x0065e5_x00672c.gfatbin\""
    )]
    [InlineData(
        "\u00e9t\u00e9",
        "GSharpModuleContext__x0000e9t_x0000e9",
        "\"Kassyi.GSharp.Kernels__x0000e9t_x0000e9.gfatbin\""
    )]
    [InlineData(
        "a{b}",
        "GSharpModuleContext_a_x00007bb_x00007d",
        "\"Kassyi.GSharp.Kernels_a_x00007bb_x00007d.gfatbin\""
    )]
    [InlineData("a\"b", "GSharpModuleContext_a_x000022b", "\"Kassyi.GSharp.Kernels_a_x000022b.gfatbin\"")]
    public void ModuleContext_AssemblyNameNotAnIdentifier_WrappersNameTheGeneratedContext(
        string? assemblyName,
        string contextName,
        string fatbinLiteral)
    {
        var (result, _, errors) = RunAndCompile(assemblyName, "Cuda");

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        var context = GetSource(result, $"{contextName}.g.cs");
        Assert.Contains($"public static class {contextName}", context, StringComparison.Ordinal);
        Assert.Contains($"Combine(baseDir, {fatbinLiteral});", context, StringComparison.Ordinal);
        Assert.Contains(
            $"global::Kassyi.GSharp.Generated.{contextName}.GetFatbin);",
            GetSource(result, "FirstKernels.g.cs"),
            StringComparison.Ordinal);
    }

    // An assembly a-b that references an assembly a_b gets a module context of its own: when both names gave
    // GSharpModuleContext_a_b, the referencing assembly declared the class that the referenced one exports (CS0436)
    // and its launchers could not tell the two modules apart
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ModuleContext_ReferencedAssemblyNameDiffersOnlyInPunctuation_EachAssemblyNamesItsOwnContext()
    {
        var (referencedResult, referencedOutput, referencedErrors) = RunAndCompile("a_b", "Cuda");
        Assert.True(referencedErrors.Count == 0, string.Join(Environment.NewLine, referencedErrors));

        var (result, output, errors) = RunAndCompile("a-b", "Cuda", referencedOutput.ToMetadataReference());

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        Assert.Contains("public static class GSharpModuleContext_a_ub", GetSource(referencedResult,
            "GSharpModuleContext_a_ub.g.cs"), StringComparison.Ordinal);
        Assert.Contains("public static class GSharpModuleContext_a_x00002db", GetSource(result,
            "GSharpModuleContext_a_x00002db.g.cs"), StringComparison.Ordinal);
        Assert.Contains("global::Kassyi.GSharp.Generated.GSharpModuleContext_a_x00002db.GetFatbin);",
            GetSource(result, "FirstKernels.g.cs"), StringComparison.Ordinal);
        var contextConflicts = output.GetDiagnostics()
            .Where(static d => d.Id == "CS0436" && d.GetMessage().Contains("GSharpModuleContext"))
            .Select(static d => d.ToString())
            .ToList();
        Assert.True(contextConflicts.Count == 0, string.Join(Environment.NewLine, contextConflicts));
    }

    // Assemblies whose names differ only in punctuation load native artifacts of their own: My.Lib and My_Lib both
    // loaded Kassyi.GSharp.Kernels_My_Lib (the native library on the CPU, the fatbin on CUDA) when only '.' was replaced
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu", "{__gsharp_arch_suffix}.dll\"")]
    [InlineData("Cuda", ".gfatbin\"")]
    public void ArtifactFiles_AssemblyNamesDifferingInPunctuation_LoadTheirOwnArtifacts(string dialect, string suffix)
    {
        var (dotted, _, dottedErrors) = RunAndCompile("My.Lib", dialect);
        var (underscored, _, underscoredErrors) = RunAndCompile("My_Lib", dialect);

        Assert.True(dottedErrors.Count == 0, string.Join(Environment.NewLine, dottedErrors));
        Assert.True(underscoredErrors.Count == 0, string.Join(Environment.NewLine, underscoredErrors));
        var dottedText = string.Concat(dotted.GeneratedSources.Select(static s => s.SourceText.ToString()));
        var underscoredText = string.Concat(underscored.GeneratedSources.Select(static s => s.SourceText.ToString()));
        Assert.Contains("Kassyi.GSharp.Kernels_My_x00002eLib" + suffix, dottedText, StringComparison.Ordinal);
        Assert.Contains("Kassyi.GSharp.Kernels_My_uLib" + suffix, underscoredText, StringComparison.Ordinal);
        Assert.DoesNotContain("Kassyi.GSharp.Kernels_My_uLib", dottedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Kassyi.GSharp.Kernels_My_x00002eLib", underscoredText, StringComparison.Ordinal);
    }

    // The CPU wrappers load the native library that the artifact of the same module name is built into, named with the
    // escaped module name
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("my-lib", "Kassyi.GSharp.Kernels_my_x00002dlib")]
    [InlineData(null, "Kassyi.GSharp.Kernels_UnknownAssembly")]
    [InlineData("\u65e5\u672c", "Kassyi.GSharp.Kernels__x0065e5_x00672c")]
    [InlineData("a{b}", "Kassyi.GSharp.Kernels_a_x00007bb_x00007d")]
    [InlineData("a\"b", "Kassyi.GSharp.Kernels_a_x000022b")]
    public void NativeLibraryPath_AssemblyNameNotAnIdentifier_NamesTheArtifactLibrary(
        string? assemblyName,
        string artifactStem)
    {
        var (result, _, errors) = RunAndCompile(assemblyName, "Cpu");

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        var dllLine = string.Empty;
        foreach (var line in GetSource(result, "FirstKernels.g.cs").AsSpan().EnumerateLines())
        {
            if (line.Contains("Kassyi.GSharp.Kernels_".AsSpan(), StringComparison.Ordinal))
            {
                dllLine = line.ToString();
                break;
            }
        }

        Assert.NotEmpty(dllLine);
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
            ("qualification.cs", RoslynTestUtility.ReadTestCase("Generators/ManagedQualification.cs")));
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
            ("forms.cs", RoslynTestUtility.ReadTestCase("Generators/ManagedOperatorMacroForms.cs")));
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

        var compilation = RoslynTestUtility.CreateCompilationAt(
            filePath,
            RoslynTestUtility.ReadTestCase("Generators/TypeKeyedSharedMemory_PathMap.cs"))
            .WithAssemblyName("PathMapTest")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true)
                .WithSourceReferenceResolver(resolver));

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

        var compilation = CreateCompilation(
            (filePath, RoslynTestUtility.ReadTestCase("Generators/TypeKeyedSharedMemory_LineDirective.cs")));
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
    private static (GeneratorRunResult Result, Compilation Output, List<string> Errors) RunAndCompile(
        string? assemblyName, string dialect, params MetadataReference[] references)
    {
        var compilation = RoslynTestUtility.CreateCompilationAt(
            ("two.cs", RoslynTestUtility.ReadTestCase("Generators/GpuKernel_TwoClasses.cu.cs")))
            .WithAssemblyName(assemblyName)
            .AddReferences(references);
        var driver = CreateDriver(dialect).RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);

        var errors = output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToList();
        return (result, output, errors);
    }

    private static CSharpCompilation CreateCompilation(params (string Path, string Source)[] files) =>
        RoslynTestUtility.CreateCompilationAt(files).WithAssemblyName("OutputTest");

    private static GeneratorDriver CreateDriver(string dialect) =>
        CSharpGeneratorDriver.Create(
            generators: [new GpuKernelGenerator().AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: s_parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(dialect),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
}
