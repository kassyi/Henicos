using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Jit;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;
using Kassyi.GSharp.Stdlib.Math.BigInt.Providers;

namespace Henicos.PiBenchmark;

public class TrialFactoringBenchmark
{
    public static void RunTrialFactoring(int E, uint maxP)
    {
        Logger<TrialFactoringBenchmark>.Info("==================================================");
        Logger<TrialFactoringBenchmark>.Info(" G# Native Fermat Trial Factoring (GPU)");
        Logger<TrialFactoringBenchmark>.Info("==================================================");
        Logger<TrialFactoringBenchmark>.Info($"E       = {E}");
        Logger<TrialFactoringBenchmark>.Info($"Max P   = {maxP:N0}");
        Logger<TrialFactoringBenchmark>.Info("==================================================");

        using var scope = new JitEngine.CudaContextScope();

        using var gpuContext = new GpuContext(1024);

        var maxResults = 1024;
        using var resultsBuffer = new ScopedGpuMemory(gpuContext.Provider, maxResults * sizeof(uint));
        using var resultCountBuffer = new ScopedGpuMemory(gpuContext.Provider, sizeof(uint));

        var zeroCount = new uint[1] { 0 };
        gpuContext.Provider.CopyHostToDevice(resultCountBuffer.Pointer, zeroCount, 1);

        uint batchSize = 100_000_000;
        uint blockSize = 256;

        Logger<TrialFactoringBenchmark>.Info($"Testing odd numbers from 3 up to {maxP:N0} in batches of {batchSize:N0}...");

        var sw = Stopwatch.StartNew();

        for (uint startOdd = 3; startOdd < maxP; startOdd += batchSize * 2)
        {
            var currentBatchSize = Math.Min(batchSize, (maxP - startOdd) / 2 + 1);
            var gridX = (currentBatchSize + blockSize - 1) / blockSize;

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
            var count = zeroCount[0];
            if (count > 0)
            {
                Logger<TrialFactoringBenchmark>.Info($"[FOUND] GPU reported {count} potential matches in range!");
                var resultsHost = new uint[count];
                gpuContext.Provider.CopyDeviceToHost(resultsHost, resultsBuffer.Pointer, (int)count);

                foreach (var k in resultsHost)
                {
                    if (IsPrimeCPU(k))
                    {
                        Logger<TrialFactoringBenchmark>.Info($"=> CONFIRMED PRIME FACTOR FOUND: {k}");
                        Logger<TrialFactoringBenchmark>.Info("=> N is COMPOSITE!");
                        return;
                    }
                    else
                    {
                        Logger<TrialFactoringBenchmark>.Info($"=> (False positive: {k} is composite, ignoring)");
                    }
                }

                // Reset count
                zeroCount[0] = 0;
                gpuContext.Provider.CopyHostToDevice(resultCountBuffer.Pointer, zeroCount, 1);
            }

            if (startOdd % (batchSize * 10) == 3 && startOdd > 3)
            {
                Logger<TrialFactoringBenchmark>.Info($"  ... reached {startOdd:N0}");
            }
        }

        sw.Stop();
        Logger<TrialFactoringBenchmark>.Info($"Trial factoring up to {maxP:N0} completed in {sw.ElapsedMilliseconds} ms.");
        Logger<TrialFactoringBenchmark>.Info("No factors found. N passed trial division screening.");
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
