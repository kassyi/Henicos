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
    public void NativeAssets_ShouldFlowTransitivelyToConsumer()
    {
        var producerDir = Path.Combine(_testRoot, "Producer");
        var consumerDir = Path.Combine(_testRoot, "Consumer");
        Directory.CreateDirectory(producerDir);
        Directory.CreateDirectory(consumerDir);

        var targetsPath = Path.Combine(_repoRoot, "src", "Compiler", "Kassyi.GSharp.Compiler", "build", "Kassyi.GSharp.Compiler.targets");
        // We know the task is built to this location before the tests run.
        var taskAssemblyPath = Path.Combine(_repoRoot, "src", "Compiler", "Kassyi.GSharp.Compiler.Tasks", "bin", "Debug", "netstandard2.0", "Kassyi.GSharp.Compiler.Tasks.dll");

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
            Arguments = "build Consumer.csproj -c Debug",
            WorkingDirectory = consumerDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        process!.WaitForExit();

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        
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
}
