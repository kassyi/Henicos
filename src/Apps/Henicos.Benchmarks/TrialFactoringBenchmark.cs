using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.Math.GpuBigInt.Kernels;

namespace Henicos.Benchmarks;

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

        var maxResults = 1024;
        using var resultsBuffer = new GpuBuffer<uint>(maxResults);
        using var resultCountBuffer = new GpuBuffer<uint>(1);

        var zeroCount = new uint[] { 0 };
        resultCountBuffer.CopyToDevice(zeroCount);

        const uint BatchSize = 100_000_000;
        const uint BlockSize = 256;

        Logger<TrialFactoringBenchmark>.Info(
            $"Testing odd numbers from 3 up to {maxP:N0} in batches of {BatchSize:N0}...");

        var sw = Stopwatch.StartNew();

        for (uint startOdd = 3; startOdd < maxP; startOdd += BatchSize * 2)
        {
            var currentBatchSize = Math.Min(BatchSize, (maxP - startOdd) / 2 + 1);
            var gridX = (currentBatchSize + BlockSize - 1) / BlockSize;

            FermatTrialFactoringKernels.LaunchSearchFactors32KernelPtr(
                startOdd,
                (uint)e,
                resultsBuffer.DevicePointer,
                resultCountBuffer.DevicePointer,
                maxResults,
                gridDimX: gridX,
                blockDimX: BlockSize
            );

            GpuKernelLoader.SynchronizeDevice();

            resultCountBuffer.CopyToHost(zeroCount);
            var count = zeroCount[0];
            if (count > 0)
            {
                Logger<TrialFactoringBenchmark>.Info($"[FOUND] GPU reported {count} potential matches in range!");
                var resultsHost = new uint[count];
                resultsBuffer.CopyToHost(resultsHost);

                foreach (var k in resultsHost)
                {
                    if (IsPrimeCpu(k))
                    {
                        Logger<TrialFactoringBenchmark>.Info($"=> CONFIRMED PRIME FACTOR FOUND: {k}");
                        Logger<TrialFactoringBenchmark>.Info("=> N is COMPOSITE!");
                        return;
                    }

                    Logger<TrialFactoringBenchmark>.Info($"=> (False positive: {k} is composite, ignoring)");
                }

                // Reset count
                zeroCount[0] = 0;
                resultCountBuffer.CopyToDevice(zeroCount);
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
