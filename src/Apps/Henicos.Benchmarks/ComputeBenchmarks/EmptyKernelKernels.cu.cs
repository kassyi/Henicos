namespace Henicos.Benchmarks.ComputeBenchmarks;

public static partial class EmptyKernelKernels
{
    [GpuKernel]
    public static void EmptyKernel()
    {
        // Intentionally empty: measures the launch overhead only
    }
}
