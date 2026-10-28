using System.Reflection;
using FluentAssertions;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests.Integration;

/// <summary>
/// Kassyi.GSharp.Compiler.targets compiles the native sources that the generator lists in
/// <c>$(IntermediateOutputPath)Kassyi.GSharp.Kernels.artifacts</c>, not every <c>.cu</c> / <c>.cpp</c> under
/// <c>obj/</c>. The projects here use mock compilation and write the manifest themselves, as the generator would.
/// </summary>
[Category(TestCategory.Compiler)]
public class MSBuildArtifactManifestTests : IDisposable
{
    private const string ManifestFileName = "Kassyi.GSharp.Kernels.artifacts";

    private readonly ITestOutputHelper _output;
    private readonly string _projectDir;
    private readonly string _objDir;

    public MSBuildArtifactManifestTests(ITestOutputHelper output)
    {
        _output = output;
        _projectDir = Path.Combine(Path.GetTempPath(), "GSharpIntegrationTests", Guid.NewGuid().ToString("N"), "Producer");
        _objDir = Path.Combine(_projectDir, "obj", "Debug", "net10.0");
        Directory.CreateDirectory(_objDir);

        var targetsPath = RepositoryPaths.Combine("src", "Compiler", "Kassyi.GSharp.Compiler", "build", "Kassyi.GSharp.Compiler.targets");
        var taskAssemblyPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory,
            "Kassyi.GSharp.Compiler.Tasks.dll");
        File.WriteAllText(Path.Combine(_projectDir, "Producer.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <GSharpMockCompilation>true</GSharpMockCompilation>
                <GSharpCompilerTaskAssembly>{taskAssemblyPath}</GSharpCompilerTaskAssembly>
              </PropertyGroup>
              <Import Project="{targetsPath}" />
            </Project>
            """);
    }

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_projectDir) ?? _projectDir;
        try
        {
            Directory.Delete(root, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _output.WriteLine($"Warning: Failed to delete temporary test directory '{root}': {ex.Message}");
        }
    }

    // Switching the dialect makes the generator list the .g.cu instead of the .g.cpp: the CPU task must not compile the
    // old .g.cpp again, and IncrementalClean removes it and its DLL
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_DialectSwitch_CompilesOnlyTheListedSource_AndCleansTheOldOne()
    {
        var cppPath = WriteObjFile("X.g.cpp");
        WriteManifest("X.g.cpp");

        await BuildAsync();

        Directory.GetFiles(Path.Combine(_objDir, "GSharpNativeDlls"), "*.dll").Should().NotBeEmpty();

        WriteObjFile("X.g.cu");
        WriteManifest("X.g.cu");

        await BuildAsync();

        File.Exists(Path.Combine(_objDir, "GSharpFatbins", "X.gfatbin")).Should().BeTrue();
        var nativeDlls = Path.Combine(_objDir, "GSharpNativeDlls");
        (Directory.Exists(nativeDlls) ? Directory.GetFiles(nativeDlls, "*.dll") : []).Should().BeEmpty();
        File.Exists(cppPath).Should().BeFalse("IncrementalClean removes the source of the earlier dialect");
        Directory.GetFiles(Path.Combine(_projectDir, "bin", "Debug", "net10.0"), "X_*.dll").Should().BeEmpty();
    }

    // IncrementalClean must keep the sources and outputs of the current build
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_RepeatedBuild_KeepsTheListedSourceAndItsOutputs()
    {
        var cuPath = WriteObjFile("X.g.cu");
        var cppPath = WriteObjFile("Y.g.cpp");
        WriteManifest("X.g.cu", "Y.g.cpp");

        await BuildAsync();
        await BuildAsync();

        File.Exists(cuPath).Should().BeTrue();
        File.Exists(cppPath).Should().BeTrue();
        File.Exists(Path.Combine(_objDir, "GSharpFatbins", "X.gfatbin")).Should().BeTrue();
        Directory.GetFiles(Path.Combine(_objDir, "GSharpNativeDlls"), "Y_*.dll").Should().NotBeEmpty();
        File.Exists(Path.Combine(_objDir, ManifestFileName)).Should().BeTrue();
    }

    // A .cu in obj/ that the generator did not list (the bundle a combined fatbin writes, a file of an older build) is
    // not compiled
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_UnlistedObjSource_IsNotCompiled()
    {
        WriteObjFile("X.g.cu");
        WriteObjFile("Stray.cu");
        WriteManifest("X.g.cu");

        await BuildAsync();

        File.Exists(Path.Combine(_objDir, "GSharpFatbins", "X.gfatbin")).Should().BeTrue();
        File.Exists(Path.Combine(_objDir, "GSharpFatbins", "Stray.gfatbin")).Should().BeFalse();
    }

    // An assembly without kernels gets an empty manifest: nothing is compiled
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_EmptyManifest_CompilesNothing()
    {
        WriteObjFile("X.g.cu");
        WriteObjFile("X.g.cpp");
        WriteManifest();

        await BuildAsync();

        Directory.Exists(Path.Combine(_objDir, "GSharpFatbins")).Should().BeFalse();
        Directory.Exists(Path.Combine(_objDir, "GSharpNativeDlls")).Should().BeFalse();
    }

    private string WriteObjFile(string fileName)
    {
        var path = Path.Combine(_objDir, fileName);
        File.WriteAllText(path, "// generated");
        return path;
    }

    private void WriteManifest(params string[] fileNames) =>
        File.WriteAllLines(Path.Combine(_objDir, ManifestFileName), fileNames);

    private async Task BuildAsync()
    {
        var (exitCode, stdout, stderr) = await BuildProcessRunner.RunAsync(_projectDir, "build Producer.csproj -c Debug -nr:false");
        exitCode.Should().Be(0, $"the build should succeed. STDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }
}
