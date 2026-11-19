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
    public async Task Build_DialectSwitch_CompilesOnlyListedSourceAndCleansOldArtifacts()
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
    public async Task Build_RepeatedExecution_KeepsListedSourcesAndOutputs()
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
    public async Task Build_UnlistedObjSource_DoesNotCompileUnlistedSource()
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
    public async Task Build_EmptyManifest_CompilesNothing()
    {
        WriteObjFile("X.g.cu");
        WriteObjFile("X.g.cpp");
        WriteManifest();

        await BuildAsync();

        Directory.Exists(Path.Combine(_objDir, "GSharpFatbins")).Should().BeFalse();
        Directory.Exists(Path.Combine(_objDir, "GSharpNativeDlls")).Should().BeFalse();
    }

    // An IDE (Visual Studio, Rider, the C# Dev Kit, MSBuildWorkspace) runs design-time builds on every edit; they
    // must not run nvcc or the C++ compiler on the sources the last real build left in obj/
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Build_DesignTimeBuild_CompilesNoNativeSource()
    {
        WriteObjFile("X.g.cu");
        WriteObjFile("Y.g.cpp");
        WriteManifest("X.g.cu", "Y.g.cpp");

        await BuildAsync(
            "-t:Compile",
            "-p:DesignTimeBuild=true",
            "-p:SkipCompilerExecution=true",
            "-p:ProvideCommandLineArgs=true"
        );

        Directory.Exists(Path.Combine(_objDir, "GSharpFatbins")).Should().BeFalse("no fatbin is built at design time");
        Directory.Exists(Path.Combine(_objDir, "GSharpNativeDlls")).Should().BeFalse("no DLL is built at design time");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Build_SplitCpuRepeatedExecution_KeepsHeaderAndSymbolFile()
    {
        WriteObjFile("X.g.cpp");
        WriteObjFile("X.1.g.cpp");
        var headerPath = WriteObjFile("X.g.hpp");
        var headerSymbolPath = WriteObjFile("X.g.hpp.gsym.json");
        WriteManifest("X.g.cpp", "X.1.g.cpp");

        await BuildAsync();
        await BuildAsync();

        File.Exists(headerPath).Should().BeTrue();
        File.Exists(headerSymbolPath).Should().BeTrue();
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Build_SplitCpuDialectSwitch_CleansHeaderAndHeaderSymbolFile()
    {
        var unit0Path = WriteObjFile("X.g.cpp");
        var unit1Path = WriteObjFile("X.1.g.cpp");
        var headerPath = WriteObjFile("X.g.hpp");
        var headerSymbolPath = WriteObjFile("X.g.hpp.gsym.json");
        WriteManifest("X.g.cpp", "X.1.g.cpp");

        await BuildAsync();

        WriteObjFile("X.g.cu");
        WriteManifest("X.g.cu");

        await BuildAsync();

        File.Exists(headerPath).Should().BeFalse("IncrementalClean removes stale header on dialect switch");
        File.Exists(headerSymbolPath).Should().BeFalse("IncrementalClean removes stale header symbol file");
        File.Exists(unit0Path).Should().BeFalse();
        File.Exists(unit1Path).Should().BeFalse();
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Build_SplitCpuToEmptyManifest_CleansHeaderAndHeaderSymbolFile()
    {
        WriteObjFile("X.g.cpp");
        WriteObjFile("X.1.g.cpp");
        var headerPath = WriteObjFile("X.g.hpp");
        var headerSymbolPath = WriteObjFile("X.g.hpp.gsym.json");
        WriteManifest("X.g.cpp", "X.1.g.cpp");

        await BuildAsync();

        WriteManifest();

        await BuildAsync();

        File.Exists(headerPath).Should().BeFalse("IncrementalClean removes stale header when manifest is empty");
        File.Exists(headerSymbolPath).Should().BeFalse("IncrementalClean removes stale header symbol file");
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Build_SplitCpuMockCompilation_LinksSingleModuleDll()
    {
        WriteObjFile("X.g.cpp");
        WriteObjFile("X.1.g.cpp");
        WriteObjFile("X.g.hpp");
        WriteManifest("X.g.cpp", "X.1.g.cpp");

        await BuildAsync();

        var nativeDlls = Path.Combine(_objDir, "GSharpNativeDlls");
        Directory.GetFiles(nativeDlls, "X_*.dll").Should().ContainSingle();
        Directory.GetFiles(nativeDlls, "X.1_*.dll").Should().BeEmpty();
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Build_SplitCpuRealCompilation_IncrementalSkipShrinkAndCleanRemoveIntermediates()
    {
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
            System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
        var objExt = OperatingSystem.IsWindows() ? ".obj" : ".o";

        File.WriteAllText(Path.Combine(_objDir, "X.g.hpp"), "inline int shared_helper(int v) { return v + 1; }\n");
        File.WriteAllText(
            Path.Combine(_objDir, "X.g.cpp"),
            "#include \"X.g.hpp\"\nextern \"C\" int unit0(int v) { return shared_helper(v); }\n");
        var unit1Path = Path.Combine(_objDir, "X.1.g.cpp");
        File.WriteAllText(
            unit1Path,
            "#include \"X.g.hpp\"\nextern \"C\" int unit1(int v) { return shared_helper(v) + 1; }\n");
        WriteManifest("X.g.cpp", "X.1.g.cpp");

        await BuildAsync("-p:GSharpMockCompilation=false");

        var archObjDir = Path.Combine(_objDir, "GSharpNativeObjs", arch);
        var obj0 = Path.Combine(archObjDir, $"X{objExt}");
        var obj1 = Path.Combine(archObjDir, $"X.1{objExt}");
        var dllPath = Path.Combine(_objDir, "GSharpNativeDlls", $"X_{arch}.dll");
        if (!File.Exists(dllPath))
        {
            // Toolchain unavailable on host (e.g., Linux without g++).
            return;
        }

        File.Exists(obj0).Should().BeTrue();
        File.Exists(obj1).Should().BeTrue();
        File.Exists(obj0 + ".record").Should().BeTrue();
        File.Exists(obj1 + ".record").Should().BeTrue();

        var secondBuild = await BuildProcessRunner.RunBuildAsync(
            _projectDir,
            "Producer.csproj",
            extraArguments: ["-p:GSharpMockCompilation=false", "-v:normal"]);
        secondBuild.ExitCode.Should().Be(0);
        secondBuild.Stdout.Should().Contain(
            "Skipping C++ object compilation: all 2 translation unit(s) are up-to-date.");

        // Shrink B=2 -> B=1: IncrementalClean should remove unit 1's object, record, and stale header
        File.WriteAllText(Path.Combine(_objDir, "X.g.cpp"), "extern \"C\" int unit0(int v) { return v + 1; }\n");
        File.Delete(Path.Combine(_objDir, "X.g.hpp"));
        File.Delete(unit1Path);
        WriteManifest("X.g.cpp");

        await BuildAsync("-p:GSharpMockCompilation=false");

        File.Exists(obj0).Should().BeTrue();
        File.Exists(obj1).Should().BeFalse("IncrementalClean removes orphaned unit 1 object");
        File.Exists(obj1 + ".record").Should().BeFalse("IncrementalClean removes orphaned unit 1 record");

        await BuildAsync("-t:Clean", "-p:GSharpMockCompilation=false");

        File.Exists(obj0).Should().BeFalse("Clean deletes compiled object file");
        File.Exists(obj0 + ".record").Should().BeFalse("Clean deletes object command record");
        File.Exists(dllPath).Should().BeFalse("Clean deletes linked native DLL");
    }

    private string WriteObjFile(string fileName)
    {
        var path = Path.Combine(_objDir, fileName);
        File.WriteAllText(path, "// generated");
        return path;
    }

    private void WriteManifest(params string[] fileNames) =>
        File.WriteAllLines(Path.Combine(_objDir, ManifestFileName), fileNames);

    private async Task BuildAsync(params string[] extraArguments)
    {
        var (exitCode, stdout, stderr) = await BuildProcessRunner.RunBuildAsync(
            _projectDir,
            "Producer.csproj",
            extraArguments: extraArguments
        );
        exitCode.Should().Be(0, $"the build should succeed. STDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }
}
