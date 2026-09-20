using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests.Integration;

public class MSBuildTransitiveCopyTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testRoot;
    private readonly string _repoRoot;

    public MSBuildTransitiveCopyTests(ITestOutputHelper output)
    {
        _output = output;
        _testRoot = Path.Combine(Path.GetTempPath(), "GSharpIntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);

        // Find repo root (assuming test assembly is in tests/Kassyi.GSharp.Tests.Integration/bin/Debug/net10.0)
        var currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        _repoRoot = Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..", "..", ".."));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    [Fact]
    [Trait("FeatureId", "GEN-55")]
    public async Task Test_GEN_55_NativeAssets_ShouldFlowTransitivelyToConsumer()
    {
        var producerDir = Path.Combine(_testRoot, "Producer");
        var consumerDir = Path.Combine(_testRoot, "Consumer");
        Directory.CreateDirectory(producerDir);
        Directory.CreateDirectory(consumerDir);

        var targetsPath = Path.Combine(_repoRoot, "src", "Compiler", "Kassyi.GSharp.Compiler", "build", "Kassyi.GSharp.Compiler.targets");
        var taskAssemblyPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Kassyi.GSharp.Compiler.Tasks.dll");

        // 1. Create Producer
        var producerProject = $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <GSharpMockCompilation>true</GSharpMockCompilation>
            <GSharpCompilerTaskAssembly>{taskAssemblyPath}</GSharpCompilerTaskAssembly>
          </PropertyGroup>

          <ItemGroup>
            <!-- No manual Compile includes needed; files will be in obj folder -->
          </ItemGroup>

          <Import Project="{targetsPath}" />
        </Project>
        """;

        File.WriteAllText(Path.Combine(producerDir, "Producer.csproj"), producerProject);
        
        var producerObjDir = Path.Combine(producerDir, "obj", "Debug", "net10.0");
        Directory.CreateDirectory(producerObjDir);
        File.WriteAllText(Path.Combine(producerObjDir, "Dummy.cu"), "// Dummy CUDA");
        File.WriteAllText(Path.Combine(producerObjDir, "Dummy.cpp"), "// Dummy CPP");

        // 2. Create Consumer
        var consumerProject = $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="..\Producer\Producer.csproj" />
          </ItemGroup>
        </Project>
        """;

        File.WriteAllText(Path.Combine(consumerDir, "Consumer.csproj"), consumerProject);

        // 3. Build Consumer
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build Consumer.csproj -c Debug -nr:false",
            WorkingDirectory = consumerDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(60000))
        {
            process.Kill();
            throw new TimeoutException("Build process timed out.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        
        _output.WriteLine("=== STDOUT ===");
        _output.WriteLine(stdout);
        _output.WriteLine("=== STDERR ===");
        _output.WriteLine(stderr);

        process.ExitCode.Should().Be(0, "Consumer project build should succeed");

        // 4. Assert assets are copied to Consumer output
        var consumerOutputDir = Path.Combine(consumerDir, "bin", "Debug", "net10.0");
        
        // From Dummy.cu
        File.Exists(Path.Combine(consumerOutputDir, "Dummy.fatbin")).Should().BeTrue("Fatbin should be copied transitively");
        
        // From Dummy.cpp (Architecture suffixes x64 and arm64 should be generated in Mock mode if MultiTarget is true, or just host arch)
        // Since we didn't specify GSharpMultiTarget, it defaults to false and uses host arch (x64 typically).
        var cppArchs = new[] { "x64", "arm64" };
        var foundAnyDll = false;
        foreach (var arch in cppArchs)
        {
            if (File.Exists(Path.Combine(consumerOutputDir, $"Dummy_{arch}.dll")))
            {
                foundAnyDll = true;
                break;
            }
        }
        
        foundAnyDll.Should().BeTrue("Native DLL should be copied transitively");
    }

    [Fact]
    [Trait("FeatureId", "GEN-55")]
    public async Task Test_GEN_55_MockCompilationFailure_ShouldFailBuild()
    {
        var producerDir = Path.Combine(_testRoot, "ProducerFail");
        Directory.CreateDirectory(producerDir);

        var targetsPath = Path.Combine(_repoRoot, "src", "Compiler", "Kassyi.GSharp.Compiler", "build", "Kassyi.GSharp.Compiler.targets");
        // Give a completely invalid path to the assembly to force MSBuild task failure.
        var taskAssemblyPath = Path.Combine(producerDir, "NonExistent.dll");

        var producerProject = $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <GSharpMockCompilation>true</GSharpMockCompilation>
            <GSharpCompilerTaskAssembly>{taskAssemblyPath}</GSharpCompilerTaskAssembly>
          </PropertyGroup>
          <Import Project="{targetsPath}" />
        </Project>
        """;

        File.WriteAllText(Path.Combine(producerDir, "ProducerFail.csproj"), producerProject);
        
        var producerObjDir = Path.Combine(producerDir, "obj", "Debug", "net10.0");
        Directory.CreateDirectory(producerObjDir);
        File.WriteAllText(Path.Combine(producerObjDir, "Dummy.cu"), "// Dummy CUDA");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build ProducerFail.csproj -c Debug -nr:false",
            WorkingDirectory = producerDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(60000))
        {
            process.Kill();
            throw new TimeoutException("Build process timed out.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        _output.WriteLine("=== STDOUT ===");
        _output.WriteLine(stdout);
        _output.WriteLine("=== STDERR ===");
        _output.WriteLine(stderr);

        process.ExitCode.Should().NotBe(0, "Build should fail when task assembly is invalid or missing");
        stdout.Should().Contain("MSB4062", "MSBuild should report missing task assembly error");
    }
}
