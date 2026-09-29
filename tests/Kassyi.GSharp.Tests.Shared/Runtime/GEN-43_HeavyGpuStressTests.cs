using System;
using System.Threading.Tasks;
using Xunit;

namespace Kassyi.GSharp.Tests.Runtime;

[Trait("Category", "GpuExecution")]
[Trait("Category", "Stress")]
public class GEN_43_HeavyGpuStressTests
{
    [SkippableFact]
    [Trait("FeatureId", "GEN-43")]
    public void Test_GEN43_GpuKernelLoader_HandlesMultipleRepeatedLaunches()
    {
        // Use a small matrix size to avoid excessive execution time, 
        // but launch it many times to stress the JIT engine and memory management.
        int n = 16; 
        int elements = n * n;
        
        var a = new float[elements];
        var b = new float[elements];
        var c = new float[elements];

        for (int i = 0; i < elements; i++)
        {
            a[i] = 1.0f;
            b[i] = 2.0f;
        }

        int iterations = 1000;

        for (int iter = 0; iter < iterations; iter++)
        {
            GEN_43_HeavyGpuStressKernels.LaunchMatrixMultiplyStressKernel(
                a, b, c, n,
                gridDimX: 1, gridDimY: 1,
                blockDimX: (uint)n, blockDimY: (uint)n
            );
        }

        // Each element of C should be 1.0 * 2.0 * n = 2.0 * 16 = 32.0
        for (int i = 0; i < elements; i++)
        {
            Assert.Equal(32.0f, c[i]);
        }
    }

    [SkippableFact]
    [Trait("FeatureId", "GEN-43")]
    [Trait("Category", "Concurrency")]
    public void Test_GEN43_GpuKernelLoader_HandlesConcurrentLaunches()
    {
        // Concurrently launch kernels across multiple worker threads to stress 
        // GpuKernelLoader thread safety and CudaContextScope per-thread binding.
        int n = 16;
        int elements = n * n;
        int workerCount = 8;
        int iterationsPerWorker = 20;

        Parallel.For(0, workerCount, _ =>
        {
            var a = new float[elements];
            var b = new float[elements];
            var c = new float[elements];

            for (int i = 0; i < elements; i++)
            {
                a[i] = 1.0f;
                b[i] = 2.0f;
            }

            for (int iter = 0; iter < iterationsPerWorker; iter++)
            {
                GEN_43_HeavyGpuStressKernels.LaunchMatrixMultiplyStressKernel(
                    a, b, c, n,
                    gridDimX: 1, gridDimY: 1,
                    blockDimX: (uint)n, blockDimY: (uint)n
                );
            }

            for (int i = 0; i < elements; i++)
            {
                Assert.Equal(32.0f, c[i]);
            }
        });
    }
}
