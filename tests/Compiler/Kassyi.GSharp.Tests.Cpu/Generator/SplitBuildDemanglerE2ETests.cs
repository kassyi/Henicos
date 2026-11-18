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
    public async Task SplitArtifacts_CpuTestBuild_EmitsHeaderErrorSitesAndDemanglesUnits()
    {
        var objDir = LocateActiveObjDirectory();
        var unitCount = ReadPartitionUnitCount(Path.Combine(objDir, PartitionFileName));
        Assert.True(unitCount > 1, $"Expected split CPU module (B > 1), found B = {unitCount}.");

        // 1. Verify manifest lists exactly B unit files and never lists the shared header.
        var manifestLines = File.ReadAllLines(Path.Combine(objDir, ManifestFileName))
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        Assert.Equal(unitCount, manifestLines.Length);
        Assert.Equal($"{ModuleStem}.g.cpp", manifestLines[0]);
        for (var i = 1; i < unitCount; i++)
        {
            Assert.Equal($"{ModuleStem}.{i}.g.cpp", manifestLines[i]);
        }

        Assert.DoesNotContain(
            manifestLines,
            static line => line.EndsWith(".g.hpp", StringComparison.OrdinalIgnoreCase));

        // 2. Verify shared header + .gsym.json carry matching hashes and module error sites.
        var headerPath = Path.Combine(objDir, $"{ModuleStem}.g.hpp");
        var headerGsymPath = headerPath + ".gsym.json";
        Assert.True(File.Exists(headerPath), $"Missing split header '{headerPath}'.");
        Assert.True(File.Exists(headerGsymPath), $"Missing split header symbol file '{headerGsymPath}'.");

        var headerSource = File.ReadAllText(headerPath, Encoding.UTF8);
        var headerFirstLine = ReadFirstLine(headerPath);
        Assert.True(
            GSharpNameEncoding.TryReadSymbolHeaderHash(headerFirstLine, out var headerHash),
            $"Missing symbol header line in '{headerPath}'.");
        Assert.Equal(GSharpNameEncoding.ComputeSourceHash(headerSource), headerHash);

        var headerGsymText = File.ReadAllText(headerGsymPath, Encoding.UTF8);
        var headerErrorSites = GSharpNameEncoding.ParseErrorSitesFromSymbolFile(headerGsymText, headerHash);
        Assert.NotEmpty(headerErrorSites);
        var headerSymbols = GSharpNameEncoding.ParseSymbolFile(headerGsymText, headerHash);

        // 3. Verify every unit has matching hash and zero error sites in its unit .gsym.json.
        string? nonZeroUnitKernelCpp = null;
        string? nonZeroUnitKernelCs = null;
        for (var i = 0; i < manifestLines.Length; i++)
        {
            var unitPath = Path.Combine(objDir, manifestLines[i]);
            var unitGsymPath = unitPath + ".gsym.json";
            Assert.True(File.Exists(unitPath), $"Missing unit source '{unitPath}'.");
            Assert.True(File.Exists(unitGsymPath), $"Missing unit symbol file '{unitGsymPath}'.");

            var unitSource = File.ReadAllText(unitPath, Encoding.UTF8);
            var unitFirstLine = ReadFirstLine(unitPath);
            Assert.True(
                GSharpNameEncoding.TryReadSymbolHeaderHash(unitFirstLine, out var unitHash),
                $"Missing symbol header line in '{unitPath}'.");
            Assert.Equal(GSharpNameEncoding.ComputeSourceHash(unitSource), unitHash);

            var unitGsymText = File.ReadAllText(unitGsymPath, Encoding.UTF8);
            var unitErrorSites = GSharpNameEncoding.ParseErrorSitesFromSymbolFile(unitGsymText, unitHash);
            Assert.Empty(unitErrorSites);

            var unitSymbols = GSharpNameEncoding.ParseSymbolFile(unitGsymText, unitHash);
            if (i > 0 && nonZeroUnitKernelCpp is null)
            {
                foreach (var pair in unitSymbols)
                {
                    if (pair.Key.StartsWith(GSharpNameEncoding.KernelPrefix, StringComparison.Ordinal))
                    {
                        nonZeroUnitKernelCpp = pair.Key;
                        nonZeroUnitKernelCs = pair.Value;
                        break;
                    }
                }
            }
        }

        Assert.NotNull(nonZeroUnitKernelCpp);
        Assert.NotNull(nonZeroUnitKernelCs);

        string? headerSymbolCpp = null;
        string? headerSymbolCs = null;
        foreach (var pair in headerSymbols)
        {
            if (pair.Key.StartsWith(GSharpNameEncoding.FunctionPrefix, StringComparison.Ordinal) ||
                pair.Key.StartsWith(GSharpNameEncoding.TypePrefix, StringComparison.Ordinal))
            {
                headerSymbolCpp = pair.Key;
                headerSymbolCs = pair.Value;
                break;
            }
        }

        // 4. Demangle a synthetic log referencing non-unit-0 kernel + header symbols via CLI.
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
