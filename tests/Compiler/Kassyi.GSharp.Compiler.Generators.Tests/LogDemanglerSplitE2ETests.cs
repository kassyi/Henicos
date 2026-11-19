#nullable enable

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Kassyi.GSharp.Naming;

namespace Kassyi.GSharp.Compiler.Generators.Tests;

[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Compiler)]
public sealed class LogDemanglerSplitE2ETests
{
    private const string HeaderTypeCpp = "__gsharp_ty_SharedContext_h11111111";
    private const string HeaderTypeCs = "Sample.SharedContext";
    private const string HeaderFuncCpp = "__gsharp_fn_ComputeStep_h22222222";
    private const string HeaderFuncCs = "Sample.DeviceHelpers.ComputeStep";
    private const string Unit0KernelCpp = "__gsharp_k_PrimaryKernel_h33333333";
    private const string Unit0KernelCs = "Sample.PrimaryKernels.Run";
    private const string Unit1KernelCpp = "__gsharp_k_SecondaryKernel_h44444444";
    private const string Unit1KernelCs = "Sample.日本語Kernels.Execute";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Demangle_SplitLayoutDirectory_DecodesHeaderAndAllUnitsPreservingErrorSites()
    {
        using var scratch = new ScratchDirectory();
        WriteSplitModuleFixture(scratch, string.Empty, "\n");

        string[] inputLines =
        [
            $"error in {Unit0KernelCpp}: undefined {HeaderTypeCpp} using __gsharp_id_3_template",
            $"warning in {Unit1KernelCpp}: call to {HeaderFuncCpp} for __gsharp_gi_Scale_g1_bSystem__Int32_e",
            $"Index was out of range. in {Unit1KernelCpp} at SampleKernels.cu.cs(24,13) " +
            "in thread (0,0,0) of block (0,0,0)",
            "GSharpModuleContext_My_x00002eLib initialized",
            "plain diagnostic line without mangled symbols"
        ];
        string[] expectedLines =
        [
            $"error in {Unit0KernelCs}: undefined {HeaderTypeCs} using template",
            $"warning in {Unit1KernelCs}: call to {HeaderFuncCs} for Scale<System.Int32>",
            $"Index was out of range. in {Unit1KernelCs} at SampleKernels.cu.cs(24,13) " +
            "in thread (0,0,0) of block (0,0,0)",
            "GSharpModuleContext(My.Lib) initialized",
            "plain diagnostic line without mangled symbols"
        ];

        var logPath = scratch.Write("build.log", string.Join('\n', inputLines) + "\n");
        var result = await RunDemangleCliAsync([logPath, scratch.Path]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(expectedLines, ReadAllLines(result.Stdout));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Demangle_SplitLayoutStdinPipe_DecodesMixedMangledAndErrorSiteLogLines()
    {
        using var scratch = new ScratchDirectory();
        WriteSplitModuleFixture(scratch, string.Empty, "\n");

        string[] inputLines =
        [
            $"error in {Unit1KernelCpp}: {HeaderTypeCpp} -> {HeaderFuncCpp}",
            $"Invalid operation in {Unit0KernelCpp} at PrimaryKernels.cu.cs(12,5)"
        ];
        string[] expectedLines =
        [
            $"error in {Unit1KernelCs}: {HeaderTypeCs} -> {HeaderFuncCs}",
            $"Invalid operation in {Unit0KernelCs} at PrimaryKernels.cu.cs(12,5)"
        ];
        var stdinText = string.Join('\n', inputLines) + "\n";

        var explicitDash = await RunDemangleCliAsync(["-", scratch.Path], standardInput: stdinText);
        Assert.Equal(0, explicitDash.ExitCode);
        Assert.Equal(string.Empty, explicitDash.Stderr);
        Assert.Equal(expectedLines, ReadAllLines(explicitDash.Stdout));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Demangle_DefaultDiscoveryFromWorkingDirectory_FindsSplitObjSymbolMaps()
    {
        using var scratch = new ScratchDirectory();
        WriteSplitModuleFixture(scratch, Path.Combine("obj", "Debug", "net10.0"), "\n");

        var logLine = $"fatal in {Unit0KernelCpp} and {Unit1KernelCpp} referencing {HeaderFuncCpp}\n";
        var expectedLine = $"fatal in {Unit0KernelCs} and {Unit1KernelCs} referencing {HeaderFuncCs}";
        var logPath = scratch.Write("build.log", logLine);

        var fileResult = await RunDemangleCliAsync([logPath], workingDirectory: scratch.Path);
        Assert.Equal(0, fileResult.ExitCode);
        Assert.Equal(string.Empty, fileResult.Stderr);
        Assert.Equal([expectedLine], ReadAllLines(fileResult.Stdout));

        // Omitting both <log> and <symbols> reads stdin and discovers *.gsym.json under ./obj.
        var implicitStdinResult = await RunDemangleCliAsync(
            [],
            standardInput: logLine,
            workingDirectory: scratch.Path);
        Assert.Equal(0, implicitStdinResult.ExitCode);
        Assert.Equal(string.Empty, implicitStdinResult.Stderr);
        Assert.Equal([expectedLine], ReadAllLines(implicitStdinResult.Stdout));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Kassyi.GSharp.Kernels_Mod.1.g.cpp", true, false, false)]
    [InlineData("Kassyi.GSharp.Kernels_Mod.1.g.cpp.gsym.json", true, false, false)]
    [InlineData("Kassyi.GSharp.Kernels_Mod.g.hpp", false, true, false)]
    public async Task Demangle_SingleSplitUnitOrHeaderPath_DecodesTargetUnitSymbols(
        string targetRelativePath,
        bool expectsUnit1Decoded,
        bool expectsHeaderDecoded,
        bool expectsUnit0Decoded)
    {
        using var scratch = new ScratchDirectory();
        WriteSplitModuleFixture(scratch, string.Empty, "\n");

        var logPath = scratch.Write(
            "targeted.log",
            $"{HeaderTypeCpp} | {Unit0KernelCpp} | {Unit1KernelCpp}\n");
        var symbolsTarget = scratch.Combine(targetRelativePath);

        var result = await RunDemangleCliAsync([logPath, symbolsTarget]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);

        var line = Assert.Single(ReadAllLines(result.Stdout));
        var expectedHeader = expectsHeaderDecoded ? HeaderTypeCs : HeaderTypeCpp;
        var expectedUnit0 = expectsUnit0Decoded ? Unit0KernelCs : Unit0KernelCpp;
        var expectedUnit1 = expectsUnit1Decoded ? Unit1KernelCs : Unit1KernelCpp;
        Assert.Equal($"{expectedHeader} | {expectedUnit0} | {expectedUnit1}", line);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Demangle_CrlfSplitArtifacts_ComputesNormalizedHashAndDecodesSymbols()
    {
        using var scratch = new ScratchDirectory();
        var (lfHeader, lfUnit0, lfUnit1) = BuildSplitBodies("\n");
        var (crlfHeader, crlfUnit0, crlfUnit1) = BuildSplitBodies("\r\n");

        var expectedHeaderHash = GSharpNameEncoding.ComputeSourceHash(lfHeader);
        Assert.Equal(expectedHeaderHash, GSharpNameEncoding.ComputeSourceHash(crlfHeader));
        Assert.Equal(
            expectedHeaderHash,
            GSharpNameEncoding.ComputeSourceHash($"// gsharp-symbols: {expectedHeaderHash}\r\n{crlfHeader}"));
        Assert.Equal(
            GSharpNameEncoding.ComputeSourceHash(lfUnit0),
            GSharpNameEncoding.ComputeSourceHash(crlfUnit0));
        Assert.Equal(
            GSharpNameEncoding.ComputeSourceHash(lfUnit1),
            GSharpNameEncoding.ComputeSourceHash(crlfUnit1));

        WriteSplitModuleFixture(scratch, string.Empty, "\r\n");

        var logPath = scratch.Write(
            "crlf-build.log",
            $"error in {Unit1KernelCpp}: call to {HeaderFuncCpp} using {HeaderTypeCpp}\r\n");
        var result = await RunDemangleCliAsync([logPath, scratch.Path]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(
            [$"error in {Unit1KernelCs}: call to {HeaderFuncCs} using {HeaderTypeCs}"],
            ReadAllLines(result.Stdout));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Demangle_SplitUnitHashMismatch_FailsWithExitCodeOne()
    {
        using var scratch = new ScratchDirectory();
        WriteSplitModuleFixture(scratch, string.Empty, "\n");

        // Overwrite unit 1's // gsharp-symbols header hash so it no longer matches its .gsym.json file.
        var unit1Path = scratch.Combine("Kassyi.GSharp.Kernels_Mod.1.g.cpp");
        var (_, _, unit1Body) = BuildSplitBodies("\n");
        File.WriteAllText(unit1Path, "// gsharp-symbols: 0000000000000000\n" + unit1Body, new UTF8Encoding(false));

        var logPath = scratch.Write("err.log", $"{Unit1KernelCpp}\n");
        var result = GeneratorRun.Run("demangle", logPath, scratch.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("does not match source hash", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Demangle_MissingCompanionSymbolFile_FailsWithExitCodeOne()
    {
        using var scratch = new ScratchDirectory();
        var unit1Path = scratch.Write("Kassyi.GSharp.Kernels_Mod.1.g.cpp", "// empty unit without companion\n");
        var logPath = scratch.Write("err.log", $"{Unit1KernelCpp}\n");

        var result = GeneratorRun.Run("demangle", logPath, unit1Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("missing companion .gsym.json", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Demangle_MissingSymbolsOrLogPath_FailsWithExitCodeOne()
    {
        using var scratch = new ScratchDirectory();
        WriteSplitModuleFixture(scratch, string.Empty, "\n");

        var existingLog = scratch.Write("ok.log", "line\n");
        var missingSymbols = scratch.Combine("nonexistent_dir");
        var missingSymbolsResult = GeneratorRun.Run("demangle", existingLog, missingSymbols);
        Assert.Equal(1, missingSymbolsResult.ExitCode);
        Assert.Contains("Symbols path not found", missingSymbolsResult.StandardError, StringComparison.Ordinal);

        var missingLog = scratch.Combine("missing.log");
        var missingLogResult = GeneratorRun.Run("demangle", missingLog, scratch.Path);
        Assert.Equal(1, missingLogResult.ExitCode);
        Assert.Contains("Log file not found", missingLogResult.StandardError, StringComparison.Ordinal);
    }

    private static (string HeaderBody, string Unit0Body, string Unit1Body) BuildSplitBodies(string lineEnding)
    {
        var headerBody =
            $"#pragma once{lineEnding}" +
            $"struct {HeaderTypeCpp} {{ int v; }};{lineEnding}" +
            $"inline int {HeaderFuncCpp}(int x) {{ return x + 1; }}{lineEnding}";
        var unit0Body =
            $"#include \"Kassyi.GSharp.Kernels_Mod.g.hpp\"{lineEnding}" +
            $"extern \"C\" void {Unit0KernelCpp}() {{}}{lineEnding}";
        var unit1Body =
            $"#include \"Kassyi.GSharp.Kernels_Mod.g.hpp\"{lineEnding}" +
            $"extern \"C\" void {Unit1KernelCpp}() {{}}{lineEnding}";
        return (headerBody, unit0Body, unit1Body);
    }

    private static void WriteSplitModuleFixture(ScratchDirectory scratch, string subDir, string lineEnding)
    {
        var (headerBody, unit0Body, unit1Body) = BuildSplitBodies(lineEnding);
        var unit2Body = $"#include \"Kassyi.GSharp.Kernels_Mod.g.hpp\"{lineEnding}";

        var headerHash = ComputeFnv1A64Hex(headerBody);
        var unit0Hash = ComputeFnv1A64Hex(unit0Body);
        var unit1Hash = ComputeFnv1A64Hex(unit1Body);
        var unit2Hash = ComputeFnv1A64Hex(unit2Body);

        WriteArtifact(scratch, subDir, "Kassyi.GSharp.Kernels_Mod.g.hpp", headerBody, headerHash, lineEnding);
        WriteSymbolFile(
            scratch,
            subDir,
            "Kassyi.GSharp.Kernels_Mod.g.hpp.gsym.json",
            headerHash,
            [
                SymbolRow(HeaderTypeCpp, "type", HeaderTypeCs, headerHash),
                SymbolRow(HeaderFuncCpp, "function", HeaderFuncCs, headerHash),
                ErrorSiteRow(1, "SampleKernels.cu.cs", 24, 13, Unit1KernelCs, "Index was out of range.", headerHash),
                ErrorSiteRow(2, "PrimaryKernels.cu.cs", 12, 5, Unit0KernelCs, "Operation is invalid.", headerHash)
            ],
            lineEnding);

        WriteArtifact(scratch, subDir, "Kassyi.GSharp.Kernels_Mod.g.cpp", unit0Body, unit0Hash, lineEnding);
        WriteSymbolFile(
            scratch,
            subDir,
            "Kassyi.GSharp.Kernels_Mod.g.cpp.gsym.json",
            unit0Hash,
            [SymbolRow(Unit0KernelCpp, "kernel", Unit0KernelCs, unit0Hash)],
            lineEnding);

        WriteArtifact(scratch, subDir, "Kassyi.GSharp.Kernels_Mod.1.g.cpp", unit1Body, unit1Hash, lineEnding);
        WriteSymbolFile(
            scratch,
            subDir,
            "Kassyi.GSharp.Kernels_Mod.1.g.cpp.gsym.json",
            unit1Hash,
            [SymbolRow(Unit1KernelCpp, "kernel", Unit1KernelCs, unit1Hash)],
            lineEnding);

        WriteArtifact(scratch, subDir, "Kassyi.GSharp.Kernels_Mod.2.g.cpp", unit2Body, unit2Hash, lineEnding);
        WriteSymbolFile(
            scratch,
            subDir,
            "Kassyi.GSharp.Kernels_Mod.2.g.cpp.gsym.json",
            unit2Hash,
            [],
            lineEnding);
    }

    private static void WriteArtifact(
        ScratchDirectory scratch,
        string subDir,
        string fileName,
        string body,
        string hash,
        string lineEnding)
    {
        var relative = string.IsNullOrEmpty(subDir) ? fileName : Path.Combine(subDir, fileName);
        scratch.Write(relative, $"// gsharp-symbols: {hash}{lineEnding}{body}");
    }

    private static void WriteSymbolFile(
        ScratchDirectory scratch,
        string subDir,
        string fileName,
        string hash,
        string[] rows,
        string lineEnding)
    {
        var relative = string.IsNullOrEmpty(subDir) ? fileName : Path.Combine(subDir, fileName);
        var builder = new StringBuilder();
        builder.Append("{\"gsharp_symbols\":\"").Append(hash).Append("\",\"dialect\":\"Cpu\"}").Append(lineEnding);
        foreach (var row in rows)
        {
            builder.Append(row).Append(lineEnding);
        }

        scratch.Write(relative, builder.ToString());
    }

    private static string SymbolRow(string cpp, string kind, string cs, string hash) =>
        $"{{\"cpp\":\"{EscapeJson(cpp)}\",\"kind\":\"{kind}\"," +
        $"\"cs\":\"{EscapeJson(cs)}\",\"symbols\":\"{hash}\",\"dialect\":\"Cpu\"}}";

    private static string ErrorSiteRow(
        int siteId,
        string file,
        int line,
        int column,
        string member,
        string message,
        string hash) =>
        $"{{\"error_site\":\"{siteId.ToString(CultureInfo.InvariantCulture)}\"," +
        $"\"file\":\"{EscapeJson(file)}\"," +
        $"\"line\":\"{line.ToString(CultureInfo.InvariantCulture)}\"," +
        $"\"column\":\"{column.ToString(CultureInfo.InvariantCulture)}\"," +
        $"\"member\":\"{EscapeJson(member)}\"," +
        $"\"message\":\"{EscapeJson(message)}\"," +
        $"\"symbols\":\"{hash}\",\"dialect\":\"Cpu\"}}";

    private static string EscapeJson(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c == '"')
            {
                sb.Append("\\\"");
            }
            else if (c == '\\')
            {
                sb.Append("\\\\");
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static string ComputeFnv1A64Hex(string sourceBodyWithoutHeader)
    {
        var hash = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;
        for (var i = 0; i < sourceBodyWithoutHeader.Length; i++)
        {
            var c = sourceBodyWithoutHeader[i];
            if (c == '\r' && i + 1 < sourceBodyWithoutHeader.Length && sourceBodyWithoutHeader[i + 1] == '\n')
            {
                continue;
            }

            unchecked
            {
                hash ^= c;
                hash *= Prime;
            }
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunDemangleCliAsync(
        string[] args,
        string? standardInput = null,
        string? workingDirectory = null)
    {
        var generatorsDll = Path.Combine(AppContext.BaseDirectory, "Kassyi.GSharp.Compiler.Generators.dll");
        Assert.True(File.Exists(generatorsDll), $"Generator tool assembly not found at '{generatorsDll}'.");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory ?? AppContext.BaseDirectory,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(generatorsDll);
        startInfo.ArgumentList.Add("demangle");
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Kassyi.GSharp.Compiler.Generators process.");

        if (standardInput is not null)
        {
            await using var writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false));
            await writer.WriteAsync(standardInput);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(cts.Token);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (process.ExitCode, stdout, stderr);
    }

    private static List<string> ReadAllLines(string text)
    {
        var lines = new List<string>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }
}
