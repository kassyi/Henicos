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

    /// <summary>Parses command-line arguments into a <see cref="ReduceCommandOptions"/> record.</summary>
    public static bool TryParseOptions(
        ReadOnlySpan<string> args,
        out ReduceCommandOptions? options,
        out bool showHelp,
        out string? error)
    {
        options = null;
        showHelp = false;
        error = null;

        string? sourcePath = null;
        ulong fuzzSeed = 1UL;
        string? kernelName = null;
        int inputSeed = 0;
        string profile = "C";
        string workDir = Path.Combine("tmp", "differential-fuzz");
        string? repoRoot = null;
        string? testCmd = null;
        string? outPath = null;
        string failingPaths = "Native,Cuda";
        string failureKind = "CellMismatch";
        string column = "hash";
        bool noGpuLock = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    showHelp = true;
                    return true;

                case "--source":
                    if (i + 1 >= args.Length)
                    {
                        error = "--source requires a file path.";
                        return false;
                    }

                    sourcePath = args[++i];
                    break;

                case "--fuzz-seed":
                    if (i + 1 >= args.Length || !FuzzRandom.TryParseSeed(args[++i], out fuzzSeed))
                    {
                        error = "--fuzz-seed requires a 64-bit integer.";
                        return false;
                    }

                    break;

                case "--kernel":
                    if (i + 1 >= args.Length)
                    {
                        error = "--kernel requires a method name.";
                        return false;
                    }

                    kernelName = args[++i];
                    break;

                case "--input-seed":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out inputSeed))
                    {
                        error = "--input-seed requires an integer.";
                        return false;
                    }

                    break;

                case "--profile":
                    if (i + 1 >= args.Length)
                    {
                        error = "--profile requires M|C|G.";
                        return false;
                    }

                    profile = KnownIssueFilter.NormalizeProfile(args[++i]);
                    break;

                case "--work-dir":
                    if (i + 1 >= args.Length)
                    {
                        error = "--work-dir requires a directory path.";
                        return false;
                    }

                    workDir = args[++i];
                    break;

                case "--repo-root":
                    if (i + 1 >= args.Length)
                    {
                        error = "--repo-root requires a directory path.";
                        return false;
                    }

                    repoRoot = args[++i];
                    break;

                case "--test-cmd":
                    if (i + 1 >= args.Length)
                    {
                        error = "--test-cmd requires a command string.";
                        return false;
                    }

                    testCmd = args[++i];
                    break;

                case "--out":
                    if (i + 1 >= args.Length)
                    {
                        error = "--out requires a file path.";
                        return false;
                    }

                    outPath = args[++i];
                    break;

                case "--failing-paths":
                    if (i + 1 >= args.Length)
                    {
                        error = "--failing-paths requires a value.";
                        return false;
                    }

                    failingPaths = args[++i];
                    break;

                case "--failure-kind":
                    if (i + 1 >= args.Length)
                    {
                        error = "--failure-kind requires a value.";
                        return false;
                    }

                    failureKind = args[++i];
                    break;

                case "--column":
                    if (i + 1 >= args.Length)
                    {
                        error = "--column requires a column name.";
                        return false;
                    }

                    column = args[++i];
                    break;

                case "--no-gpu-lock":
                    noGpuLock = true;
                    break;

                default:
                    error = $"Unknown option '{arg}'.";
                    return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(sourcePath) && !File.Exists(sourcePath))
        {
            error = $"Source file not found: {sourcePath}";
            return false;
        }

        options = new ReduceCommandOptions(
            sourcePath,
            fuzzSeed,
            kernelName,
            inputSeed,
            profile,
            workDir,
            repoRoot,
            testCmd,
            outPath,
            failingPaths,
            failureKind,
            column,
            noGpuLock);
        return true;
    }

    /// <summary>Executes the <c>reduce</c> subcommand using the parsed <paramref name="options"/>.</summary>
    public static int Execute(ReduceCommandOptions options)
    {
        string initialSource;
        if (!string.IsNullOrWhiteSpace(options.SourcePath))
        {
            initialSource = File.ReadAllText(options.SourcePath, s_utf8NoBom);
            if (!string.IsNullOrWhiteSpace(options.KernelName))
            {
                initialSource = IsolateSingleKernelCompilationUnit(initialSource, options.KernelName);
            }
        }
        else
        {
            var gen = new ScalarFuzzGenerator(new FuzzOptions
            {
                FuzzSeed = options.FuzzSeed,
                InputSeed = options.InputSeed,
                BatchSize = 64,
                Profile = options.Profile,
            });
            var batch = gen.GenerateBatch();
            string target = options.KernelName ?? batch.Kernels[0].KernelName;
            initialSource = IsolateSingleKernelCompilationUnit(batch.KernelsSource, target);
        }

        var expectedSignature = FuzzFailureSignature.Create(options.FailingPaths, options.FailureKind, options.Column);
        string reducedSource;
        int origStmts = FuzzReducer.CountKernelExecutableStatements(initialSource);
        int iterations;

        if (!string.IsNullOrWhiteSpace(options.TestCmd))
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"fuzz_reduce_{Guid.NewGuid():N}.cu.cs");
            try
            {
                var res = FuzzReducer.Reduce(initialSource, candidate =>
                {
                    File.WriteAllText(tempFile, candidate, s_utf8NoBom);
                    string cmd = SubstitutePlaceholder(options.TestCmd, "{source}", tempFile);
                    return FuzzWorkspaceReducer.RunExternalShellPredicate(cmd);
                });
                reducedSource = res.ReducedSource;
                iterations = res.Iterations;
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
        else
        {
            (reducedSource, iterations) = FuzzWorkspaceReducer.ReduceWorkspaceWithSpeculativeBatches(
                initialSource,
                options.FuzzSeed,
                options.InputSeed,
                options.Profile,
                options.WorkDir,
                options.RepoRoot,
                expectedSignature,
                options.NoGpuLock);
        }

        if (!string.IsNullOrWhiteSpace(options.OutPath))
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(options.OutPath));
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(options.OutPath, reducedSource, s_utf8NoBom);
        }

        var fp = FuzzDeduplicator.ComputeFingerprint(reducedSource, options.FailingPaths, options.FailureKind);
        int finalStmts = FuzzReducer.CountKernelExecutableStatements(reducedSource);

        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            status = "reduced",
            fingerprint = fp.Fingerprint,
            originalStatements = origStmts,
            reducedStatements = finalStmts,
            iterations,
            failingPaths = fp.FailingPathsMask,
            failureKind = fp.FailureKindTag,
            column = expectedSignature.Column,
            outPath = options.OutPath is not null ? Path.GetFullPath(options.OutPath) : null,
        }));
        return 0;
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
