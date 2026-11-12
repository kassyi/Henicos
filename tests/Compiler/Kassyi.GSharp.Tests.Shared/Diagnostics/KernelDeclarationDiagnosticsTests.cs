#nullable enable
using Kassyi.GSharp.Compiler.Diagnostics.Host;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// <c>[GpuKernel]</c> is allowed only on an ordinary static method of a class (GSH0111), a kernel parameter needs a
/// device representation (GSH0112), and the generator emits nothing for a kernel that either rule rejects, so that the
/// generated C# has no declaration error that would stop the build before the analyzers report.
/// </summary>
[Category(TestCategory.Compiler)]
public class KernelDeclarationDiagnosticsTests
{
    private const string GeneratedKernelClassPrefix = "Kassyi__GSharp__Tests__Shared__TestCases__Analyzers__";

    private const string ParameterCases = "KernelParameterTypeCases.";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task KernelDeclaration_UnsupportedPlaces_ReportsGsh0111AtTheAttribute(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/KernelDeclaration_SadPath.cs", dialect);

        Assert.Equal(
            [
                (13, 6, 13, 15, Gsh0111("an operator")),
                (16, 6, 16, 15, Gsh0111("a conversion operator")),
                (19, 6, 19, 15, Gsh0111("a member of a struct")),
                (22, 6, 22, 15, Gsh0111("a member of a struct")),
                (28, 6, 28, 15, Gsh0111("a member of a struct")),
                (34, 6, 34, 15, Gsh0111("a member of an interface")),
                (37, 6, 37, 15, Gsh0111("a member of an interface")),
                (43, 32, 43, 41, Gsh0111("an accessor")),
                (45, 31, 45, 40, Gsh0111("an accessor")),
                (47, 6, 47, 15, Gsh0111("an instance method")),
                (50, 6, 50, 15, Gsh0111("an instance method")),
                (53, 6, 53, 15, Gsh0111("a [ModuleInitializer] method")),
                (56, 6, 56, 15, Gsh0111("an [UnmanagedCallersOnly] method")),
                (59, 6, 59, 15, Gsh0111("a generic method")),
                (64, 23, 64, 32, Gsh0111("a lambda")),
                (67, 10, 67, 19, Gsh0111("a local function")),
                (74, 6, 74, 15, Gsh0111("a member of a generic type")),
                (85, 6, 85, 15, Gsh0111("a method that implements an interface member")),
                (91, 6, 91, 15, Gsh0111("an extension method that C# calls by its name 'Deconstruct'")),
                (94, 6, 94, 15, Gsh0111("an extension method that C# calls by its name 'Add'")),
                (97, 6, 97, 15, Gsh0111("a method that [CollectionBuilder] names"))
            ],
            RangesAndMessages(diagnostics, GSharpDiagnostics.KernelDeclarationNotSupported.Id));
    }

    // Main is the entry point only of an application; in a library it is an ordinary kernel with a launcher
    [Theory]
    [InlineData(OutputKind.ConsoleApplication, 1)]
    [InlineData(OutputKind.DynamicallyLinkedLibrary, 0)]
    [Scenario(TestScenario.Boundary)]
    public async Task KernelDeclaration_MainOfApplicationOrLibrary_ReportsGsh0111OnlyForTheEntryPoint(
        OutputKind outputKind,
        int expectedCount)
    {
        var source = RoslynTestUtility.ReadTestCase("Analyzers/KernelDeclaration_EntryPoint.cs");
        var compilation = RoslynTestUtility.CreateCompilation(source);
        var run = await KernelDiagnostics.RunCompilationAsync(
            compilation.WithOptions(compilation.Options.WithOutputKind(outputKind)),
            "Cpu");

        var reported = run.Analyzer
            .Where(static d => d.Id == GSharpDiagnostics.KernelDeclarationNotSupported.Id)
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .ToImmutableArray();
        Assert.Equal(
            Enumerable.Repeat((8, 6, 8, 15, Gsh0111("the entry point")), expectedCount),
            RangesAndMessages(reported, GSharpDiagnostics.KernelDeclarationNotSupported.Id));
        Assert.Equal(expectedCount == 0, GeneratedText(run).Contains("LaunchMain(", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task KernelParameter_ReferencesToManagedObjects_ReportsGsh0112AtTheParameter(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/KernelParameterType_SadPath.cs", dialect);

        Assert.Equal(
            [
                (25, 39, 25, 61, Gsh0112("bag", ParameterCases + "ClassParameter", "KernelParameterBag")),
                (28, 43, 28, 70, Gsh0112("shape", ParameterCases + "InterfaceParameter", "IKernelParameterShape")),
                (31, 42, 31, 76,
                    Gsh0112("transform", ParameterCases + "DelegateParameter", "KernelParameterTransform")),
                (34, 42, 34, 58, Gsh0112("value", ParameterCases + "ObjectByReference", "object")),
                (37, 42, 37, 69, Gsh0112("bag", ParameterCases + "ExtensionReceiver", "KernelParameterBag")),
                (40, 40, 40, 51, Gsh0112("text", ParameterCases + "StringParameter", "string")),
                (43, 37, 43, 62, Gsh0112("bags", ParameterCases + "ArrayOfClass", "KernelParameterBag[]")),
                (46, 43, 46, 61, Gsh0112("pair", ParameterCases + "TupleWithReference", "(string, int)")),
                (49, 51, 49, 72,
                    Gsh0112("texts", ParameterCases + "NullableMemoryOfReferences", "Memory<string>?"))
            ],
            RangesAndMessages(diagnostics, GSharpDiagnostics.KernelParameterHasNoDeviceRepresentation.Id));
    }

    // Only the kernels that the rules accept get a launcher; the generated C# of both files compiles
    [Theory]
    [InlineData("Analyzers/KernelDeclaration_SadPath.cs",
        new[] { "KernelDeclarationOuter__Nested", "KernelDeclarationRecord", "KernelDeclarationValid" },
        new[] { "LaunchOrdinary(", "LaunchFill(", "LaunchCreate(", "LaunchInNestedClass(", "LaunchInRecord(" },
        new string[0])]
    [InlineData("Analyzers/KernelParameterType_SadPath.cs",
        new[] { "KernelParameterTypeCases" },
        new[] { "LaunchDeviceParameters(", "LaunchValueTupleParameter(" },
        new[]
        {
            "LaunchClassParameter(", "LaunchStringParameter(", "LaunchArrayOfClass(", "LaunchExtensionReceiver(",
            "LaunchTupleWithReference(", "LaunchNullableMemoryOfReferences("
        })]
    [Scenario(TestScenario.SadPath)]
    public async Task Generator_RejectedKernels_EmitsNothingForThem(
        string testCasePath,
        string[] expectedClasses,
        string[] expectedLaunchers,
        string[] absentLaunchers)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(testCasePath));
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu");

        Assert.Equal(expectedClasses, GeneratedKernelClasses(run));
        Assert.Empty(run.Output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        var generated = GeneratedText(run);
        Assert.All(expectedLaunchers, launcher => Assert.Contains(launcher, generated, StringComparison.Ordinal));
        Assert.All(absentLaunchers, launcher => Assert.DoesNotContain(launcher, generated, StringComparison.Ordinal));
    }

    // One declaration of each kind whose launcher used to be a declaration error (CS0261, CS0101, CS0246, CS0611) and
    // an unrelated direct call: the generated C# compiles, so the analyzers run on the output as csc runs them, and
    // every G# diagnostic of the file is reported
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Generator_UnsupportedKernelDeclarations_LeavesTheAnalyzerDiagnosticsOfTheProject()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/KernelDeclaration_GeneratedCode.cs"));
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu");
        var reported = await BuildLikeCscAsync(run);

        Assert.Empty(CompilerErrors(reported));
        Assert.Equal(["GeneratedCodeKernels"], GeneratedKernelClasses(run));

        Assert.Equal(
            [
                (9, 6, 9, 15, Gsh0111("a member of a struct")),
                (15, 6, 15, 15, Gsh0111("a member of a struct")),
                (21, 6, 21, 15, Gsh0111("a member of an interface")),
                (27, 6, 27, 15, Gsh0111("a member of an interface")),
                (43, 6, 43, 15, Gsh0111("a generic method")),
                (57, 10, 57, 19, Gsh0111("a member of an extension block"))
            ],
            RangesAndMessages(reported, GSharpDiagnostics.KernelDeclarationNotSupported.Id));
        Assert.Equal(
            [(50, 39, 50, 59, Gsh0112("bag", "GeneratedCodeKernels.ClassParameter", "GeneratedCodeBag"))],
            RangesAndMessages(reported, GSharpDiagnostics.KernelParameterHasNoDeviceRepresentation.Id));
        Assert.Equal(
            [(47, 38, 47, 54)],
            reported.Ranges(GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable.Id)
                .Select(static d => (d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)));
        Assert.Equal(
            [(64, 40, 64, 69)],
            reported.Ranges(GSharpDiagnostics.DirectGpuKernelInvocation.Id)
                .Select(static d => (d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)));
        Assert.Contains(
            (71, 31, 71, 39),
            reported.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "without a body")
                .Select(static d => (d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)));
    }

    // GSH0036 reports a kernel class that is not partial, or a type around it that is not; the generator writes no
    // part of it, which would be CS0260 and hide the analyzer diagnostics
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Generator_KernelClassesThatAreNotPartial_LeavesGsh0036AndTheOtherDiagnostics()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/KernelDeclaration_NonPartial.cs"));
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu");
        var reported = await BuildLikeCscAsync(run);

        Assert.Empty(CompilerErrors(reported));
        Assert.Equal(["NonPartialWitness"], GeneratedKernelClasses(run));
        Assert.Equal(
            [(7, 21, 7, 38), (13, 15, 13, 30)],
            reported.Ranges(GSharpDiagnostics.KernelClassMustBePartial.Id)
                .Select(static d => (d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)));
        Assert.Equal(
            [(30, 40, 30, 66)],
            reported.Ranges(GSharpDiagnostics.DirectGpuKernelInvocation.Id)
                .Select(static d => (d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)));
    }

    // Another file cannot reopen a file-local class: alone, its launcher would land in a different class; in two
    // files, the two generated parts would be one class with duplicate attributes (CS0579)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task KernelDeclaration_FileLocalClassesInTwoFiles_ReportsGsh0111AndGeneratesNothing()
    {
        var compilation = RoslynTestUtility.CreateCompilationAt(
            ("FileLocalA.cs", RoslynTestUtility.ReadTestCase("Analyzers/KernelDeclaration_FileLocalA.cs")),
            ("FileLocalB.cs", RoslynTestUtility.ReadTestCase("Analyzers/KernelDeclaration_FileLocalB.cs")));
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu");
        var reported = await BuildLikeCscAsync(run);

        Assert.Empty(CompilerErrors(reported));
        Assert.Empty(GeneratedKernelClasses(run));
        Assert.Equal(
            [
                (8, 6, 8, 15, Gsh0111("a member of a file-local type")),
                (8, 6, 8, 15, Gsh0111("a member of a file-local type"))
            ],
            RangesAndMessages(reported, GSharpDiagnostics.KernelDeclarationNotSupported.Id));
    }

    // Code that another generator or a tool writes (*.g.cs) is checked as well; the generator skips its kernels too
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task KernelDeclaration_InGeneratedFile_ReportsGsh0111AndGsh0112(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilationAt(
            "KernelDeclaration_InGeneratedFile.g.cs",
            RoslynTestUtility.ReadTestCase("Analyzers/KernelDeclaration_InGeneratedFile.cs"));
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);
        var reported = run.Analyzer
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .ToImmutableArray();

        Assert.Empty(GeneratedKernelClasses(run));
        Assert.Equal(
            [(8, 6, 8, 15, Gsh0111("a member of a struct"))],
            RangesAndMessages(reported, GSharpDiagnostics.KernelDeclarationNotSupported.Id));
        Assert.Equal(
            [(15, 40, 15, 52, Gsh0112("value", "GeneratedFileKernels.ObjectParameter", "object"))],
            RangesAndMessages(reported, GSharpDiagnostics.KernelParameterHasNoDeviceRepresentation.Id));
    }

    // What csc reports (CommonCompiler.CompileAndEmit): the generator's diagnostics, then the declaration errors of
    // the output compilation, generated trees included, which stop the build before any analyzer runs; without them,
    // every error of the output and the diagnostics of the analyzers run on the output
    private static async Task<ImmutableArray<ReportedDiagnostic>> BuildLikeCscAsync(KernelDiagnosticsRun run)
    {
        var generator = run.Generator.Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Generator, d));
        var declarationErrors = run.Output.GetDeclarationDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Generator, d))
            .ToList();
        if (declarationErrors.Count > 0)
        {
            return [.. generator, .. declarationErrors];
        }

        DiagnosticAnalyzer[] analyzers = [.. KernelDiagnostics.Analyzers, new GpuKernelDirectInvocationAnalyzer()];
        var analyzerDiagnostics = await run.Output
            .WithAnalyzers([.. analyzers], new AnalyzerOptions([], new TestAnalyzerConfigOptionsProvider("Cpu")))
            .GetAnalyzerDiagnosticsAsync();
        var outputErrors = run.Output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Generator, d));
        return
        [
            .. generator
                .Concat(outputErrors)
                .Concat(analyzerDiagnostics.Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d)))
                .OrderBy(static d => d.StartLine)
                .ThenBy(static d => d.StartColumn)
        ];
    }

    // The C# compiler's errors (CSxxxx) among the reported diagnostics, as text for a readable failure
    private static IReadOnlyList<string> CompilerErrors(ImmutableArray<ReportedDiagnostic> reported) =>
        reported
            .Where(static d => d.Id.StartsWith("CS", StringComparison.Ordinal))
            .Select(static d => d.ToString())
            .ToList();

    private static string Gsh0111(string placement) =>
        $"[GpuKernel] cannot be applied to {placement}. A kernel must be an ordinary static method of a class; " +
        "move the body to a [GpuDeviceFunction] method and call it from such a kernel.";

    private static string Gsh0112(string parameter, string kernel, string type) =>
        $"Parameter '{parameter}' of kernel '{kernel}' has the type '{type}', which holds " +
        "references to managed objects and has no device representation. Pass the values the kernel reads, or " +
        "structs, instead.";

    private static IReadOnlyList<(int StartLine, int StartColumn, int EndLine, int EndColumn, string Message)>
        RangesAndMessages(ImmutableArray<ReportedDiagnostic> diagnostics, string id) =>
        diagnostics
            .Where(d => d.Id == id)
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .Select(static d => (d.StartLine, d.StartColumn, d.EndLine, d.EndColumn, d.Message))
            .ToList();

    // The kernel classes the generator wrote a part of, by the hint name of their wrapper
    private static IReadOnlyList<string> GeneratedKernelClasses(KernelDiagnosticsRun run) =>
        GeneratedTrees(run)
            .Select(static tree => Path.GetFileName(tree.FilePath))
            .Where(static name => name.StartsWith(GeneratedKernelClassPrefix, StringComparison.Ordinal))
            .Select(static name => name[GeneratedKernelClassPrefix.Length..^".g.cs".Length])
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string GeneratedText(KernelDiagnosticsRun run) =>
        string.Concat(GeneratedTrees(run).Select(static tree => tree.ToString()));

    private static IEnumerable<SyntaxTree> GeneratedTrees(KernelDiagnosticsRun run)
    {
        var input = run.Input.SyntaxTrees.ToHashSet();
        return run.Output.SyntaxTrees.Where(tree => !input.Contains(tree));
    }
}
