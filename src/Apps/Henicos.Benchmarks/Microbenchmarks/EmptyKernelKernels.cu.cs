namespace Henicos.Benchmarks.Microbenchmarks;

public static class EmptyKernelKernels
{
    [GpuKernel]
    public static void EmptyKernel()
    {
        // Do nothing
    }
}
