using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Aot;
using Kassyi.GSharp.Compiler.Models;
using Kassyi.GSharp.Naming;
using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Tests.Shared.Generator;

[Category(TestCategory.Compiler)]
public class AotArtifactWriterHostTests
{
    private const string NeutralBaseDir = @"C:\Program Files\dotnet\sdk\10.0.402\Roslyn\bincore\";

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Roslyn.Worker", NeutralBaseDir)]
    [InlineData("JetBrains.Roslyn.Worker", NeutralBaseDir)]
    [InlineData("ServiceHub.RoslynCodeAnalysisService", NeutralBaseDir)]
    [InlineData("LanguageServer", NeutralBaseDir)]
    [InlineData("OmniSharp", NeutralBaseDir)]
    [InlineData("csc", @"C:\Users\x\AppData\Local\JetBrains\Rider\bin\")]
    [InlineData("Stryker.CLI", NeutralBaseDir)]
    [InlineData("csc", @"C:\Users\x\.nuget\packages\dotnet-stryker\5.0.0\tools\net10.0\any\")]
    public void IsToolHost_ToolProcess_ReturnsTrue(string domainName, string baseDir)
    {
        Assert.True(AotArtifactWriter.IsToolHost(domainName, baseDir));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("csc", NeutralBaseDir)]
    [InlineData("VBCSCompiler", NeutralBaseDir)]
    [InlineData("dotnet", @"C:\Program Files\dotnet\sdk\10.0.402\")]
    public void IsToolHost_BuildProcess_ReturnsFalse(string domainName, string baseDir)
    {
        Assert.False(AotArtifactWriter.IsToolHost(domainName, baseDir));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Write_IdenticalContent_PreservesTimestamps()
    {
        using var directory = new TemporaryDirectory();
        var artifacts = WriteAndAge(directory.Path, "// kernel body v1\n");

        Write(directory.Path, "// kernel body v1\n");

        Assert.Equal(OldTime, File.GetLastWriteTimeUtc(artifacts.Source));
        Assert.Equal(OldTime, File.GetLastWriteTimeUtc(artifacts.SymbolFile));
        Assert.Equal(OldTime, File.GetLastWriteTimeUtc(artifacts.Manifest));
    }

    // The first case has the length of the original body, so the content comparison, not the length, decides
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("// kernel body v2\n")]
    [InlineData("// kernel body v2 with different length\n")]
    public void Write_DifferentContent_UpdatesModifiedArtifacts(string updatedBody)
    {
        using var directory = new TemporaryDirectory();
        var artifacts = WriteAndAge(directory.Path, "// kernel body v1\n");

        Write(directory.Path, updatedBody);

        Assert.Contains(updatedBody, File.ReadAllText(artifacts.Source), StringComparison.Ordinal);
        Assert.True(File.GetLastWriteTimeUtc(artifacts.Source) > OldTime);
        Assert.True(File.GetLastWriteTimeUtc(artifacts.SymbolFile) > OldTime);
        Assert.Equal(OldTime, File.GetLastWriteTimeUtc(artifacts.Manifest));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Write_PartitionJson_PreservesTimestampWhenIdentical()
    {
        using var directory = new TemporaryDirectory();
        const string PartitionV1 = "{\"B\": 1}\n";
        AotArtifactWriter.Write(
            new AotTarget("TestModule", false),
            new GeneratorConfig("Cpu", false, directory.Path),
            "// kernel body v1\n",
            default,
            partitionJson: PartitionV1
        );
        var partitionPath = Path.Combine(directory.Path, "Kassyi.GSharp.Kernels_TestModule.partition.json");
        Assert.True(File.Exists(partitionPath));
        File.SetLastWriteTimeUtc(partitionPath, OldTime);

        // Second write with identical partition JSON
        AotArtifactWriter.Write(
            new AotTarget("TestModule", false),
            new GeneratorConfig("Cpu", false, directory.Path),
            "// kernel body v1\n",
            default,
            partitionJson: PartitionV1
        );
        Assert.Equal(OldTime, File.GetLastWriteTimeUtc(partitionPath));

        // Third write with changed partition JSON
        const string PartitionV2 = "{\"B\": 2}\n";
        AotArtifactWriter.Write(
            new AotTarget("TestModule", false),
            new GeneratorConfig("Cpu", false, directory.Path),
            "// kernel body v1\n",
            default,
            partitionJson: PartitionV2
        );
        Assert.True(File.GetLastWriteTimeUtc(partitionPath) > OldTime);
        Assert.Equal(PartitionV2, File.ReadAllText(partitionPath));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Write_SplitCpuModule_WritesHeaderUnitsGsymFilesAndBLineManifest()
    {
        using var directory = new TemporaryDirectory();
        var files = CreateSplitFiles(
            "TestModule",
            "// header __gsharp_fn_Test__Helper__\n",
            ["// unit 0 __gsharp_k_Test__K0__\n", "// unit 1 __gsharp_k_Test__K1__\n"]);
        var errorSites = new[]
        {
            new GSharpNameEncoding.SymbolErrorSiteRecord(
                42u, "Kernels.cu.cs", 10, 4, "Test.K0", "Divided by zero.")
        };

        AotArtifactWriter.Write(
            new AotTarget("TestModule", false),
            new GeneratorConfig("Cpu", false, directory.Path),
            files,
            default,
            errorSites: errorSites);

        var headerPath = Path.Combine(directory.Path, "Kassyi.GSharp.Kernels_TestModule.g.hpp");
        Assert.True(File.Exists(headerPath));
        Assert.Contains("\"error_site\"", File.ReadAllText(headerPath + ".gsym.json"), StringComparison.Ordinal);

        string[] unitNames = ["Kassyi.GSharp.Kernels_TestModule.g.cpp", "Kassyi.GSharp.Kernels_TestModule.1.g.cpp"];
        foreach (var unitName in unitNames)
        {
            var unitPath = Path.Combine(directory.Path, unitName);
            Assert.True(File.Exists(unitPath));
            var lines = File.ReadAllLines(unitPath);
            Assert.True(GSharpNameEncoding.TryReadSymbolHeaderHash(lines[0], out var headerHash));
            var bodyHash = GSharpNameEncoding.ComputeSourceHash(File.ReadAllText(unitPath));
            Assert.Equal(bodyHash, headerHash);

            var unitSymbolPath = unitPath + ".gsym.json";
            Assert.True(File.Exists(unitSymbolPath));
            Assert.DoesNotContain("\"error_site\"", File.ReadAllText(unitSymbolPath), StringComparison.Ordinal);
        }

        var manifestLines = File.ReadAllLines(Path.Combine(directory.Path, AotArtifactWriter.ManifestFileName));
        Assert.Equal(unitNames, manifestLines);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Write_SplitCpuModuleIdenticalContent_PreservesAllTimestamps()
    {
        using var directory = new TemporaryDirectory();
        var files = CreateSplitFiles(
            "TestModule",
            "// header v1\n",
            ["// unit 0 v1\n", "// unit 1 v1\n"]);
        var paths = WriteSplitAndAge(directory.Path, files);

        AotArtifactWriter.Write(
            new AotTarget("TestModule", false),
            new GeneratorConfig("Cpu", false, directory.Path),
            files,
            default);

        foreach (var path in paths)
        {
            Assert.Equal(OldTime, File.GetLastWriteTimeUtc(path));
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Write_SplitCpuModuleSingleUnitChanged_UpdatesOnlyChangedUnit()
    {
        using var directory = new TemporaryDirectory();
        var initial = CreateSplitFiles(
            "TestModule",
            "// header v1\n",
            ["// unit 0 v1\n", "// unit 1 v1\n"]);
        var paths = WriteSplitAndAge(directory.Path, initial);

        var updated = CreateSplitFiles(
            "TestModule",
            "// header v1\n",
            ["// unit 0 v1\n", "// unit 1 v2\n"]);
        AotArtifactWriter.Write(
            new AotTarget("TestModule", false),
            new GeneratorConfig("Cpu", false, directory.Path),
            updated,
            default);

        Assert.Equal(OldTime, File.GetLastWriteTimeUtc(paths[0])); // header
        Assert.Equal(OldTime, File.GetLastWriteTimeUtc(paths[1])); // header .gsym.json
        Assert.Equal(OldTime, File.GetLastWriteTimeUtc(paths[2])); // unit 0
        Assert.Equal(OldTime, File.GetLastWriteTimeUtc(paths[3])); // unit 0 .gsym.json
        Assert.True(File.GetLastWriteTimeUtc(paths[4]) > OldTime); // unit 1
        Assert.True(File.GetLastWriteTimeUtc(paths[5]) > OldTime); // unit 1 .gsym.json
        Assert.Equal(OldTime, File.GetLastWriteTimeUtc(paths[6])); // manifest
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Write_ShrinkFromSplitToSingleUnit_DeletesStaleHeaderAndItsSymbolFile()
    {
        using var directory = new TemporaryDirectory();
        var splitFiles = CreateSplitFiles(
            "TestModule",
            "// header v1\n",
            ["// unit 0 v1\n", "// unit 1 v1\n"]);
        AotArtifactWriter.Write(
            new AotTarget("TestModule", false),
            new GeneratorConfig("Cpu", false, directory.Path),
            splitFiles,
            default);

        var headerPath = Path.Combine(directory.Path, "Kassyi.GSharp.Kernels_TestModule.g.hpp");
        var unit1Path = Path.Combine(directory.Path, "Kassyi.GSharp.Kernels_TestModule.1.g.cpp");
        Assert.True(File.Exists(headerPath));
        Assert.True(File.Exists(headerPath + ".gsym.json"));
        Assert.True(File.Exists(unit1Path));

        Write(directory.Path, "// single unit v2\n");

        Assert.False(File.Exists(headerPath));
        Assert.False(File.Exists(headerPath + ".gsym.json"));
        Assert.False(File.Exists(unit1Path));
        Assert.False(File.Exists(unit1Path + ".gsym.json"));
        Assert.Equal(
            ["Kassyi.GSharp.Kernels_TestModule.g.cpp"],
            File.ReadAllLines(Path.Combine(directory.Path, AotArtifactWriter.ManifestFileName)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Write_UnitWriteFails_LeavesPreviousManifestIntact()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Path);
        var manifestPath = Path.Combine(directory.Path, AotArtifactWriter.ManifestFileName);
        const string OldManifest = "Kassyi.GSharp.Kernels_Old.g.cpp\n";
        File.WriteAllText(manifestPath, OldManifest);
        var unit1Path = Path.Combine(directory.Path, "Kassyi.GSharp.Kernels_TestModule.1.g.cpp");
        Directory.CreateDirectory(unit1Path);

        var files = CreateSplitFiles(
            "TestModule",
            "// header v1\n",
            ["// unit 0 v1\n", "// unit 1 v1\n"]);
        var diagnostics = new List<Diagnostic>();

        AotArtifactWriter.WriteCore(
            new AotTarget("TestModule", false),
            new GeneratorConfig("Cpu", false, directory.Path),
            files,
            diagnostics.Add);

        var failure = Assert.Single(diagnostics, static d => d.Id == GSharpDiagnostics.AotArtifactWriteFailed.Id);
        Assert.Contains(unit1Path, failure.GetMessage(), StringComparison.Ordinal);
        Assert.Equal(OldManifest, File.ReadAllText(manifestPath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Scenario(TestScenario.SadPath)]
    public void WriteCore_EmptyOrDefaultUnits_ThrowsArgumentException(bool useDefault)
    {
        using var directory = new TemporaryDirectory();
        var units = useDefault
            ? default
            : System.Collections.Immutable.ImmutableArray<NativeModuleFile>.Empty;
        var files = new NativeModuleFiles(null, units);
        var diagnostics = new List<Diagnostic>();

        Assert.Throws<ArgumentException>(() =>
            AotArtifactWriter.WriteCore(
                new AotTarget("TestModule", false),
                new GeneratorConfig("Cpu", false, directory.Path),
                files,
                diagnostics.Add));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Write_ShrinkMultiUnitSplit_DeletesOrphanUnitsAndSymbolFiles()
    {
        using var directory = new TemporaryDirectory();
        var bodies8 = Enumerable.Range(0, 8).Select(i => $"// unit {i} v1\n").ToArray();
        var files8 = CreateSplitFiles("TestModule", "// header v1\n", bodies8);
        AotArtifactWriter.Write(
            new AotTarget("TestModule", false),
            new GeneratorConfig("Cpu", false, directory.Path),
            files8,
            default);

        for (var i = 1; i < 8; i++)
        {
            var unitPath = Path.Combine(directory.Path, $"Kassyi.GSharp.Kernels_TestModule.{i}.g.cpp");
            Assert.True(File.Exists(unitPath));
            Assert.True(File.Exists(unitPath + ".gsym.json"));
        }

        var bodies3 = Enumerable.Range(0, 3).Select(i => $"// unit {i} v2\n").ToArray();
        var files3 = CreateSplitFiles("TestModule", "// header v1\n", bodies3);
        AotArtifactWriter.Write(
            new AotTarget("TestModule", false),
            new GeneratorConfig("Cpu", false, directory.Path),
            files3,
            default);

        Assert.True(File.Exists(Path.Combine(directory.Path, "Kassyi.GSharp.Kernels_TestModule.g.hpp")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "Kassyi.GSharp.Kernels_TestModule.g.cpp")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "Kassyi.GSharp.Kernels_TestModule.1.g.cpp")));
        Assert.True(File.Exists(Path.Combine(directory.Path, "Kassyi.GSharp.Kernels_TestModule.2.g.cpp")));
        for (var i = 3; i < 8; i++)
        {
            var orphanUnit = Path.Combine(directory.Path, $"Kassyi.GSharp.Kernels_TestModule.{i}.g.cpp");
            Assert.False(File.Exists(orphanUnit));
            Assert.False(File.Exists(orphanUnit + ".gsym.json"));
        }

        Assert.Equal(
            [
                "Kassyi.GSharp.Kernels_TestModule.g.cpp",
                "Kassyi.GSharp.Kernels_TestModule.1.g.cpp",
                "Kassyi.GSharp.Kernels_TestModule.2.g.cpp"
            ],
            File.ReadAllLines(Path.Combine(directory.Path, AotArtifactWriter.ManifestFileName)));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void WriteEmptyManifest_ExistingPartitionFile_RemovesPartitionFile()
    {
        using var directory = new TemporaryDirectory();
        var files = CreateSplitFiles("TestModule", "// header\n", ["// u0\n", "// u1\n"]);
        AotArtifactWriter.Write(
            new AotTarget("TestModule", false),
            new GeneratorConfig("Cpu", false, directory.Path),
            files,
            default,
            partitionJson: "{\"B\":2}\n");

        var partitionPath = Path.Combine(directory.Path, "Kassyi.GSharp.Kernels_TestModule.partition.json");
        Assert.True(File.Exists(partitionPath));

        AotArtifactWriter.WriteEmptyManifest(
            new AotTarget("TestModule", false),
            new GeneratorConfig("Cpu", false, directory.Path),
            default);

        Assert.False(File.Exists(partitionPath));
        Assert.Equal(string.Empty, File.ReadAllText(Path.Combine(directory.Path, AotArtifactWriter.ManifestFileName)));
    }

    private static NativeModuleFiles CreateSplitFiles(string moduleName, string headerBody, string[] unitBodies)
    {
        var stem = "Kassyi.GSharp.Kernels_" + moduleName;
        var header = new NativeModuleFile(stem + ".g.hpp", headerBody, IncludeErrorSites: true);
        var units = System.Collections.Immutable.ImmutableArray.CreateBuilder<NativeModuleFile>(unitBodies.Length);
        for (var i = 0; i < unitBodies.Length; i++)
        {
            var fileName = i == 0 ? stem + ".g.cpp" : $"{stem}.{i}.g.cpp";
            units.Add(new NativeModuleFile(fileName, unitBodies[i], IncludeErrorSites: false));
        }

        return new NativeModuleFiles(header, units.MoveToImmutable());
    }

    private static string[] WriteSplitAndAge(string directory, NativeModuleFiles files)
    {
        AotArtifactWriter.Write(
            new AotTarget("TestModule", false),
            new GeneratorConfig("Cpu", false, directory),
            files,
            default);

        var headerName = files.Header?.FileName
            ?? throw new InvalidOperationException("Expected split header.");
        string[] paths =
        [
            Path.Combine(directory, headerName),
            Path.Combine(directory, headerName + ".gsym.json"),
            Path.Combine(directory, files.Units[0].FileName),
            Path.Combine(directory, files.Units[0].FileName + ".gsym.json"),
            Path.Combine(directory, files.Units[1].FileName),
            Path.Combine(directory, files.Units[1].FileName + ".gsym.json"),
            Path.Combine(directory, AotArtifactWriter.ManifestFileName)
        ];
        foreach (var path in paths)
        {
            File.SetLastWriteTimeUtc(path, OldTime);
        }

        return paths;
    }

    private static readonly DateTime OldTime = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly record struct ArtifactPaths(string Source, string SymbolFile, string Manifest);

    private static void Write(string directory, string body) =>
        AotArtifactWriter.Write(
            new AotTarget("TestModule", false),
            new GeneratorConfig("Cpu", false, directory),
            body,
            default
        );

    // Writes the artifacts of a CPU module and sets their write times back to OldTime
    private static ArtifactPaths WriteAndAge(string directory, string body)
    {
        Write(directory, body);
        var source = Path.Combine(directory, "Kassyi.GSharp.Kernels_TestModule.g.cpp");
        var artifacts = new ArtifactPaths(
            source,
            source + ".gsym.json",
            Path.Combine(directory, AotArtifactWriter.ManifestFileName)
        );
        File.SetLastWriteTimeUtc(artifacts.Source, OldTime);
        File.SetLastWriteTimeUtc(artifacts.SymbolFile, OldTime);
        File.SetLastWriteTimeUtc(artifacts.Manifest, OldTime);
        return artifacts;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
