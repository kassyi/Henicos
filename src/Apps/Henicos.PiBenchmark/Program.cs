
using Kassyi.GSharp.Core;

namespace Henicos.PiBenchmark;

/// <summary>
/// Console application entry point and command-line dispatcher for the Henicos Pi benchmark suite.
/// </summary>
internal class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return Execute(args);
        }
        catch (Exception ex)
        {
            Logger<Program>.Error(ex, "An unhandled exception occurred");
            return 1;
        }
        finally
        {
            Logger.Flush();
        }
    }

    private static int Execute(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].ToLowerInvariant();

        return command switch
        {
            "-h" or "--help" or "help" => PrintUsage(),
            "gpu-pi" => RunGpuPi(args[1..]),
            "cpu-pi" => RunCpuPi(args[1..]),
            "tf" => RunTrialFactoring(args[1..]),
            "prp" => RunFermatPrp(args[1..]),
            "bbp" => RunBbp(args[1..]),
            _ when LooksLikeDigits(args[0]) => RunCpuPi(args),
            _ => HandleUnknownCommand(command)
        };
    }

    private static int PrintUsage()
    {
        const string usage = """
            Henicos.PiBenchmark - High-Performance GPU & CPU Number Theory Suite

            Usage: dotnet run -c Release -- <command> [options]

            Commands:
              gpu-pi [digits] [options]  GPU Chudnovsky Pi computation (default: 10,000)
                                         Options:
                                           --out <path>        Write result digits to file
                                           --threshold <terms> CPU/GPU hybrid threshold (default: 8192)
                                           --hex               Output in hexadecimal without decimal conversion
              cpu-pi [digits]            CPU Chudnovsky Pi baseline computation (default: 10,000)
              tf [exponent] [maxP]       Mersenne trial factoring benchmark (default: 71428571, 4000000000)
              prp [exponent] [iters]     Fermat probable prime test benchmark (default: 71428571, 10)
              bbp [offset] [count]       Extract hexadecimal Pi digits via BBP (default: offset 0, count 16)
              help, -h, --help           Display this help message

            Digits supports SI prefixes like 100k, 1M, 100M (e.g. `gpu-pi 100M`).
            """;
        Logger<Program>.Info(usage);
        return 0;
    }

    private static int HandleUnknownCommand(string command)
    {
        Logger<Program>.Warning($"Unknown command: '{command}'\n");
        PrintUsage();
        return 1;
    }

    private static int RunGpuPi(string[] args)
    {
        var targetDigits = 10000;
        string? outPath = null;
        var thresholdTerms = 8192;
        var asHex = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                outPath = args[++i];
            }
            else if (arg.Equals("--threshold", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var thresh) && thresh > 0)
                {
                    thresholdTerms = thresh;
                }
                else
                {
                    Logger<Program>.Error("Error: --threshold must be a positive integer greater than zero.");
                    return 1;
                }
            }
            else if (arg.Equals("--hex", StringComparison.OrdinalIgnoreCase))
            {
                asHex = true;
            }
            else if (!arg.StartsWith('-') && i == 0)
            {
                targetDigits = ParseDigits(arg, 10000);
            }
        }

        GpuChudnovskyPi.ComputePiAndWriteToFile(targetDigits, outPath, thresholdTerms, asHex);
        return 0;
    }

    private static int RunCpuPi(string[] args)
    {
        Logger<Program>.Info("Henicos.PiBenchmark - Chudnovsky Binary Splitting (CPU Baseline)");

        var digits = args.Length > 0 ? ParseDigits(args[0], 10000) : 10000;

        Logger<Program>.Info($"Calculating {digits:N0} digits of Pi...");

        var sw = Stopwatch.StartNew();
        var pi = ChudnovskyPi.ComputePi(digits);
        sw.Stop();

        Logger<Program>.Info(pi.Length > 100
            ? $"Result: {pi[..50]}...{pi[^50..]}"
            : $"Result: {pi}");
        Logger<Program>.Info($"Total Time: {sw.ElapsedMilliseconds} ms");

        return 0;
    }

    private static int RunTrialFactoring(string[] args)
    {
        var exponent = 71428571;
        var maxP = 4000000000u;

        if (args.Length > 0 && int.TryParse(args[0], out var e))
        {
            exponent = e;
        }

        if (args.Length > 1 && uint.TryParse(args[1], out var mp))
        {
            maxP = mp;
        }

        TrialFactoringBenchmark.RunTrialFactoring(exponent, maxP);
        return 0;
    }

    private static int RunFermatPrp(string[] args)
    {
        var exponent = 71428571;
        var iterations = 10;

        if (args.Length > 0 && int.TryParse(args[0], out var e))
        {
            exponent = e;
        }

        if (args.Length > 1 && int.TryParse(args[1], out var i))
        {
            iterations = i;
        }

        FermatPrpBenchmark.RunFermatTest(exponent, iterations);
        return 0;
    }

    private static int RunBbp(string[] args)
    {
        var offset = 0;
        var count = 16;

        if (args.Length > 0 && int.TryParse(args[0], out var o))
        {
            offset = o;
        }

        if (args.Length > 1 && int.TryParse(args[1], out var c))
        {
            count = c;
        }

        Logger<Program>.Info(
            $"Extracting {count} hexadecimal digits of Pi starting at position {offset} (BBP Algorithm)...");
        var sw = Stopwatch.StartNew();
        var hex = BbpDigitExtractor.ExtractHexDigits(offset, count);
        sw.Stop();

        Logger<Program>.Info($"Hex Digits : {hex}");
        Logger<Program>.Info($"Time       : {sw.ElapsedMilliseconds} ms");
        return 0;
    }

    private static bool LooksLikeDigits(string s) => TryParseDigits(s, out _);

    private static int ParseDigits(string s, int defaultDigits = 10000) =>
        TryParseDigits(s, out var digits) ? digits : defaultDigits;

    private static bool TryParseDigits(string? s, out int result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        var trimmed = s.Trim().Replace("_", string.Empty).Replace(",", string.Empty);
        if (trimmed.Length == 0)
        {
            return false;
        }

        var (multiplier, numberPart) = char.ToUpperInvariant(trimmed[^1]) switch
        {
            'K' => (1_000.0, trimmed[..^1]),
            'M' => (1_000_000.0, trimmed[..^1]),
            'G' => (1_000_000_000.0, trimmed[..^1]),
            _ => (1.0, trimmed)
        };

        if (double.TryParse(numberPart, System.Globalization.CultureInfo.InvariantCulture, out var val))
        {
            result = (int)(val * multiplier);
            return true;
        }

        return false;
    }
}
