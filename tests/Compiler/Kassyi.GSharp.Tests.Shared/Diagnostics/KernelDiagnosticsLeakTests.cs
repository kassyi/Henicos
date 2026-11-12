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
[Collection(TestCollections.CorpusScan)]
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
    public async Task AnalyzerDiagnostics_StatedFailures_ReportsEveryFailure(string input, string dialect)
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
    public async Task GeneratorDiagnostics_StatedFailures_FoundInGeneratedSource(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase("Generator/SadPathDiagnostics.cu.cs"));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        Assert.Equal(
            [("(11,13): GSH0002", true), ("(12,26): GSH0001", true)],
            KernelDiagnostics.FindStatedFailures(run));
    }

    // The kernels of the standard library build in both dialects with nothing stated as #error. Both dialects share one
    // compilation of the library, which the test does not keep: it is bound in full and takes hundreds of MB.
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task StdlibKernels_DiagnosticAudit_HasNoUnreportedFailure()
    {
        var compilation = CreateStdlibCompilation();

        await AssertNoUnreportedFailureAsync(compilation, "Cuda");
        await AssertNoUnreportedFailureAsync(compilation, "Cpu");
    }

    private static async Task AssertNoUnreportedFailureAsync(Compilation compilation, string dialect)
    {
        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

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

    // The project's own sources and the sources of its other generator, without the built Stdlib assembly
    private static Compilation CreateStdlibCompilation()
    {
        var projectDir = RepositoryPaths.Combine("src", "Stdlib", "Kassyi.GSharp.Stdlib.GpuMath");
        var files = new List<(string Path, string Source)>
        {
            ("ImplicitUsings.g.cs",
                "global using System; global using System.Collections.Generic; global using System.IO; " +
                "global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;")
        };

        var sources = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(static f => !IsBuildOutput(f));
        var generated = Directory.EnumerateDirectories(Path.Combine(projectDir, "obj"), "Kassyi.GSharp.Stdlib.GpuMath.Generators",
                SearchOption.AllDirectories)
            .Take(1)
            .SelectMany(static d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories));
        foreach (var file in sources.Concat(generated).Order(StringComparer.Ordinal))
        {
#pragma warning disable RS0030 // Category 3: the tree paths use / on every OS, so the output does not depend on it
            var path = Path.GetRelativePath(projectDir, file).Replace('\\', '/');
#pragma warning restore RS0030
            files.Add((path, File.ReadAllText(file)));
        }

        var compilation = RoslynTestUtility.CreateCompilationAt(files.ToArray());
        compilation = compilation
            .WithAssemblyName("Kassyi.GSharp.Stdlib.GpuMath")
            .WithOptions(compilation.Options.WithNullableContextOptions(NullableContextOptions.Enable));

        // The base compilation references the built library (twice: from the test output and explicitly)
        var referencesToRemove = compilation.References
            .Where(static r => r is PortableExecutableReference pe &&
                               pe.GetMetadata() is AssemblyMetadata metadata &&
                               metadata.GetModules()[0].Name == "Kassyi.GSharp.Stdlib.GpuMath.dll")
            .ToArray();

        return compilation.RemoveReferences(referencesToRemove);
    }

    private static bool IsBuildOutput(string file)
    {
        var separator = Path.DirectorySeparatorChar;
        return file.Contains($"{separator}obj{separator}", StringComparison.Ordinal) ||
               file.Contains($"{separator}bin{separator}", StringComparison.Ordinal);
    }
}
