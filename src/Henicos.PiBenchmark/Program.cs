namespace Henicos.PiBenchmark;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Henicos.PiBenchmark - Chudnovsky Binary Splitting (CPU Baseline)");

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
