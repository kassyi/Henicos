using Kassyi.GSharp.Compiler.Aot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// The artifact manifest (<c>Kassyi.GSharp.Kernels.artifacts</c>) names the native sources the generator wrote, so that
/// the build compiles only those and not every <c>.cu</c> / <c>.cpp</c> left in <c>obj/</c>.
/// </summary>
[Category(TestCategory.Compiler)]
public class AotArtifactManifestTests
{
    private const string AssemblyName = "MegaTestCompilationBase";

    private static readonly string s_kernelSource = RoslynTestUtility.ReadTestCase("Generator/NativeModuleAssembly.cu.cs");
    private static readonly string s_noKernelSource = RoslynTestUtility.ReadTestCase("Generator/AotManifest_NoKernels.cs");

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cuda", ".g.cu")]
    [InlineData("Cpu", ".g.cpp")]
    public void Test_Kernels_ManifestNamesTheWrittenSource(string dialect, string extension)
    {
        using var dir = new TempDirectory();

        RoslynTestUtility.RunGenerator(s_kernelSource, dialect, dir.Options);

        var sourceName = $"Kassyi.GSharp.Kernels_{AssemblyName}{extension}";
        Assert.True(File.Exists(Path.Combine(dir.Path, sourceName)));
        Assert.Equal([sourceName], File.ReadAllLines(dir.ManifestPath));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_NoKernels_ManifestIsEmpty()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.ManifestPath, "Kassyi.GSharp.Kernels_Old.g.cpp\n");

        RoslynTestUtility.RunGenerator(s_noKernelSource, "Cuda", dir.Options);

        Assert.True(File.Exists(dir.ManifestPath));
        Assert.Empty(File.ReadAllLines(dir.ManifestPath));
    }

    // An IDE worker (a compilation that references another compilation) must not touch obj/
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(true)]
    [InlineData(false)]
    public void Test_DesignTimeCompilation_WritesNoManifest(bool hasKernels)
    {
        using var dir = new TempDirectory();
        var compilation = RoslynTestUtility.CreateCompilation(hasKernels ? s_kernelSource : s_noKernelSource)
            .AddReferences(CSharpCompilation.Create("OtherProject").ToMetadataReference());

        RoslynTestUtility.RunGenerator(compilation, "Cuda", dir.Options);

        Assert.False(File.Exists(dir.ManifestPath));
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    // The manifest is written last: when the source cannot be written, the old manifest must not name the new source
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_SourceWriteFails_ManifestIsNotUpdatedAndFailureIsReported()
    {
        using var dir = new TempDirectory();
        const string OldManifest = "Kassyi.GSharp.Kernels_Old.g.cpp\n";
        File.WriteAllText(dir.ManifestPath, OldManifest);
        // A directory with the source's name makes the source write fail
        var sourcePath = Path.Combine(dir.Path, $"Kassyi.GSharp.Kernels_{AssemblyName}.g.cu");
        Directory.CreateDirectory(sourcePath);

        var (_, _, diagnostics) = RoslynTestUtility.RunGenerator(s_kernelSource, "Cuda", dir.Options);

        var failure = Assert.Single(diagnostics, static d => d.Id == GSharpDiagnostics.AotArtifactWriteFailed.Id);
        Assert.Equal(DiagnosticSeverity.Info, failure.Severity);
        Assert.Contains(sourcePath, failure.GetMessage(), StringComparison.Ordinal);
        Assert.Equal(OldManifest, File.ReadAllText(dir.ManifestPath));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            Directory.CreateDirectory(Path);
            Options = new Dictionary<string, string>
            {
                { "build_property.IntermediateOutputPath", "." },
                { "build_property.MSBuildProjectDirectory", Path }
            };
        }

        public string Path { get; }

        public string ManifestPath => System.IO.Path.Combine(Path, "Kassyi.GSharp.Kernels.artifacts");

        public Dictionary<string, string> Options { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
