
namespace Henicos.PiBenchmark;

/// <summary>
/// Console application entry point and command-line dispatcher for the Henicos Pi benchmark suite.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return Execute(args);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"\n[ERROR] An unhandled exception occurred: {ex.Message}");
            Console.ResetColor();
            Console.Error.WriteLine(ex.ToString());
            return 1;
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
        Console.WriteLine("Henicos.PiBenchmark - High-Performance GPU & CPU Number Theory Suite");
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run -c Release -- <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  gpu-pi [digits] [options]  GPU Chudnovsky Pi computation (default: 10,000)");
        Console.WriteLine("                             Options:");
        Console.WriteLine("                               --out <path>        Write result digits to file");
        Console.WriteLine("                               --threshold <terms> CPU/GPU hybrid threshold (default: 8192)");
        Console.WriteLine("                               --hex               Output in hexadecimal without decimal conversion");
        Console.WriteLine("  cpu-pi [digits]            CPU Chudnovsky Pi baseline computation (default: 10,000)");
        Console.WriteLine("  tf [exponent] [maxP]       Mersenne trial factoring benchmark (default: 71428571, 4000000000)");
        Console.WriteLine("  prp [exponent] [iters]     Fermat probable prime test benchmark (default: 71428571, 10)");
        Console.WriteLine("  bbp [offset] [count]       Extract hexadecimal Pi digits via BBP (default: offset 0, count 16)");
        Console.WriteLine("  help, -h, --help           Display this help message");
        Console.WriteLine();
        Console.WriteLine("Digits supports SI prefixes like 100k, 1M, 100M (e.g. `gpu-pi 100M`).");
        return 0;
    }

    private static int HandleUnknownCommand(string command)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine($"[WARNING] Unknown command: '{command}'\n");
        Console.ResetColor();
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
                    Console.Error.WriteLine("Error: --threshold must be a positive integer greater than zero.");
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
        Console.WriteLine("Henicos.PiBenchmark - Chudnovsky Binary Splitting (CPU Baseline)");

        var digits = args.Length > 0 ? ParseDigits(args[0], 10000) : 10000;

        Console.WriteLine($"Calculating {digits:N0} digits of Pi...");

        var sw = Stopwatch.StartNew();
        var pi = ChudnovskyPi.ComputePi(digits);
        sw.Stop();

        Console.WriteLine(pi.Length > 100
            ? $"Result: {pi[..50]}...{pi[^50..]}"
            : $"Result: {pi}");
        Console.WriteLine($"Total Time: {sw.ElapsedMilliseconds} ms");

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

        Console.WriteLine(
            $"Extracting {count} hexadecimal digits of Pi starting at position {offset} (BBP Algorithm)...");
        var sw = Stopwatch.StartNew();
        var hex = BbpDigitExtractor.ExtractHexDigits(offset, count);
        sw.Stop();

        Console.WriteLine($"Hex Digits : {hex}");
        Console.WriteLine($"Time       : {sw.ElapsedMilliseconds} ms");
        return 0;
    }

    private static bool LooksLikeDigits(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        var trimmed = s.Trim().Replace("_", string.Empty).Replace(",", string.Empty);
        if (trimmed.EndsWith('k') || trimmed.EndsWith('K') ||
            trimmed.EndsWith('m') || trimmed.EndsWith('M') ||
            trimmed.EndsWith('g') || trimmed.EndsWith('G'))
        {
            trimmed = trimmed[..^1];
        }

        return double.TryParse(trimmed, out _);
    }

    private static int ParseDigits(string s, int defaultDigits = 10000)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return defaultDigits;
        }

        var trimmed = s.Trim().Replace("_", string.Empty).Replace(",", string.Empty);
        if (trimmed.EndsWith('k') || trimmed.EndsWith('K'))
        {
            if (double.TryParse(trimmed[..^1], out var val))
            {
                return (int)(val * 1_000);
            }
        }
        else if (trimmed.EndsWith('m') || trimmed.EndsWith('M'))
        {
            if (double.TryParse(trimmed[..^1], out var val))
            {
                return (int)(val * 1_000_000);
            }
        }
        else if (trimmed.EndsWith('g') || trimmed.EndsWith('G'))
        {
            if (double.TryParse(trimmed[..^1], out var val))
            {
                return (int)(val * 1_000_000_000);
            }
        }

        return int.TryParse(trimmed, out var parsed) ? parsed : defaultDigits;
    }
}
