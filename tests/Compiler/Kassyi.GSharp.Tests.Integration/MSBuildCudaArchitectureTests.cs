#nullable enable
using System.Reflection;
using FluentAssertions;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests.Integration;

/// <summary>
/// Kassyi.GSharp.Compiler.targets checks <c>GSharpTargetArchitectures</c> in every build, also when the CUDA
/// compilation is skipped (<c>GSHARP_TEST_NO_CUDA</c>, <c>GSharpSkipCudaCompilation</c>) and when the project has no
/// kernels: a malformed entry fails the build with error GS0004.
/// </summary>
[Category(TestCategory.Compiler)]
public class MSBuildCudaArchitectureTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _projectDir;

    public MSBuildCudaArchitectureTests(ITestOutputHelper output)
    {
        _output = output;
        _projectDir = Path.Combine(Path.GetTempPath(), "GSharpIntegrationTests", Guid.NewGuid().ToString("N"), "Arch");
        Directory.CreateDirectory(_projectDir);
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

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Build_MalformedEntryWithoutCuda_FailsWithGs0004()
    {
        WriteProject("compute_86,sm_86;sm89", extraProperties: "");

        var result = await BuildWithoutCudaAsync();

        result.ExitCode.Should().NotBe(0, Describe(result));
        result.Stdout.Should().Contain("error GS0004: GSharpTargetArchitectures entry 'sm89'");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Build_MalformedEntryWithCudaCompilationSkipped_FailsWithGs0004()
    {
        WriteProject("sm_89,compute_89", "<GSharpSkipCudaCompilation>true</GSharpSkipCudaCompilation>");

        var result = await BuildWithoutCudaAsync();

        result.ExitCode.Should().NotBe(0, Describe(result));
        result.Stdout.Should().Contain("error GS0004: GSharpTargetArchitectures entry 'sm_89,compute_89'");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Build_ValidEntriesWithoutCuda_Succeeds()
    {
        WriteProject("sm_89;compute_86,sm_86;compute_90", extraProperties: "");

        var result = await BuildWithoutCudaAsync();

        result.ExitCode.Should().Be(0, Describe(result));
        result.Stdout.Should().NotContain("GS0004");
    }

    private void WriteProject(string architectures, string extraProperties)
    {
        var targetsPath = RepositoryPaths.Combine(
            "src", "Compiler", "Kassyi.GSharp.Compiler", "build", "Kassyi.GSharp.Compiler.targets"
        );
        var taskAssemblyPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory,
            "Kassyi.GSharp.Compiler.Tasks.dll"
        );
        File.WriteAllText(Path.Combine(_projectDir, "Arch.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <GSharpCompilerTaskAssembly>{taskAssemblyPath}</GSharpCompilerTaskAssembly>
                <GSharpTargetArchitectures>{architectures}</GSharpTargetArchitectures>
                {extraProperties}
              </PropertyGroup>
              <Import Project="{targetsPath}" />
            </Project>
            """);
    }

    private Task<BuildResult> BuildWithoutCudaAsync() =>
        BuildProcessRunner.RunBuildAsync(
            _projectDir,
            "Arch.csproj",
            new Dictionary<string, string?> { ["GSHARP_TEST_NO_CUDA"] = "1" }
        );

    private static string Describe(BuildResult result) =>
        $"STDOUT:\n{result.Stdout}\nSTDERR:\n{result.Stderr}";
}
