using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Implements the <c>fuzz-diff promote</c> subcommand.
/// </summary>
public static class PromoteCommand
{
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Runs the <c>promote</c> subcommand.</summary>
    public static int Run(ReadOnlySpan<string> args)
    {
        string? sourcePath = null;
        ulong fuzzSeed = 1UL;
        int inputSeed = 0;
        string failingPaths = "Native,Cuda";
        string failureKind = "CellMismatch";
        string findingsDir = Path.Combine("tmp", "differential-fuzz", "findings");
        string? kernelName = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    Console.Out.WriteLine(Program.HelpText);
                    return 0;

                case "--source":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--source requires a file path.");
                    }

                    sourcePath = args[++i];
                    break;

                case "--fuzz-seed":
                    if (i + 1 >= args.Length || !FuzzRandom.TryParseSeed(args[++i], out fuzzSeed))
                    {
                        return Program.FailUsage("--fuzz-seed requires a 64-bit integer.");
                    }

                    break;

                case "--input-seed":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out inputSeed))
                    {
                        return Program.FailUsage("--input-seed requires an integer.");
                    }

                    break;

                case "--failing-paths":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--failing-paths requires a value.");
                    }

                    failingPaths = args[++i];
                    break;

                case "--failure-kind":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--failure-kind requires a value.");
                    }

                    failureKind = args[++i];
                    break;

                case "--findings-dir":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--findings-dir requires a directory path.");
                    }

                    findingsDir = args[++i];
                    break;

                case "--kernel":
                    if (i + 1 >= args.Length)
                    {
                        return Program.FailUsage("--kernel requires a kernel method name.");
                    }

                    kernelName = args[++i];
                    break;

                default:
                    return Program.FailUsage($"Unknown option '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return Program.FailUsage("--source requires an existing .cu.cs file.");
        }

        string source = File.ReadAllText(sourcePath, s_utf8NoBom);
        var artifacts = FuzzPromoter.BuildPromotionArtifacts(
            source,
            fuzzSeed,
            inputSeed,
            failingPaths,
            failureKind,
            targetKernelName: kernelName);
        string writtenPath = FuzzPromoter.WritePromotedFinding(findingsDir, artifacts, fuzzSeed);

        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            status = "promoted",
            fingerprint = artifacts.Fingerprint,
            reproKernelPath = writtenPath,
            reproClassName = artifacts.ReproKernelClassName,
            reproMethodName = artifacts.ReproKernelMethodName,
        }));
        return 0;
    }
}

