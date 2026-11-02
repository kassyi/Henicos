using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Implements the <c>fuzz-diff dedup</c> subcommand.
/// </summary>
public static class DedupCommand
{
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Runs the <c>dedup</c> subcommand.</summary>
    public static int Run(ReadOnlySpan<string> args)
    {
        string? sourcePath = null;
        string failingPaths = "Native,Cuda";
        string failureKind = "CellMismatch";
        bool json = false;

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

                case "--json":
                    json = true;
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
        var fp = FuzzDeduplicator.ComputeFingerprint(source, failingPaths, failureKind);
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                fingerprint = fp.Fingerprint,
                hash64 = string.Create(CultureInfo.InvariantCulture, $"0x{fp.Hash64:X16}"),
                failingPaths = fp.FailingPathsMask,
                failureKind = fp.FailureKindTag,
                normalizedAstSkeleton = fp.NormalizedAstSkeleton,
            }));
        }
        else
        {
            Console.Out.WriteLine(fp.Fingerprint);
        }

        return 0;
    }
}

