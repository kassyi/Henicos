using System.Diagnostics;

namespace Henicos.PiBenchmark;

/// <summary>Console application entry point for the Chudnovsky Pi benchmark baseline.</summary>
class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Henicos.PiBenchmark - Chudnovsky Binary Splitting (CPU Baseline)");

        if (args.Length > 0 && args[0] == "prp")
        {
            var iterations = 10;
            if (args.Length > 1 && int.TryParse(args[1], out var i))
            {
                iterations = i;
            }

            FermatPrpBenchmark.RunFermatTest(iterations);
            return;
        }

        if (args.Length > 0 && args[0] == "bbp")
        {
            var offset = 0;
            if (args.Length > 1 && int.TryParse(args[1], out var o))
            {
                offset = o;
            }

            var count = 16;
            if (args.Length > 2 && int.TryParse(args[2], out var c))
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
            return;
        }

        if (args.Length > 0 && args[0] == "gpu-pi")
        {
            var targetDigits = 10000;
            if (args.Length > 1 && int.TryParse(args[1], out var parsed))
            {
                targetDigits = parsed;
            }

            string? outPath = null;
            int outIdx = Array.IndexOf(args, "--out");
            if (outIdx >= 0 && outIdx + 1 < args.Length)
            {
                outPath = args[outIdx + 1];
            }

            int thresholdTerms = 16;
            int threshIdx = Array.IndexOf(args, "--threshold");
            if (threshIdx >= 0 && threshIdx + 1 < args.Length && int.TryParse(args[threshIdx + 1], out var t))
            {
                thresholdTerms = t;
            }

            bool asHex = Array.IndexOf(args, "--hex") >= 0;
            GpuChudnovskyPi.ComputePiAndWriteToFile(targetDigits, outPath, thresholdTerms, asHex);
            return;
        }

        var digits = 10000;
        if (args.Length > 0 && int.TryParse(args[0], out var d))
        {
            digits = d;
        }

        Console.WriteLine($"Calculating {digits} digits of Pi...");

        var pi = ChudnovskyPi.ComputePi(digits);

        // Output first 50 and last 50 digits for verification
        Console.WriteLine(pi.Length > 100
            ? $"Result: {pi.Substring(0, 50)}...{pi.Substring(pi.Length - 50)}"
            : $"Result: {pi}");
    }
}

