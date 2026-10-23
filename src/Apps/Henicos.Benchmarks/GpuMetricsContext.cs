namespace Henicos.Benchmarks;

/// <summary>
/// A context to pass metrics (GPU execution time, operations count, bytes transferred)
/// from the benchmark method to the BenchmarkDotNet Diagnoser.
/// </summary>
public static class GpuMetricsContext
{
    [ThreadStatic]
    private static double t_lastGpuTimeUs;

    [ThreadStatic]
    private static double t_flops;

    [ThreadStatic]
    private static double t_bytes;

    /// <summary>
    /// Records the hardware-measured GPU kernel execution time (in microseconds).
    /// </summary>
    public static void RecordGpuTimeUs(double microseconds) => t_lastGpuTimeUs = microseconds;

    /// <summary>
    /// Records the theoretical number of floating-point operations performed by the benchmark.
    /// </summary>
    public static void RecordFlops(double operations) => t_flops = operations;

    /// <summary>
    /// Records the theoretical number of bytes read/written to global memory by the benchmark.
    /// </summary>
    public static void RecordBytes(double bytes) => t_bytes = bytes;

    public static double GetAndResetGpuTimeUs()
    {
        var val = t_lastGpuTimeUs;
        t_lastGpuTimeUs = 0;
        return val;
    }

    public static double GetAndResetFlops()
    {
        var val = t_flops;
        t_flops = 0;
        return val;
    }

    public static double GetAndResetBytes()
    {
        var val = t_bytes;
        t_bytes = 0;
        return val;
    }
}
