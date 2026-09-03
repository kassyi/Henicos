using System;
using System.Diagnostics;
using Kassyi.GSharp.Stdlib.Math.BigInt;

namespace Henicos.PiBenchmark;

public class FermatPrpBenchmark
{
    public static void RunFermatTest(int iterations)
    {
        int E = 71428571;
        Console.WriteLine("==================================================");
        Console.WriteLine(" G# Native Fermat PRP Test (Stdlib FermatRunner)");
        Console.WriteLine("==================================================");
        Console.WriteLine($"E       = {E}");
        Console.WriteLine($"Digits  = {(E * Math.Log10(2)):N0}");
        Console.WriteLine("==================================================");

        var runner = new FermatRunner(E);
        
        Console.WriteLine($"Starting {iterations} iterations of Fermat PRP Test via Cooperative Kernel...");
        var sw = Stopwatch.StartNew();
        
        // Dispatch using the Stdlib's runner
        // The Stdlib handles the loops internally on the GPU via Cooperative Groups.
        runner.RunBenchmarkChunk(iterations);
        
        sw.Stop();
        Console.WriteLine($"Completed {iterations} iterations in {sw.ElapsedMilliseconds} ms");
    }
}
