#nullable enable

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Kassyi.GSharp.Naming;

namespace Kassyi.GSharp.Tests.Cpu.Generator;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class SplitBuildDemanglerE2ETests
{
    private const string ModuleStem =
        "Kassyi.GSharp.Kernels_Kassyi_x00002eGSharp_x00002eTests_x00002eCpu";
    private const string ManifestFileName = "Kassyi.GSharp.Kernels.artifacts";
    private const string PartitionFileName = ModuleStem + ".partition.json";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Manifest_SplitCpuBuild_ListsEveryUnitAndNotTheHeader()
    {
        var (_, unitCount, manifestLines) = ReadSplitBuild();

        Assert.Equal(unitCount, manifestLines.Length);
        Assert.Equal($"{ModuleStem}.g.cpp", manifestLines[0]);
        for (var i = 1; i < unitCount; i++)
        {
            Assert.Equal($"{ModuleStem}.{i}.g.cpp", manifestLines[i]);
        }

        Assert.DoesNotContain(
            manifestLines,
            static line => line.EndsWith(".g.hpp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void HeaderSymbolFile_SplitCpuBuild_MatchesTheHeaderHashAndHoldsTheErrorSites()
    {
        var (objDir, _, _) = ReadSplitBuild();

        var (headerGsymText, headerHash) = ReadVerifiedSymbolFile(Path.Combine(objDir, $"{ModuleStem}.g.hpp"));

        Assert.NotEmpty(GSharpNameEncoding.ParseErrorSitesFromSymbolFile(headerGsymText, headerHash));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void UnitSymbolFiles_SplitCpuBuild_MatchTheUnitHashesAndHoldNoErrorSites()
    {
        var (objDir, _, manifestLines) = ReadSplitBuild();

        foreach (var unitFileName in manifestLines)
        {
            var (unitGsymText, unitHash) = ReadVerifiedSymbolFile(Path.Combine(objDir, unitFileName));

            Assert.Empty(GSharpNameEncoding.ParseErrorSitesFromSymbolFile(unitGsymText, unitHash));
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Demangler_SplitCpuBuild_DecodesAKernelOutsideUnitZeroAndAHeaderSymbol()
    {
        var (objDir, _, manifestLines) = ReadSplitBuild();
        var (nonZeroUnitKernelCpp, nonZeroUnitKernelCs) = manifestLines
            .Skip(1)
            .SelectMany(unitFileName => ReadSymbols(Path.Combine(objDir, unitFileName)))
            .FirstOrDefault(static pair =>
                pair.Key.StartsWith(GSharpNameEncoding.KernelPrefix, StringComparison.Ordinal));
        Assert.NotNull(nonZeroUnitKernelCpp);
        Assert.NotNull(nonZeroUnitKernelCs);
        var (headerSymbolCpp, headerSymbolCs) = ReadSymbols(Path.Combine(objDir, $"{ModuleStem}.g.hpp"))
            .FirstOrDefault(static pair =>
                pair.Key.StartsWith(GSharpNameEncoding.FunctionPrefix, StringComparison.Ordinal) ||
                pair.Key.StartsWith(GSharpNameEncoding.TypePrefix, StringComparison.Ordinal));

        var syntheticLog = headerSymbolCpp is not null
            ? $"error in {nonZeroUnitKernelCpp}: call to {headerSymbolCpp} failed\n"
            : $"error in {nonZeroUnitKernelCpp}: trap raised\n";
        var result = await RunDemanglerProcessAsync(objDir, syntheticLog);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Contains(nonZeroUnitKernelCs, result.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(nonZeroUnitKernelCpp, result.Stdout, StringComparison.Ordinal);
        if (headerSymbolCpp is not null && headerSymbolCs is not null)
        {
            Assert.Contains(headerSymbolCs, result.Stdout, StringComparison.Ordinal);
            Assert.DoesNotContain(headerSymbolCpp, result.Stdout, StringComparison.Ordinal);
        }
    }

    // The obj directory of this build, its unit count (B > 1) and the non-empty lines of its artifact manifest.
    private static (string ObjDir, int UnitCount, string[] ManifestLines) ReadSplitBuild()
    {
        var objDir = LocateActiveObjDirectory();
        var unitCount = ReadPartitionUnitCount(Path.Combine(objDir, PartitionFileName));
        Assert.True(unitCount > 1, $"Expected split CPU module (B > 1), found B = {unitCount}.");
        var manifestLines = File.ReadAllLines(Path.Combine(objDir, ManifestFileName))
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        return (objDir, unitCount, manifestLines);
    }

    // The symbol file of sourcePath, after checking that the source's line-1 hash matches its content.
    private static (string GsymText, string Hash) ReadVerifiedSymbolFile(string sourcePath)
    {
        var gsymPath = sourcePath + ".gsym.json";
        Assert.True(File.Exists(sourcePath), $"Missing generated source '{sourcePath}'.");
        Assert.True(File.Exists(gsymPath), $"Missing symbol file '{gsymPath}'.");
        Assert.True(
            GSharpNameEncoding.TryReadSymbolHeaderHash(ReadFirstLine(sourcePath), out var hash),
            $"Missing symbol header line in '{sourcePath}'.");
        Assert.Equal(GSharpNameEncoding.ComputeSourceHash(File.ReadAllText(sourcePath, Encoding.UTF8)), hash);
        return (File.ReadAllText(gsymPath, Encoding.UTF8), hash);
    }

    private static IReadOnlyDictionary<string, string> ReadSymbols(string sourcePath)
    {
        var (gsymText, hash) = ReadVerifiedSymbolFile(sourcePath);
        return GSharpNameEncoding.ParseSymbolFile(gsymText, hash);
    }

    private static string LocateActiveObjDirectory()
    {
        var projectDir = RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Cpu");
        var relativeFromBin = Path.GetRelativePath(Path.Combine(projectDir, "bin"), AppContext.BaseDirectory);
        var candidate = Path.Combine(projectDir, "obj", relativeFromBin);
        if (File.Exists(Path.Combine(candidate, PartitionFileName)))
        {
            return candidate;
        }

        var objRoot = Path.Combine(projectDir, "obj");
        Assert.True(Directory.Exists(objRoot), $"Intermediate directory '{objRoot}' not found.");

        string? newestFile = null;
        var newestTime = DateTime.MinValue;
        foreach (var file in Directory.EnumerateFiles(objRoot, PartitionFileName, SearchOption.AllDirectories))
        {
            var writeTime = File.GetLastWriteTimeUtc(file);
            if (newestFile is null || writeTime > newestTime)
            {
                newestFile = file;
                newestTime = writeTime;
            }
        }

        var resolvedDir = newestFile is null ? null : Path.GetDirectoryName(newestFile);
        Assert.True(resolvedDir is not null && Directory.Exists(resolvedDir), $"'{PartitionFileName}' not found.");
        return resolvedDir;
    }

    private static string LocateGeneratorsAssembly()
    {
        var testsProjectDir = RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Cpu");
        var relativeFromBin = Path.GetRelativePath(Path.Combine(testsProjectDir, "bin"), AppContext.BaseDirectory);
        var genProjectDir = RepositoryPaths.Combine("src", "Compiler", "Kassyi.GSharp.Compiler.Generators");
        var candidate = Path.Combine(
            genProjectDir,
            "bin",
            relativeFromBin,
            "Kassyi.GSharp.Compiler.Generators.dll");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var genBinRoot = Path.Combine(genProjectDir, "bin");
        Assert.True(Directory.Exists(genBinRoot), $"Generator binary directory '{genBinRoot}' not found.");

        string? newestDll = null;
        var newestTime = DateTime.MinValue;
        foreach (var file in Directory.EnumerateFiles(
            genBinRoot,
            "Kassyi.GSharp.Compiler.Generators.dll",
            SearchOption.AllDirectories))
        {
            var writeTime = File.GetLastWriteTimeUtc(file);
            if (newestDll is null || writeTime > newestTime)
            {
                newestDll = file;
                newestTime = writeTime;
            }
        }

        Assert.True(
            newestDll is not null && File.Exists(newestDll),
            "Could not locate Kassyi.GSharp.Compiler.Generators.dll.");
        return newestDll;
    }

    private static int ReadPartitionUnitCount(string partitionPath)
    {
        Assert.True(File.Exists(partitionPath), $"Partition file '{partitionPath}' not found.");
        using var stream = File.OpenRead(partitionPath);
        using var doc = JsonDocument.Parse(stream);
        Assert.Equal("Cpu", doc.RootElement.GetProperty("dialect").GetString());
        return doc.RootElement.GetProperty("B").GetInt32();
    }

    private static string ReadFirstLine(string path)
    {
        using var reader = File.OpenText(path);
        return reader.ReadLine() ?? string.Empty;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunDemanglerProcessAsync(
        string objDir,
        string standardInput)
    {
        var generatorsDll = LocateGeneratorsAssembly();
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = RepositoryPaths.Root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(generatorsDll);
        startInfo.ArgumentList.Add("demangle");
        startInfo.ArgumentList.Add("-");
        startInfo.ArgumentList.Add(objDir);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch demangler process.");

        await using (var writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)))
        {
            await writer.WriteAsync(standardInput);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(cts.Token);

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}
