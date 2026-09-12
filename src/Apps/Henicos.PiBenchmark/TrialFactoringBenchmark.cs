using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt.Providers;
using Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

namespace Henicos.PiBenchmark;

public class TrialFactoringBenchmark
{
    public static void RunTrialFactoring(int e, uint maxP)
    {
        Logger<TrialFactoringBenchmark>.Info("==================================================");
        Logger<TrialFactoringBenchmark>.Info(" G# Native Fermat Trial Factoring (GPU)");
        Logger<TrialFactoringBenchmark>.Info("==================================================");
        Logger<TrialFactoringBenchmark>.Info($"E       = {e}");
        Logger<TrialFactoringBenchmark>.Info($"Max P   = {maxP:N0}");
        Logger<TrialFactoringBenchmark>.Info("==================================================");

        using var scope = new GpuKernelLoader.CudaContextScope();

        using var gpuContext = new GpuContext(1024);

        var maxResults = 1024;
        using var resultsBuffer = new ScopedGpuMemory(gpuContext.Provider, maxResults * sizeof(uint));
        using var resultCountBuffer = new ScopedGpuMemory(gpuContext.Provider, sizeof(uint));

        var zeroCount = new uint[] { 0 };
        gpuContext.Provider.CopyHostToDevice(resultCountBuffer.Pointer, zeroCount, 1);

        const uint BatchSize = 100_000_000;
        const uint BlockSize = 256;

        Logger<TrialFactoringBenchmark>.Info(
            $"Testing odd numbers from 3 up to {maxP:N0} in batches of {BatchSize:N0}...");

        var sw = Stopwatch.StartNew();

        for (uint startOdd = 3; startOdd < maxP; startOdd += BatchSize * 2)
        {
            var currentBatchSize = Math.Min(BatchSize, (maxP - startOdd) / 2 + 1);
            var gridX = (currentBatchSize + BlockSize - 1) / BlockSize;

            FermatTrialFactoringKernelsWrapper.Launch_Kernel_SearchFactors32_Device(
                startOdd,
                (uint)e,
                resultsBuffer.Pointer,
                resultCountBuffer.Pointer,
                maxResults,
                gridDimX: gridX,
                blockDimX: BlockSize
            );

            GpuKernelLoader.SynchronizeDevice();

            gpuContext.Provider.CopyDeviceToHost(zeroCount, resultCountBuffer.Pointer, 1);
            var count = zeroCount[0];
            if (count > 0)
            {
                Logger<TrialFactoringBenchmark>.Info($"[FOUND] GPU reported {count} potential matches in range!");
                var resultsHost = new uint[count];
                gpuContext.Provider.CopyDeviceToHost(resultsHost, resultsBuffer.Pointer, (int)count);

                foreach (var k in resultsHost)
                {
                    if (IsPrimeCpu(k))
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

            if (startOdd % (BatchSize * 10) == 3 && startOdd > 3)
            {
                Logger<TrialFactoringBenchmark>.Info($"  ... reached {startOdd:N0}");
            }
        }

        sw.Stop();
        Logger<TrialFactoringBenchmark>.Info(
            $"Trial factoring up to {maxP:N0} completed in {sw.ElapsedMilliseconds} ms.");
        Logger<TrialFactoringBenchmark>.Info("No factors found. N passed trial division screening.");
    }

    private static bool IsPrimeCpu(uint n)
    {
        switch (n)
        {
            case <= 1:
                return false;
            case 2:
            case 3:
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
