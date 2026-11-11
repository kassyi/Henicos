using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Parsed command-line options for <c>fuzz-diff reduce</c>.
/// </summary>
public sealed record ReduceCommandOptions(
    string? SourcePath,
    ulong FuzzSeed,
    string? KernelName,
    int InputSeed,
    string Profile,
    string WorkDir,
    string? RepoRoot,
    string? TestCmd,
    string? OutPath,
    string FailingPaths,
    string FailureKind,
    string Column,
    bool NoGpuLock);

/// <summary>
/// Result of <see cref="ReduceCommand.ReduceInProcess"/>.
/// </summary>
public sealed record InProcessReduction(
    string ReducedSource,
    string Fingerprint,
    int OriginalStatements,
    int ReducedStatements,
    int Iterations);

/// <summary>
/// Implements the <c>fuzz-diff reduce</c> subcommand.
/// </summary>
public sealed class ReduceCommand : IFuzzCommand
{
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly string s_defaultWorkDir = Path.Combine("tmp", "differential-fuzz");

    /// <summary>Gets the singleton command instance.</summary>
    public static ReduceCommand Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "reduce";

    /// <summary>Runs the <c>reduce</c> subcommand.</summary>
    public static int Run(ReadOnlySpan<string> args) => ((IFuzzCommand)Instance).Run(args);

    /// <inheritdoc/>
    int IFuzzCommand.Run(ReadOnlySpan<string> args)
    {
        if (!TryParseOptions(args, out var options, out bool showHelp, out string? error))
        {
            return Program.FailUsage(error ?? "Invalid options for 'reduce'.");
        }

        if (showHelp)
        {
            Console.Out.WriteLine(Program.HelpText);
            return 0;
        }

        return Execute(options!);
    }

    private static readonly Dictionary<string, FuzzOptionSpec> s_options = FuzzOptionTable.CreateTable(
        new("--source", FuzzOptionTable.Value(FuzzOptionSetters.ParseSourcePath)),
        new("--fuzz-seed", FuzzOptionTable.Value(FuzzOptionTable.ParseFuzzSeed)),
        new("--kernel", FuzzOptionTable.Value(FuzzOptionSetters.ParseKernelName)),
        new("--input-seed", FuzzOptionTable.Value(FuzzOptionTable.ParseInputSeed)),
        new("--profile", FuzzOptionTable.Value(FuzzOptionSetters.ParseProfileCode)),
        new("--work-dir", FuzzOptionTable.Value(FuzzOptionSetters.ParseWorkDir)),
        new("--repo-root", FuzzOptionTable.Value(FuzzOptionSetters.ParseRepoRoot)),
        new("--test-cmd", FuzzOptionTable.Value(FuzzOptionSetters.ParseTestCmd)),
        new("--out", FuzzOptionTable.Value(FuzzOptionSetters.ParseOutPath)),
        new("--failing-paths", FuzzOptionTable.Value(FuzzOptionSetters.ParseFailingPaths)),
        new("--failure-kind", FuzzOptionTable.Value(FuzzOptionSetters.ParseFailureKind)),
        new("--column", FuzzOptionTable.Value(FuzzOptionSetters.ParseColumn)),
        new("--no-gpu-lock", FuzzOptionTable.Flag(static v => v.NoGpuLock = true)));

    /// <summary>Parses command-line arguments into a <see cref="ReduceCommandOptions"/> record.</summary>
    public static bool TryParseOptions(
        ReadOnlySpan<string> args,
        out ReduceCommandOptions? options,
        out bool showHelp,
        out string? error)
    {
        options = null;
        var values = new FuzzOptionValues { WorkDir = s_defaultWorkDir };

        if (!FuzzOptionTable.TryParse(args, s_options, values, out showHelp, out error) || showHelp)
        {
            return showHelp;
        }

        if (!string.IsNullOrWhiteSpace(values.SourcePath) && !File.Exists(values.SourcePath))
        {
            error = $"Source file not found: {values.SourcePath}";
            return false;
        }

        options = new ReduceCommandOptions(
            values.SourcePath,
            values.FuzzSeed,
            values.KernelName,
            values.InputSeed ?? 0,
            values.ProfileCode,
            values.WorkDir ?? s_defaultWorkDir,
            values.RepoRoot,
            values.TestCmd,
            values.OutPath,
            values.FailingPaths,
            values.FailureKind,
            values.Column,
            values.NoGpuLock);
        return true;
    }

    /// <summary>
    /// Returns the arguments that <see cref="TryParseOptions"/> reads back into <paramref name="options"/>; an
    /// option whose value is <see langword="null"/> or <see langword="false"/> is left out.
    /// </summary>
    internal static List<string> ToArguments(ReduceCommandOptions options)
    {
        var args = new List<string>();
        AddIfSet(args, "--source", options.SourcePath);
        args.Add("--fuzz-seed");
        args.Add(FuzzRandom.FormatSeed(options.FuzzSeed));
        AddIfSet(args, "--kernel", options.KernelName);
        args.Add("--input-seed");
        args.Add(options.InputSeed.ToString(CultureInfo.InvariantCulture));
        args.AddRange(["--profile", options.Profile, "--work-dir", options.WorkDir]);
        AddIfSet(args, "--repo-root", options.RepoRoot);
        AddIfSet(args, "--test-cmd", options.TestCmd);
        args.AddRange(
        [
            "--failing-paths", options.FailingPaths,
            "--failure-kind", options.FailureKind,
            "--column", options.Column,
        ]);
        AddIfSet(args, "--out", options.OutPath);
        if (options.NoGpuLock)
        {
            args.Add("--no-gpu-lock");
        }

        return args;
    }

    private static void AddIfSet(List<string> args, string name, string? value)
    {
        if (value is not null)
        {
            args.Add(name);
            args.Add(value);
        }
    }

    /// <summary>Executes the <c>reduce</c> subcommand using the parsed <paramref name="options"/>.</summary>
    public static int Execute(ReduceCommandOptions options)
    {
        string initialSource = ResolveInitialSource(options);
        var expectedSignature = FuzzFailureSignature.Create(options.FailingPaths, options.FailureKind, options.Column);
        int origStmts = FuzzReducer.CountKernelExecutableStatements(initialSource);

        var (reducedSource, iterations) = ExecuteReduction(initialSource, options, expectedSignature);
        WriteOutputFileIfRequested(options.OutPath, reducedSource);

        var fp = FuzzDeduplicator.ComputeFingerprint(reducedSource, options.FailingPaths, options.FailureKind);
        int finalStmts = FuzzReducer.CountKernelExecutableStatements(reducedSource);
        PrintReductionResult(fp, origStmts, finalStmts, iterations, expectedSignature.Column, options.OutPath);
        return 0;
    }

    private static string ResolveInitialSource(ReduceCommandOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SourcePath))
        {
            string source = File.ReadAllText(options.SourcePath, s_utf8NoBom);
            return !string.IsNullOrWhiteSpace(options.KernelName)
                ? IsolateSingleKernelCompilationUnit(source, options.KernelName)
                : source;
        }

        var gen = new ScalarFuzzGenerator(new FuzzOptions
        {
            FuzzSeed = options.FuzzSeed,
            InputSeed = options.InputSeed,
            BatchSize = 64,
            Profile = options.Profile,
        });
        var batch = gen.GenerateBatch();
        string target = options.KernelName ?? batch.Kernels[0].KernelName;
        return IsolateSingleKernelCompilationUnit(batch.KernelsSource, target);
    }

    private static (string ReducedSource, int Iterations) ExecuteReduction(
        string initialSource,
        ReduceCommandOptions options,
        FuzzFailureSignature expectedSignature)
    {
        if (!string.IsNullOrWhiteSpace(options.TestCmd))
        {
            return ReduceWithTestCommand(initialSource, options.TestCmd);
        }

        return FuzzWorkspaceReducer.ReduceWorkspaceWithSpeculativeBatches(
            initialSource,
            options.FuzzSeed,
            options.InputSeed,
            options.Profile,
            options.WorkDir,
            options.RepoRoot,
            expectedSignature,
            options.NoGpuLock);
    }

    private static (string ReducedSource, int Iterations) ReduceWithTestCommand(string initialSource, string testCmd)
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"fuzz_reduce_{Guid.NewGuid():N}.cu.cs");
        try
        {
            var res = FuzzReducer.Reduce(initialSource, candidate =>
            {
                File.WriteAllText(tempFile, candidate, s_utf8NoBom);
                string cmd = SubstitutePlaceholder(testCmd, "{source}", tempFile);
                return FuzzWorkspaceReducer.RunExternalShellPredicate(cmd);
            });
            return (res.ReducedSource, res.Iterations);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static void WriteOutputFileIfRequested(string? outPath, string reducedSource)
    {
        if (string.IsNullOrWhiteSpace(outPath))
        {
            return;
        }

        string? dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(outPath, reducedSource, s_utf8NoBom);
    }

    private static void PrintReductionResult(
        FuzzFingerprintResult fp,
        int origStmts,
        int finalStmts,
        int iterations,
        string column,
        string? outPath)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            status = "reduced",
            fingerprint = fp.Fingerprint,
            originalStatements = origStmts,
            reducedStatements = finalStmts,
            iterations,
            failingPaths = fp.FailingPathsMask,
            failureKind = fp.FailureKindTag,
            column,
            outPath = outPath is not null ? Path.GetFullPath(outPath) : null,
        }));
    }

    /// <summary>
    /// Reduces <paramref name="source"/> in this process while <paramref name="isInteresting"/> holds, for a caller
    /// that can test a candidate itself instead of through <c>--test-cmd</c>, and fingerprints the result.
    /// </summary>
    public static InProcessReduction ReduceInProcess(
        string source,
        Func<string, bool> isInteresting,
        string failingPaths,
        string failureKind)
    {
        var result = FuzzReducer.Reduce(source, isInteresting);
        var fingerprint = FuzzDeduplicator.ComputeFingerprint(result.ReducedSource, failingPaths, failureKind);
        return new InProcessReduction(
            result.ReducedSource,
            fingerprint.Fingerprint,
            result.OriginalStatementCount,
            result.ReducedStatementCount,
            result.Iterations);
    }

    internal static string IsolateSingleKernelCompilationUnit(string source, string targetKernelName)
    {
        var root = FuzzReducer.ParseRoot(source);
        var kernels = FuzzReducer.GetKernelMethods(root);
        MethodDeclarationSyntax? selected = null;
        foreach (var method in kernels)
        {
            if (string.Equals(method.Identifier.ValueText, targetKernelName, StringComparison.Ordinal))
            {
                selected = method;
                break;
            }
        }

        if (selected is null)
        {
            return source;
        }

        var others = new List<MethodDeclarationSyntax>();
        foreach (var method in kernels)
        {
            if (method != selected)
            {
                others.Add(method);
            }
        }

        if (others.Count == 0)
        {
            return source;
        }

        var updated = root.RemoveNodes(others, SyntaxRemoveOptions.KeepNoTrivia);
        return updated is not null ? FuzzSyntaxHelpers.RenderFullNode(updated) : source;
    }

    private static string SubstitutePlaceholder(string template, string placeholder, string replacement)
    {
        int index = template.IndexOf(placeholder, StringComparison.Ordinal);
        if (index < 0)
        {
            return template;
        }

        ReadOnlySpan<char> before = template.AsSpan(0, index);
        ReadOnlySpan<char> after = template.AsSpan(index + placeholder.Length);
        return string.Concat(before, replacement, after);
    }
}
