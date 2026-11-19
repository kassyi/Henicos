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
public sealed class MSBuildSplitNativeBuildTests : IDisposable
{
    private const string ManifestFileName = "Kassyi.GSharp.Kernels.artifacts";

    private readonly ITestOutputHelper _output;
    private readonly string _projectDir;
    private readonly string _objDir;
    private readonly string _binDir;
    private readonly string _targetsPath;
    private readonly string _taskAssemblyPath;

    public MSBuildSplitNativeBuildTests(ITestOutputHelper output)
    {
        _output = output;
        _projectDir = Path.Combine(
            Path.GetTempPath(),
            "GSharpIntegrationTests",
            Guid.NewGuid().ToString("N"),
            "Producer");
        _objDir = Path.Combine(_projectDir, "obj", "Debug", "net10.0");
        _binDir = Path.Combine(_projectDir, "bin", "Debug", "net10.0");
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
        var root = Path.GetDirectoryName(_projectDir) ?? _projectDir;
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _output.WriteLine($"Warning: Failed to delete temporary test directory '{root}': {ex.Message}");
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Build_SplitMockUnits_ProducesSingleSharedLibraryPerModuleStem()
    {
        WriteProducerProject(mockCompilation: true);
        var arch = HostArchitecture();

        WriteObjFile("ModA.g.hpp", "#pragma once\n");
        WriteObjFile("ModA.g.hpp.gsym.json", "{}");
        WriteObjFile("ModA.g.cpp", "#include \"ModA.g.hpp\"\n");
        WriteObjFile("ModA.g.cpp.gsym.json", "{}");
        WriteObjFile("ModA.1.g.cpp", "#include \"ModA.g.hpp\"\n");
        WriteObjFile("ModA.1.g.cpp.gsym.json", "{}");
        WriteObjFile("ModA.2.g.cpp", "#include \"ModA.g.hpp\"\n");
        WriteObjFile("ModA.2.g.cpp.gsym.json", "{}");
        WriteObjFile("ModA.partition.json", "{\"dialect\":\"Cpu\",\"B\":3,\"units\":[],\"classes\":[]}");
        WriteObjFile("ModB.g.cpp", "// single unit\n");
        WriteObjFile("ModB.g.cpp.gsym.json", "{}");
        WriteManifest("ModA.g.cpp", "ModA.1.g.cpp", "ModA.2.g.cpp", "ModB.g.cpp");

        await BuildSuccessAsync("-m:2");

        var nativeDllsDir = Path.Combine(_objDir, "GSharpNativeDlls");
        var dllFiles = Directory.GetFiles(nativeDllsDir, "*.dll")
            .Select(Path.GetFileName)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        dllFiles.Should().BeEquivalentTo([$"ModA_{arch}.dll", $"ModB_{arch}.dll"]);

        File.Exists(Path.Combine(_binDir, $"ModA_{arch}.dll")).Should().BeTrue();
        File.Exists(Path.Combine(_binDir, $"ModB_{arch}.dll")).Should().BeTrue();
        Directory.GetFiles(_binDir, "ModA.1_*.dll").Should().BeEmpty();
        Directory.GetFiles(_binDir, "ModA.2_*.dll").Should().BeEmpty();
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Build_SplitToSingleUnitShrink_CleansStaleUnitsAndHeader()
    {
        WriteProducerProject(mockCompilation: true);
        var arch = HostArchitecture();

        var headerPath = WriteObjFile("ModA.g.hpp", "#pragma once\n");
        var headerGsymPath = WriteObjFile("ModA.g.hpp.gsym.json", "{}");
        var unit0Path = WriteObjFile("ModA.g.cpp", "#include \"ModA.g.hpp\"\n");
        var unit0GsymPath = WriteObjFile("ModA.g.cpp.gsym.json", "{}");
        var unit1Path = WriteObjFile("ModA.1.g.cpp", "#include \"ModA.g.hpp\"\n");
        var unit1GsymPath = WriteObjFile("ModA.1.g.cpp.gsym.json", "{}");
        WriteObjFile("ModA.partition.json", "{\"dialect\":\"Cpu\",\"B\":2,\"units\":[],\"classes\":[]}");
        WriteManifest("ModA.g.cpp", "ModA.1.g.cpp");

        await BuildSuccessAsync();
        File.Exists(Path.Combine(_objDir, "GSharpNativeDlls", $"ModA_{arch}.dll")).Should().BeTrue();

        // Build 2: shrink manifest to single unit (ModA.1.g.cpp + companion .gsym.json drop out of FileWrites).
        WriteObjFile("ModA.g.cpp", "// single unit\n");
        WriteObjFile("ModA.partition.json", "{\"dialect\":\"Cpu\",\"B\":1,\"units\":[],\"classes\":[]}");
        WriteManifest("ModA.g.cpp");

        await BuildSuccessAsync();

        File.Exists(unit0Path).Should().BeTrue("active unit 0 source must remain");
        File.Exists(Path.Combine(_objDir, "GSharpNativeDlls", $"ModA_{arch}.dll")).Should().BeTrue();
        File.Exists(unit1Path).Should().BeFalse("IncrementalClean must remove stale unit 1 source");
        File.Exists(unit1GsymPath).Should().BeFalse("IncrementalClean must remove stale unit 1 symbol map");

        // Build 3: switch to CUDA / empty .cpp manifest -> .targets drops _GSharpHeaderCandidates, cleaning .g.hpp.
        WriteObjFile("ModA.g.cu", "// cuda single module\n");
        WriteManifest("ModA.g.cu");

        await BuildSuccessAsync();

        File.Exists(headerPath).Should().BeFalse("IncrementalClean must remove stale .g.hpp");
        File.Exists(headerGsymPath).Should().BeFalse("IncrementalClean must remove stale .g.hpp.gsym.json");
        File.Exists(unit0Path).Should().BeFalse("IncrementalClean must remove stale unit 0 source");
        File.Exists(unit0GsymPath).Should().BeFalse("IncrementalClean must remove stale unit 0 symbol map");
        File.Exists(Path.Combine(_objDir, "GSharpFatbins", "ModA.gfatbin")).Should().BeTrue();
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Build_CleanTargetOnSplitModule_RemovesHeadersObjsRecordsAndDlls()
    {
        WriteProducerProject(mockCompilation: false);
        var arch = HostArchitecture();

        var headerPath = WriteObjFile(
            "ModSplit.g.hpp",
            "#pragma once\ninline int shared_mul(int x) { return x * 2; }\n");
        var headerGsymPath = WriteObjFile("ModSplit.g.hpp.gsym.json", "{}");
        var unit0Path = WriteObjFile("ModSplit.g.cpp", FormatUnitSource(0, "shared_mul(x) + 1"));
        WriteObjFile("ModSplit.g.cpp.gsym.json", "{}");
        var unit1Path = WriteObjFile("ModSplit.1.g.cpp", FormatUnitSource(1, "shared_mul(x) + 10"));
        WriteObjFile("ModSplit.1.g.cpp.gsym.json", "{}");
        var partitionPath = WriteObjFile(
            "ModSplit.partition.json",
            "{\"dialect\":\"Cpu\",\"B\":2,\"units\":[],\"classes\":[]}");
        WriteManifest("ModSplit.g.cpp", "ModSplit.1.g.cpp");

        var build1 = await BuildAsync("-m:2");
        var dllPath = Path.Combine(_objDir, "GSharpNativeDlls", $"ModSplit_{arch}.dll");
        if (!File.Exists(dllPath) &&
            build1.Stdout.Contains("Skipping native CPU kernel compilation", StringComparison.Ordinal))
        {
            Skip.If(true, "No native C++ toolchain is available on this host.");
        }

        build1.ExitCode.Should().Be(0, $"Build 1 failed.\nSTDOUT:\n{build1.Stdout}\nSTDERR:\n{build1.Stderr}");
        File.Exists(headerPath).Should().BeTrue();
        File.Exists(dllPath).Should().BeTrue();

        var nativeObjsDir = Path.Combine(_objDir, "GSharpNativeObjs");
        Directory.Exists(nativeObjsDir).Should().BeTrue("real C++ build must populate GSharpNativeObjs before Clean");
        var objFiles = Directory.GetFiles(nativeObjsDir, "*", SearchOption.AllDirectories)
            .Where(static path =>
                path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".o", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        objFiles.Length.Should().BeGreaterThanOrEqualTo(2);
        foreach (var objFile in objFiles)
        {
            new FileInfo(objFile).Length.Should().BeGreaterThan(0);
        }

        await BuildSuccessAsync("-t:Clean");

        File.Exists(headerPath).Should().BeFalse();
        File.Exists(headerGsymPath).Should().BeFalse();
        File.Exists(unit0Path).Should().BeFalse();
        File.Exists(unit1Path).Should().BeFalse();
        File.Exists(partitionPath).Should().BeFalse();

        var nativeDllsDir = Path.Combine(_objDir, "GSharpNativeDlls");
        (Directory.Exists(nativeDllsDir) ? Directory.GetFiles(nativeDllsDir, "*.dll") : []).Should().BeEmpty();
        (Directory.Exists(nativeObjsDir)
            ? Directory.GetFiles(nativeObjsDir, "*", SearchOption.AllDirectories)
            : []).Should().BeEmpty();
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Build_RealCppSplitUnitsIncrementalEdit_RecompilesOnlyModifiedUnit()
    {
        WriteProducerProject(mockCompilation: false);
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

        var build1 = await BuildAsync("-m:2");
        build1.ExitCode.Should().Be(0, $"Build 1 failed.\nSTDOUT:\n{build1.Stdout}\nSTDERR:\n{build1.Stderr}");

        var dllPath = Path.Combine(_binDir, $"ModSplit_{arch}.dll");
        if (!File.Exists(dllPath) &&
            build1.Stdout.Contains("Skipping native CPU kernel compilation", StringComparison.Ordinal))
        {
            Skip.If(true, "No native C++ toolchain is available on this host.");
        }

        File.Exists(dllPath).Should().BeTrue($"Expected linked DLL at '{dllPath}'.");
        AssertExportedValues(dllPath, expectedUnit0: 11, expectedUnit1: 20);

        var objsDir = Path.Combine(_objDir, "GSharpNativeObjs");
        var unit0Obj = FindUnitObject(objsDir, "ModSplit.g", "ModSplit");
        var unit1Obj = FindUnitObject(objsDir, "ModSplit.1.g", "ModSplit.1");

        // Backdate object timestamps so any subsequent recompile is unambiguously observable.
        var agedTime = DateTime.UtcNow.AddSeconds(-10);
        File.SetLastWriteTimeUtc(unit0Obj, agedTime);
        File.SetLastWriteTimeUtc(unit1Obj, agedTime);

        // Build 2: no source edits -> 0 compiles.
        await BuildSuccessAsync("-m:2");
        File.GetLastWriteTimeUtc(unit0Obj).Should().Be(agedTime);
        File.GetLastWriteTimeUtc(unit1Obj).Should().Be(agedTime);

        // Build 3: edit only unit 1 -> only unit 1 recompiles.
        File.WriteAllText(unit1Path, FormatUnitSource(1, "shared_mul(x) + 20"));
        await BuildSuccessAsync("-m:2");
        File.GetLastWriteTimeUtc(unit0Obj).Should().Be(agedTime);
        File.GetLastWriteTimeUtc(unit1Obj).Should().BeAfter(agedTime);
        AssertExportedValues(dllPath, expectedUnit0: 11, expectedUnit1: 30);

        // Age unit 1 object again, then edit shared header -> both units recompile.
        File.SetLastWriteTimeUtc(unit0Obj, agedTime);
        File.SetLastWriteTimeUtc(unit1Obj, agedTime);
        File.WriteAllText(headerPath, "#pragma once\ninline int shared_mul(int x) { return x * 3; }\n");
        await BuildSuccessAsync("-m:2");
        File.GetLastWriteTimeUtc(unit0Obj).Should().BeAfter(agedTime);
        File.GetLastWriteTimeUtc(unit1Obj).Should().BeAfter(agedTime);
        AssertExportedValues(dllPath, expectedUnit0: 16, expectedUnit1: 35);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public async Task Build_StrayUnitObjectInObjsDir_ExcludedFromManifestOnlyLink()
    {
        WriteProducerProject(mockCompilation: false);
        var arch = HostArchitecture();

        WriteObjFile("ModSplit.g.hpp", "#pragma once\ninline int shared_mul(int x) { return x * 2; }\n");
        WriteObjFile("ModSplit.g.hpp.gsym.json", "{}");
        WriteObjFile("ModSplit.g.cpp", FormatUnitSource(0, "shared_mul(x) + 1"));
        WriteObjFile("ModSplit.g.cpp.gsym.json", "{}");
        WriteObjFile("ModSplit.1.g.cpp", FormatUnitSource(1, "shared_mul(x) + 10"));
        WriteObjFile("ModSplit.1.g.cpp.gsym.json", "{}");
        WriteObjFile("ModSplit.partition.json", "{\"dialect\":\"Cpu\",\"B\":2,\"units\":[],\"classes\":[]}");
        WriteManifest("ModSplit.g.cpp", "ModSplit.1.g.cpp");

        var build1 = await BuildAsync("-m:2");
        var dllPath = Path.Combine(_binDir, $"ModSplit_{arch}.dll");
        if (!File.Exists(dllPath) &&
            build1.Stdout.Contains("Skipping native CPU kernel compilation", StringComparison.Ordinal))
        {
            Skip.If(true, "No native C++ toolchain is available on this host.");
        }

        build1.ExitCode.Should().Be(0, $"Build 1 failed.\nSTDOUT:\n{build1.Stdout}\nSTDERR:\n{build1.Stderr}");

        var objsDir = Path.Combine(_objDir, "GSharpNativeObjs");
        var unit0Obj = FindUnitObject(objsDir, "ModSplit.g", "ModSplit");
        var unitObjsSubDir = Path.GetDirectoryName(unit0Obj) ?? objsDir;

        // Seed corrupt stray objects that would fail linking if the linker globbed *.obj / *.o blindly.
        File.WriteAllText(Path.Combine(unitObjsSubDir, "ModSplit.99.obj"), "CORRUPT_STRAY_OBJECT_BYTES");
        File.WriteAllText(Path.Combine(unitObjsSubDir, "ModSplit.99.o"), "CORRUPT_STRAY_OBJECT_BYTES");

        var objDllPath = Path.Combine(_objDir, "GSharpNativeDlls", $"ModSplit_{arch}.dll");
        if (File.Exists(objDllPath))
        {
            File.Delete(objDllPath);
        }

        if (File.Exists(dllPath))
        {
            File.Delete(dllPath);
        }

        await BuildSuccessAsync("-m:2");

        File.Exists(dllPath).Should().BeTrue();
        AssertExportedValues(dllPath, expectedUnit0: 11, expectedUnit1: 20);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public async Task Build_InvalidSigningKey_FailsWithGs0003AndOmitsBinOutput()
    {
        WriteProducerProject(mockCompilation: false);
        var arch = HostArchitecture();

        WriteObjFile("ModSplit.g.hpp", "#pragma once\ninline int shared_mul(int x) { return x * 2; }\n");
        WriteObjFile("ModSplit.g.hpp.gsym.json", "{}");
        WriteObjFile("ModSplit.g.cpp", FormatUnitSource(0, "shared_mul(x) + 1"));
        WriteObjFile("ModSplit.g.cpp.gsym.json", "{}");
        WriteObjFile("ModSplit.1.g.cpp", FormatUnitSource(1, "shared_mul(x) + 10"));
        WriteObjFile("ModSplit.1.g.cpp.gsym.json", "{}");
        WriteObjFile("ModSplit.partition.json", "{\"dialect\":\"Cpu\",\"B\":2,\"units\":[],\"classes\":[]}");
        WriteManifest("ModSplit.g.cpp", "ModSplit.1.g.cpp");

        var env = new Dictionary<string, string?> { ["GSHARP_SIGNING_KEY"] = "badkey" };
        var result = await BuildProcessRunner.RunBuildAsync(
            _projectDir,
            "Producer.csproj",
            environment: env,
            extraArguments: ["-m:2"]);

        if (result.ExitCode == 0 &&
            result.Stdout.Contains("Skipping native CPU kernel compilation", StringComparison.Ordinal))
        {
            Skip.If(true, "No native C++ toolchain is available on this host.");
        }

        result.ExitCode.Should().NotBe(0);
        (result.Stdout + "\n" + result.Stderr).Should().Contain("GS0003");
        File.Exists(Path.Combine(_binDir, $"ModSplit_{arch}.dll")).Should().BeFalse();
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Build_AssemblyNameChangeAndZeroKernelManifest_CleansStalePartitionJson()
    {
        WriteProducerProject(mockCompilation: true);

        // Build 1: AssemblyName = ProbeA with B=2 split artifacts.
        WriteObjFile("Kassyi.GSharp.Kernels_ProbeA.g.hpp", "#pragma once\n");
        WriteObjFile("Kassyi.GSharp.Kernels_ProbeA.g.hpp.gsym.json", "{}");
        WriteObjFile("Kassyi.GSharp.Kernels_ProbeA.g.cpp", "#include \"Kassyi.GSharp.Kernels_ProbeA.g.hpp\"\n");
        WriteObjFile("Kassyi.GSharp.Kernels_ProbeA.g.cpp.gsym.json", "{}");
        WriteObjFile("Kassyi.GSharp.Kernels_ProbeA.1.g.cpp", "#include \"Kassyi.GSharp.Kernels_ProbeA.g.hpp\"\n");
        WriteObjFile("Kassyi.GSharp.Kernels_ProbeA.1.g.cpp.gsym.json", "{}");
        var probeAPartition = WriteObjFile(
            "Kassyi.GSharp.Kernels_ProbeA.partition.json",
            "{\"dialect\":\"Cpu\",\"B\":2,\"units\":[],\"classes\":[]}");
        WriteManifest("Kassyi.GSharp.Kernels_ProbeA.g.cpp", "Kassyi.GSharp.Kernels_ProbeA.1.g.cpp");

        await BuildSuccessAsync("-p:AssemblyName=ProbeA");
        File.Exists(probeAPartition).Should().BeTrue();

        // Build 2: AssemblyName changes ProbeA -> ProbeB; IncrementalClean must remove ProbeA.partition.json.
        WriteObjFile("Kassyi.GSharp.Kernels_ProbeB.g.hpp", "#pragma once\n");
        WriteObjFile("Kassyi.GSharp.Kernels_ProbeB.g.hpp.gsym.json", "{}");
        WriteObjFile("Kassyi.GSharp.Kernels_ProbeB.g.cpp", "#include \"Kassyi.GSharp.Kernels_ProbeB.g.hpp\"\n");
        WriteObjFile("Kassyi.GSharp.Kernels_ProbeB.g.cpp.gsym.json", "{}");
        WriteObjFile("Kassyi.GSharp.Kernels_ProbeB.1.g.cpp", "#include \"Kassyi.GSharp.Kernels_ProbeB.g.hpp\"\n");
        WriteObjFile("Kassyi.GSharp.Kernels_ProbeB.1.g.cpp.gsym.json", "{}");
        var probeBPartition = WriteObjFile(
            "Kassyi.GSharp.Kernels_ProbeB.partition.json",
            "{\"dialect\":\"Cpu\",\"B\":2,\"units\":[],\"classes\":[]}");
        WriteManifest("Kassyi.GSharp.Kernels_ProbeB.g.cpp", "Kassyi.GSharp.Kernels_ProbeB.1.g.cpp");

        await BuildSuccessAsync("-p:AssemblyName=ProbeB");
        File.Exists(probeAPartition).Should().BeFalse("IncrementalClean must remove prior assembly's partition plan");
        File.Exists(probeBPartition).Should().BeTrue("active assembly's partition plan must remain");

        // Build 3: Kernels drop to 0 (empty manifest); IncrementalClean must remove ProbeB.partition.json.
        WriteManifest();

        await BuildSuccessAsync("-p:AssemblyName=ProbeB");
        Directory.GetFiles(_objDir, "*.partition.json").Should().BeEmpty();
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public async Task Build_RealCppZeroByteObjectWithCurrentRecord_RecompilesAndLinksWorkingDll()
    {
        WriteProducerProject(mockCompilation: false);
        var arch = HostArchitecture();

        WriteObjFile("ModSplit.g.hpp", "#pragma once\ninline int shared_mul(int x) { return x * 2; }\n");
        WriteObjFile("ModSplit.g.hpp.gsym.json", "{}");
        WriteObjFile("ModSplit.g.cpp", FormatUnitSource(0, "shared_mul(x) + 1"));
        WriteObjFile("ModSplit.g.cpp.gsym.json", "{}");
        WriteObjFile("ModSplit.1.g.cpp", FormatUnitSource(1, "shared_mul(x) + 10"));
        WriteObjFile("ModSplit.1.g.cpp.gsym.json", "{}");
        WriteObjFile("ModSplit.partition.json", "{\"dialect\":\"Cpu\",\"B\":2,\"units\":[],\"classes\":[]}");
        WriteManifest("ModSplit.g.cpp", "ModSplit.1.g.cpp");

        var build1 = await BuildAsync("-m:2");
        var dllPath = Path.Combine(_binDir, $"ModSplit_{arch}.dll");
        if (!File.Exists(dllPath) &&
            build1.Stdout.Contains("Skipping native CPU kernel compilation", StringComparison.Ordinal))
        {
            Skip.If(true, "No native C++ toolchain is available on this host.");
        }

        build1.ExitCode.Should().Be(0, $"Build 1 failed.\nSTDOUT:\n{build1.Stdout}\nSTDERR:\n{build1.Stderr}");

        var objsDir = Path.Combine(_objDir, "GSharpNativeObjs");
        var unit1Obj = FindUnitObject(objsDir, "ModSplit.1.g", "ModSplit.1");
        File.Exists(unit1Obj + ".record").Should().BeTrue();

        // Truncate unit 1 object to 0 bytes while leaving its .record intact.
        File.WriteAllBytes(unit1Obj, []);
        new FileInfo(unit1Obj).Length.Should().Be(0);

        await BuildSuccessAsync("-m:2");

        new FileInfo(unit1Obj).Length.Should().BeGreaterThan(0);
        AssertExportedValues(dllPath, expectedUnit0: 11, expectedUnit1: 20);
    }

    private static string FormatUnitSource(int unitIndex, string expression) =>
        "#include \"ModSplit.g.hpp\"\n" +
        "#if !defined(_MSC_VER)\n" +
        "#define GSHARP_TEST_EXPORT __attribute__((visibility(\"default\")))\n" +
        "#else\n" +
        "#define GSHARP_TEST_EXPORT __declspec(dllexport)\n" +
        "#endif\n" +
        $"extern \"C\" GSHARP_TEST_EXPORT int fn_unit{unitIndex}(int x) {{ return {expression}; }}\n";

    private static unsafe void AssertExportedValues(string dllPath, int expectedUnit0, int expectedUnit1)
    {
        var handle = NativeLibrary.Load(dllPath);
        try
        {
            var fn0 = (delegate* unmanaged[Cdecl]<int, int>)NativeLibrary.GetExport(handle, "fn_unit0");
            var fn1 = (delegate* unmanaged[Cdecl]<int, int>)NativeLibrary.GetExport(handle, "fn_unit1");
            fn0(5).Should().Be(expectedUnit0);
            fn1(5).Should().Be(expectedUnit1);
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }

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

    private void WriteProducerProject(bool mockCompilation)
    {
        var mockLiteral = mockCompilation ? "true" : "false";
        File.WriteAllText(
            Path.Combine(_projectDir, "Producer.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <GSharpMockCompilation>{mockLiteral}</GSharpMockCompilation>
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

    private Task<BuildResult> BuildAsync(params string[] extraArguments) =>
        BuildProcessRunner.RunBuildAsync(_projectDir, "Producer.csproj", extraArguments: extraArguments);

    private async Task BuildSuccessAsync(params string[] extraArguments)
    {
        var (exitCode, stdout, stderr) = await BuildAsync(extraArguments);
        exitCode.Should().Be(0, $"the build should succeed. STDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }
}
