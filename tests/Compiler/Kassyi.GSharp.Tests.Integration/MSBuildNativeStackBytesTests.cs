#nullable enable
using System.Reflection;
using FluentAssertions;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests.Integration;

[Category(TestCategory.Compiler)]
public class MSBuildNativeStackBytesTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _projectDir;

    public MSBuildNativeStackBytesTests(ITestOutputHelper output)
    {
        _output = output;
        _projectDir = Path.Combine(
            Path.GetTempPath(), "GSharpIntegrationTests", Guid.NewGuid().ToString("N"), "StackBytes");
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

    [Theory]
    [InlineData("131072")]
    [InlineData("1048576")]
    [InlineData("1073741824")]
    [Scenario(TestScenario.Boundary)]
    public async Task Build_ValidNativeStackBytes_Succeeds(string stackBytes)
    {
        WriteProject(stackBytes);

        var result = await BuildAsync();

        result.ExitCode.Should().Be(0, Describe(result));
        result.Stdout.Should().NotContain("GSharpNativeStackBytes");
    }

    [Theory]
    [InlineData("65536")]
    [InlineData("0")]
    [InlineData("131073")]
    [InlineData("0131072")]
    [InlineData("abc")]
    [InlineData("-131072")]
    [InlineData("131072.0")]
    [InlineData("1073807360")]
    [InlineData("2147483648")]
    [Scenario(TestScenario.SadPath)]
    public async Task Build_InvalidNativeStackBytes_FailsWithTargetsError(string stackBytes)
    {
        WriteProject(stackBytes);

        var result = await BuildAsync();

        result.ExitCode.Should().NotBe(0, Describe(result));
        result.Stdout.Should().Contain(
            $"GSharpNativeStackBytes value '{stackBytes}' is invalid. " +
            "Supported values are integer multiples of 65536 from 131072 to 1073741824.",
            Describe(result));
    }

    private void WriteProject(string stackBytes)
    {
        var targetsPath = RepositoryPaths.Combine(
            "src", "Compiler", "Kassyi.GSharp.Compiler", "build", "Kassyi.GSharp.Compiler.targets"
        );
        var taskAssemblyPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory,
            "Kassyi.GSharp.Compiler.Tasks.dll"
        );
        File.WriteAllText(Path.Combine(_projectDir, "Class1.cs"), "internal static class Class1 {}\n");
        File.WriteAllText(Path.Combine(_projectDir, "StackBytes.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <GSharpCompilerTaskAssembly>{taskAssemblyPath}</GSharpCompilerTaskAssembly>
                <GSharpNativeBarrierModel>Stack</GSharpNativeBarrierModel>
                <GSharpNativeStackBytes>{stackBytes}</GSharpNativeStackBytes>
                <GSharpSkipCudaCompilation>true</GSharpSkipCudaCompilation>
                <GSharpSkipCpuCompilation>true</GSharpSkipCpuCompilation>
              </PropertyGroup>
              <Import Project="{targetsPath}" />
            </Project>
            """);
    }

    private Task<BuildResult> BuildAsync() =>
        BuildProcessRunner.RunBuildAsync(
            _projectDir,
            "StackBytes.csproj",
            new Dictionary<string, string?> { ["GSHARP_TEST_NO_CUDA"] = "1" }
        );

    private static string Describe(BuildResult result) =>
        $"STDOUT:\n{result.Stdout}\nSTDERR:\n{result.Stderr}";
}
