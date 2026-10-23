namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The generator reports only its own failures (GSH0000, GSH0048), and every construct it could not transpile, which it
/// states as <c>#error</c> in either dialect's output, is also reported by the analyzers at the same place (S-46).
/// </summary>
/// <remarks>
/// The analyzer runs the transpiler for the CUDA dialect only (TranspilerFailureCheck); these tests are what shows that
/// the C++ dialect's lowered output has no failure of its own that a build would only see in the native compiler.
/// </remarks>
[Category(TestCategory.Compiler)]
public class KernelDiagnosticsLeakTests
{
    private static readonly string[] s_generatorIds =
    [
        GSharpDiagnostics.AotArtifactWriteFailed.Id,
        GSharpDiagnostics.InternalCompilerError.Id
    ];

    public static TheoryData<string, string> SadPathInputs()
    {
        var data = new TheoryData<string, string>();
        foreach (var input in KernelDiagnosticsInventoryTests.Inputs)
        {
            data.Add(input, "Cpu");
            data.Add(input, "Cuda");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SadPathInputs))]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_SadPathInput_EveryStatedFailureIsReportedByTheAnalyzer(string input, string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(input));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        Assert.All(run.Generator, static d => Assert.Contains(d.Id, s_generatorIds));
        Assert.Empty(KernelDiagnostics.FindStatedFailures(run).Where(static f => !f.IsReported));
    }

    // The search itself works: the kernel of SadPathDiagnostics is stated as #error in both dialects
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_StatedFailures_AreFoundInTheGeneratedSource(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase("Generator/SadPathDiagnostics.cu.cs"));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        Assert.Equal(
            [("(11,13): GSH0002", true), ("(12,26): GSH0001", true)],
            KernelDiagnostics.FindStatedFailures(run));
    }

    // The kernels of the standard library build in both dialects with nothing stated as #error
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_StdlibKernels_HaveNoUnreportedFailure(string dialect)
    {
        var run = await KernelDiagnostics.RunCompilationAsync(s_stdlibCompilation.Value, dialect);

        // The input is the real library: for its own dialect (CUDA) it compiles with the generated code. The C++ dialect
        // generates other launchers (no Ptr wrappers) and, for CooperativeMath, managed state machines that do not compile
        // (CS0102), which the library never meets; there only the failures are checked.
        if (dialect == "Cuda")
        {
            Assert.Empty(run.Output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        }

        Assert.All(run.Generator, static d => Assert.Contains(d.Id, s_generatorIds));
        Assert.Empty(KernelDiagnostics.FindStatedFailures(run).Where(static f => !f.IsReported));
    }

    private static readonly Lazy<Compilation> s_stdlibCompilation = new(CreateStdlibCompilation);

    // The project's own sources and the sources of its other generator, without the built Stdlib assembly
    private static Compilation CreateStdlibCompilation()
    {
        var projectDir = RepositoryPaths.Combine("src", "Stdlib", "Kassyi.GSharp.Stdlib.GpuMath");
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(
                "global using System; global using System.Collections.Generic; global using System.IO; " +
                "global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;",
                parseOptions,
                path: "ImplicitUsings.g.cs")
        };

        var sources = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(static f => !IsBuildOutput(f));
        var generated = Directory.EnumerateDirectories(Path.Combine(projectDir, "obj"), "Kassyi.GSharp.Stdlib.GpuMath.Generators",
                SearchOption.AllDirectories)
            .Take(1)
            .SelectMany(static d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories));
        foreach (var file in sources.Concat(generated).Order(StringComparer.Ordinal))
        {
            var path = Path.GetRelativePath(projectDir, file).Replace('\\', '/');
            trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, path: path));
        }

        // The base compilation references the built library (twice: from the test output and explicitly)
        var references = MegaCompilationCache.BaseCompilation.References
            .Where(static r => r is not PortableExecutableReference pe ||
                               pe.GetMetadata() is not AssemblyMetadata metadata ||
                               metadata.GetModules()[0].Name != "Kassyi.GSharp.Stdlib.GpuMath.dll");
        return CSharpCompilation.Create(
            "Kassyi.GSharp.Stdlib.GpuMath",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAllowUnsafe(true)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static bool IsBuildOutput(string file)
    {
        var separator = Path.DirectorySeparatorChar;
        return file.Contains($"{separator}obj{separator}", StringComparison.Ordinal) ||
               file.Contains($"{separator}bin{separator}", StringComparison.Ordinal);
    }
}
