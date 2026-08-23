using System;
using System.Numerics;

namespace TestApp {
    class Program {
        static void Main() {
            int e = 1000;
            var x = BigInteger.One << e;
            var n = (BigInteger.One << (2 * e + 1)) - x + 11;
            BigInteger a = 2;
            var exponent = n - 1;

            var expected = BigInteger.ModPow(a, exponent, n);

            var runner = new Kassyi.GSharp.GpuBigInt.FermatRunner(e);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = runner.RunFermatPrp();
            sw.Stop();
            var resultArray = res.Item1;
            
            var highestWord = resultArray[resultArray.Length - 1];
            var resBytes = new byte[resultArray.Length * 4 + 1];
            Buffer.BlockCopy(resultArray, 0, resBytes, 0, resultArray.Length * 4);
            var actual = new BigInteger(resBytes);

            Console.WriteLine($"Expected: 2");
            // Console.WriteLine($"Actual  : {actual}"); // Omit huge number
            Console.WriteLine($"Match?  : {2 == actual}");
            
            double elapsedSec = sw.Elapsed.TotalSeconds;
            // e = 1000 means roughly p = 2^2000 (about 602 digits)
            // But wait, the standard M-Digit/s formula:
            // M-Digit/s = (Number of digits * iterations) / (time in seconds * 10^6)
            // Number of digits = e * log10(2) * 2 roughly
            double digits = e * 2 * Math.Log10(2);
            double mDigitsPerSec = (digits * (e * 2)) / (elapsedSec * 1000000.0);
            
            Console.WriteLine($"Elapsed : {sw.ElapsedMilliseconds} ms");
            Console.WriteLine($"Perf    : {mDigitsPerSec:F2} M-Digit/s");
        }
    }
}
