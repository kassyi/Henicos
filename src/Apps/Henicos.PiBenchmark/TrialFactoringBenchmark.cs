using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime.Jit;
using Kassyi.GSharp.Stdlib.Math.BigInt.Providers;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

namespace Henicos.PiBenchmark;

public class TrialFactoringBenchmark
{
    public static void RunTrialFactoring(int E, uint maxP)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine(" G# Native Fermat Trial Factoring (GPU)");
        Console.WriteLine("==================================================");
        Console.WriteLine($"E       = {E}");
        Console.WriteLine($"Max P   = {maxP:N0}");
        Console.WriteLine("==================================================");

        using var scope = new JitEngine.CudaContextScope();

        using var gpuContext = new GpuContext(1024);

        int maxResults = 1024;
        using var resultsBuffer = new ScopedGpuMemory(gpuContext.Provider, maxResults * sizeof(uint));
        using var resultCountBuffer = new ScopedGpuMemory(gpuContext.Provider, sizeof(uint));

        var zeroCount = new uint[1] { 0 };
        gpuContext.Provider.CopyHostToDevice(resultCountBuffer.Pointer, zeroCount, 1);

        uint batchSize = 100_000_000;
        uint blockSize = 256;

        Console.WriteLine($"Testing odd numbers from 3 up to {maxP:N0} in batches of {batchSize:N0}...");

        var sw = Stopwatch.StartNew();

        for (uint startOdd = 3; startOdd < maxP; startOdd += batchSize * 2)
        {
            uint currentBatchSize = Math.Min(batchSize, (maxP - startOdd) / 2 + 1);
            uint gridX = (currentBatchSize + blockSize - 1) / blockSize;

            FermatTrialFactoringKernelsWrapper.Launch_Kernel_SearchFactors32_Device(
                startOdd,
                (uint)E,
                resultsBuffer.Pointer,
                resultCountBuffer.Pointer,
                maxResults,
                gridDimX: gridX,
                blockDimX: blockSize
            );

            JitEngine.SynchronizeDevice();

            gpuContext.Provider.CopyDeviceToHost(zeroCount, resultCountBuffer.Pointer, 1);
            uint count = zeroCount[0];
            if (count > 0)
            {
                Console.WriteLine($"\n[FOUND] GPU reported {count} potential matches in range!");
                var resultsHost = new uint[count];
                gpuContext.Provider.CopyDeviceToHost(resultsHost, resultsBuffer.Pointer, (int)count);

                foreach (var k in resultsHost)
                {
                    if (IsPrimeCPU(k))
                    {
                        Console.WriteLine($"=> CONFIRMED PRIME FACTOR FOUND: {k}");
                        Console.WriteLine($"=> N is COMPOSITE!");
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"=> (False positive: {k} is composite, ignoring)");
                    }
                }

                // Reset count
                zeroCount[0] = 0;
                gpuContext.Provider.CopyHostToDevice(resultCountBuffer.Pointer, zeroCount, 1);
            }

            if (startOdd % (batchSize * 10) == 3 && startOdd > 3)
            {
                Console.WriteLine($"  ... reached {startOdd:N0}");
            }
        }

        sw.Stop();
        Console.WriteLine($"\nTrial factoring up to {maxP:N0} completed in {sw.ElapsedMilliseconds} ms.");
        Console.WriteLine("No factors found. N passed trial division screening.");
    }

    private static bool IsPrimeCPU(uint n)
    {
        if (n <= 1)
        {
            return false;
        }

        if (n == 2 || n == 3)
        {
            return true;
        }

        if (n % 2 == 0)
        {
            return false;
        }

        for (uint i = 3; i * i <= n; i += 2)
        {
            if (n % i == 0)
            {
                return false;
            }
        }
        return true;
    }
}
