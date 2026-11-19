#nullable enable

using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests.Integration;

[Collection(TestCollections.GlobalStateSequential)]
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class MSBuildSplitPackagingTests : IDisposable
{
    private const string ManifestFileName = "Kassyi.GSharp.Kernels.artifacts";

    private readonly ITestOutputHelper _output;
    private readonly string _rootDir;
    private readonly string _producerDir;
    private readonly string _objDir;
    private readonly string _targetsPath;
    private readonly string _taskAssemblyPath;

    public MSBuildSplitPackagingTests(ITestOutputHelper output)
    {
        _output = output;
        _rootDir = Path.Combine(
            Path.GetTempPath(),
            "GSharpIntegrationTests",
            Guid.NewGuid().ToString("N"));
        _producerDir = Path.Combine(_rootDir, "Producer");
        _objDir = Path.Combine(_producerDir, "obj", "Debug", "net10.0");
        Directory.CreateDirectory(_objDir);

        _targetsPath = RepositoryPaths.Combine(
            "src",
            "Compiler",
            "Kassyi.GSharp.Compiler",
            "build",
            "Kassyi.GSharp.Compiler.targets");
        _taskAssemblyPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory,
            "Kassyi.GSharp.Compiler.Tasks.dll");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootDir))
            {
                Directory.Delete(_rootDir, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _output.WriteLine($"Warning: Failed to delete temporary test directory '{_rootDir}': {ex.Message}");
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Build_DesignTimeBuildOnSplitArtifacts_SkipsNativeCompilation()
    {
        WriteProducerProject(mockCompilation: true, multiTarget: false);

        WriteObjFile("ModA.g.hpp", "#pragma once\n");
        WriteObjFile("ModA.g.hpp.gsym.json", "{}");
        WriteObjFile("ModA.g.cpp", "#include \"ModA.g.hpp\"\n");
        WriteObjFile("ModA.1.g.cpp", "#include \"ModA.g.hpp\"\n");
        WriteManifest("ModA.g.cpp", "ModA.1.g.cpp");

        var result = await BuildProcessRunner.RunBuildAsync(
            _producerDir,
            "Producer.csproj",
            extraArguments:
            [
                "-t:Compile",
                "-p:DesignTimeBuild=true",
                "-p:SkipCompilerExecution=true",
                "-p:ProvideCommandLineArgs=true"
            ]);

        result.ExitCode.Should().Be(0, $"DesignTimeBuild failed.\nSTDOUT:\n{result.Stdout}\nSTDERR:\n{result.Stderr}");
        Directory.Exists(Path.Combine(_objDir, "GSharpNativeDlls")).Should().BeFalse();
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public async Task Build_DesignTimeBuildAfterRealSplitBuild_PreservesObjectsAndSkipsCompile()
    {
        WriteProducerProject(mockCompilation: false, multiTarget: false);
        var arch = HostArchitecture();

        var headerPath = WriteObjFile(
            "ModSplit.g.hpp",
            "#pragma once\ninline int shared_mul(int x) { return x * 2; }\n");
        WriteObjFile("ModSplit.g.hpp.gsym.json", "{}");
        WriteObjFile("ModSplit.g.cpp", FormatUnitSource(0, "shared_mul(x) + 1"));
        WriteObjFile("ModSplit.g.cpp.gsym.json", "{}");
        var unit1Path = WriteObjFile("ModSplit.1.g.cpp", FormatUnitSource(1, "shared_mul(x) + 10"));
        WriteObjFile("ModSplit.1.g.cpp.gsym.json", "{}");
        WriteObjFile("ModSplit.partition.json", "{\"dialect\":\"Cpu\",\"B\":2,\"units\":[],\"classes\":[]}");
        WriteManifest("ModSplit.g.cpp", "ModSplit.1.g.cpp");

        var build1 = await BuildProcessRunner.RunBuildAsync(
            _producerDir,
            "Producer.csproj",
            extraArguments: ["-m:2"]);
        var dllPath = Path.Combine(_objDir, "GSharpNativeDlls", $"ModSplit_{arch}.dll");
        if (!File.Exists(dllPath) &&
            build1.Stdout.Contains("Skipping native CPU kernel compilation", StringComparison.Ordinal))
        {
            Skip.If(true, "No native C++ toolchain is available on this host.");
        }

        build1.ExitCode.Should().Be(0, $"Build 1 failed.\nSTDOUT:\n{build1.Stdout}\nSTDERR:\n{build1.Stderr}");

        var objsDir = Path.Combine(_objDir, "GSharpNativeObjs");
        var unit0Obj = FindUnitObject(objsDir, "ModSplit.g", "ModSplit");
        var unit1Obj = FindUnitObject(objsDir, "ModSplit.1.g", "ModSplit.1");

        var agedTime = DateTime.UtcNow.AddSeconds(-10);
        File.SetLastWriteTimeUtc(unit0Obj, agedTime);
        File.SetLastWriteTimeUtc(unit1Obj, agedTime);

        var designTime = await BuildProcessRunner.RunBuildAsync(
            _producerDir,
            "Producer.csproj",
            extraArguments:
            [
                "-t:Compile",
                "-p:DesignTimeBuild=true",
                "-p:SkipCompilerExecution=true",
                "-p:ProvideCommandLineArgs=true"
            ]);
        designTime.ExitCode.Should().Be(
            0,
            $"Design-time build failed.\nSTDOUT:\n{designTime.Stdout}\nSTDERR:\n{designTime.Stderr}");

        File.Exists(headerPath).Should().BeTrue();
        File.Exists(unit1Path).Should().BeTrue();
        File.GetLastWriteTimeUtc(unit0Obj).Should().Be(agedTime);
        File.GetLastWriteTimeUtc(unit1Obj).Should().Be(agedTime);

        var build3 = await BuildProcessRunner.RunBuildAsync(
            _producerDir,
            "Producer.csproj",
            extraArguments: ["-m:2"]);
        build3.ExitCode.Should().Be(0, $"Build 3 failed.\nSTDOUT:\n{build3.Stdout}\nSTDERR:\n{build3.Stderr}");
        File.GetLastWriteTimeUtc(unit0Obj).Should().Be(agedTime);
        File.GetLastWriteTimeUtc(unit1Obj).Should().Be(agedTime);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Build_TestNoCpuEnvVar_SkipsRealBuildAndRunsMockBuild()
    {
        var env = new Dictionary<string, string?> { ["GSHARP_TEST_NO_CPU"] = "1" };
        var arch = HostArchitecture();

        WriteObjFile("ModA.g.hpp", "#pragma once\n");
        WriteObjFile("ModA.g.cpp", "#include \"ModA.g.hpp\"\n");
        WriteObjFile("ModA.1.g.cpp", "#include \"ModA.g.hpp\"\n");
        WriteManifest("ModA.g.cpp", "ModA.1.g.cpp");

        WriteProducerProject(mockCompilation: false, multiTarget: false);
        var realSkipped = await BuildProcessRunner.RunBuildAsync(
            _producerDir,
            "Producer.csproj",
            environment: env);
        realSkipped.ExitCode.Should().Be(
            0,
            $"Real build with GSHARP_TEST_NO_CPU=1 failed.\n" +
            $"STDOUT:\n{realSkipped.Stdout}\nSTDERR:\n{realSkipped.Stderr}");
        var nativeDllsDir = Path.Combine(_objDir, "GSharpNativeDlls");
        (Directory.Exists(nativeDllsDir) ? Directory.GetFiles(nativeDllsDir, "*.dll") : []).Should().BeEmpty();

        WriteProducerProject(mockCompilation: true, multiTarget: false);
        var mockBuilt = await BuildProcessRunner.RunBuildAsync(
            _producerDir,
            "Producer.csproj",
            environment: env);
        mockBuilt.ExitCode.Should().Be(
            0,
            $"Mock build with GSHARP_TEST_NO_CPU=1 failed.\n" +
            $"STDOUT:\n{mockBuilt.Stdout}\nSTDERR:\n{mockBuilt.Stderr}");
        File.Exists(Path.Combine(nativeDllsDir, $"ModA_{arch}.dll")).Should().BeTrue();
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Build_MultiTargetSplitProducerConsumer_CopiesBothArchDllsWithoutLeakingObjects()
    {
        WriteProducerProject(mockCompilation: true, multiTarget: true);

        WriteObjFile("ModSplit.g.hpp", "#pragma once\n");
        WriteObjFile("ModSplit.g.hpp.gsym.json", "{}");
        WriteObjFile("ModSplit.g.cpp", "#include \"ModSplit.g.hpp\"\n");
        WriteObjFile("ModSplit.g.cpp.gsym.json", "{}");
        WriteObjFile("ModSplit.1.g.cpp", "#include \"ModSplit.g.hpp\"\n");
        WriteObjFile("ModSplit.1.g.cpp.gsym.json", "{}");
        WriteObjFile("ModSplit.partition.json", "{\"dialect\":\"Cpu\",\"B\":2,\"units\":[],\"classes\":[]}");
        WriteManifest("ModSplit.g.cpp", "ModSplit.1.g.cpp");

        var consumerDir = Path.Combine(_rootDir, "Consumer");
        Directory.CreateDirectory(consumerDir);
        File.WriteAllText(
            Path.Combine(consumerDir, "Consumer.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Producer\Producer.csproj" />
              </ItemGroup>
            </Project>
            """);

        var result = await BuildProcessRunner.RunBuildAsync(consumerDir, "Consumer.csproj");
        result.ExitCode.Should().Be(
            0,
            $"Consumer build failed.\nSTDOUT:\n{result.Stdout}\nSTDERR:\n{result.Stderr}");

        var producerDlls = Directory.GetFiles(Path.Combine(_objDir, "GSharpNativeDlls"), "*.dll")
            .Select(Path.GetFileName)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        producerDlls.Should().BeEquivalentTo(["ModSplit_arm64.dll", "ModSplit_x64.dll"]);

        var consumerBinDir = Path.Combine(consumerDir, "bin", "Debug", "net10.0");
        File.Exists(Path.Combine(consumerBinDir, "ModSplit_x64.dll")).Should().BeTrue();
        File.Exists(Path.Combine(consumerBinDir, "ModSplit_arm64.dll")).Should().BeTrue();
        Directory.GetFiles(consumerBinDir, "ModSplit.1_*").Should().BeEmpty();
        Directory.GetFiles(consumerBinDir, "*.obj").Should().BeEmpty();
        Directory.GetFiles(consumerBinDir, "*.o").Should().BeEmpty();
        Directory.GetFiles(consumerBinDir, "*.g.hpp").Should().BeEmpty();
        Directory.GetFiles(consumerBinDir, "*.g.cpp").Should().BeEmpty();
        Directory.GetFiles(consumerBinDir, "*.gsym.json").Should().BeEmpty();
        Directory.GetFiles(consumerBinDir, "*.partition.json").Should().BeEmpty();
    }

    private static string FormatUnitSource(int unitIndex, string expression) =>
        "#include \"ModSplit.g.hpp\"\n" +
        "#if !defined(_MSC_VER)\n" +
        "#define GSHARP_TEST_EXPORT __attribute__((visibility(\"default\")))\n" +
        "#else\n" +
        "#define GSHARP_TEST_EXPORT __declspec(dllexport)\n" +
        "#endif\n" +
        $"extern \"C\" GSHARP_TEST_EXPORT int fn_unit{unitIndex}(int x) {{ return {expression}; }}\n";

    private static string FindUnitObject(string objsDir, string unitStem, string cleanStem)
    {
        Directory.Exists(objsDir).Should().BeTrue($"Expected object directory '{objsDir}' to exist.");
        var match = Directory.EnumerateFiles(objsDir, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                var isObj = name.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".o", StringComparison.OrdinalIgnoreCase);
                if (!isObj)
                {
                    return false;
                }

                return name.StartsWith(unitStem + ".", StringComparison.OrdinalIgnoreCase) ||
                       name.StartsWith(unitStem + "_", StringComparison.OrdinalIgnoreCase) ||
                       (name.StartsWith(cleanStem + ".", StringComparison.OrdinalIgnoreCase) &&
                        name.Length > cleanStem.Length + 1 &&
                        !char.IsAsciiDigit(name[cleanStem.Length + 1])) ||
                       name.StartsWith(cleanStem + "_", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        match.Should().ContainSingle(
            $"Expected one object file for '{unitStem}' / '{cleanStem}' under '{objsDir}'.");
        return match[0];
    }

    private static string HostArchitecture() =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    private void WriteProducerProject(bool mockCompilation, bool multiTarget)
    {
        var mockLiteral = mockCompilation ? "true" : "false";
        var multiTargetLiteral = multiTarget ? "true" : "false";
        File.WriteAllText(
            Path.Combine(_producerDir, "Producer.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <GSharpMockCompilation>{mockLiteral}</GSharpMockCompilation>
                <GSharpMultiTarget>{multiTargetLiteral}</GSharpMultiTarget>
                <GSharpCompilerTaskAssembly>{_taskAssemblyPath}</GSharpCompilerTaskAssembly>
              </PropertyGroup>
              <Import Project="{_targetsPath}" />
            </Project>
            """);
    }

    private string WriteObjFile(string fileName, string content)
    {
        var path = Path.Combine(_objDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private void WriteManifest(params string[] fileNames) =>
        File.WriteAllLines(Path.Combine(_objDir, ManifestFileName), fileNames);
}
