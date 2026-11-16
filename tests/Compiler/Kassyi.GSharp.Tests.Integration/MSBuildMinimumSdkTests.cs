#nullable enable
using System.Reflection;
using FluentAssertions;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests.Integration;

/// <summary>
/// Kassyi.GSharp.Compiler.targets stops a build on a .NET SDK older than 10.0.400 with error GS0005, before the C#
/// compilation, where the source generator built against Roslyn 5.9 could not load.
/// </summary>
[Category(TestCategory.Compiler)]
public class MSBuildMinimumSdkTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _projectDir;

    public MSBuildMinimumSdkTests(ITestOutputHelper output)
    {
        _output = output;
        _projectDir = Path.Combine(Path.GetTempPath(), "GSharpIntegrationTests", Guid.NewGuid().ToString("N"), "Sdk");
        Directory.CreateDirectory(_projectDir);
        WriteProject();
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
    public async Task Build_SdkOlderThanTheMinimum_FailsWithGs0005()
    {
        var result = await BuildAsync("10.0.303");

        result.ExitCode.Should().NotBe(0, Describe(result));
        result.Stdout.Should().Contain("error GS0005: G# needs the .NET SDK 10.0.400 or later");
        result.Stdout.Should().Contain("this build uses 10.0.303.");
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Build_TheMinimumSdk_Succeeds()
    {
        var result = await BuildAsync("10.0.400");

        result.ExitCode.Should().Be(0, Describe(result));
        result.Stdout.Should().NotContain("GS0005");
    }

    private void WriteProject()
    {
        var targetsPath = RepositoryPaths.Combine(
            "src", "Compiler", "Kassyi.GSharp.Compiler", "build", "Kassyi.GSharp.Compiler.targets"
        );
        var taskAssemblyPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory,
            "Kassyi.GSharp.Compiler.Tasks.dll"
        );
        File.WriteAllText(Path.Combine(_projectDir, "Sdk.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <GSharpCompilerTaskAssembly>{taskAssemblyPath}</GSharpCompilerTaskAssembly>
              </PropertyGroup>
              <Import Project="{targetsPath}" />
            </Project>
            """);
    }

    // The SDK version is passed as a global property, which overrides the value the SDK sets, so the check sees it
    private Task<BuildResult> BuildAsync(string sdkVersion) =>
        BuildProcessRunner.RunBuildAsync(
            _projectDir,
            "Sdk.csproj",
            new Dictionary<string, string?> { ["GSHARP_TEST_NO_CUDA"] = "1" },
            [$"-p:NETCoreSdkVersion={sdkVersion}"]
        );

    private static string Describe(BuildResult result) =>
        $"STDOUT:\n{result.Stdout}\nSTDERR:\n{result.Stderr}";
}
