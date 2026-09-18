namespace Henicos.Benchmarks.Infrastructure;

/// <summary>
/// A context to pass metrics (GPU execution time, operations count, bytes transferred)
/// from the benchmark method to the BenchmarkDotNet Diagnoser.
/// </summary>
public static class GpuMetricsContext
{
    private static readonly ThreadLocal<double> s_lastGpuTimeUs = new();
    private static readonly ThreadLocal<double> s_flops = new();
    private static readonly ThreadLocal<double> s_bytes = new();

    /// <summary>
    /// Records the hardware-measured GPU kernel execution time (in microseconds).
    /// </summary>
    public static void RecordGpuTimeUs(double microseconds) => s_lastGpuTimeUs.Value = microseconds;

    /// <summary>
    /// Records the theoretical number of floating-point operations performed by the benchmark.
    /// </summary>
    public static void RecordFlops(double operations) => s_flops.Value = operations;

    /// <summary>
    /// Records the theoretical number of bytes read/written to global memory by the benchmark.
    /// </summary>
    public static void RecordBytes(double bytes) => s_bytes.Value = bytes;

    public static double GetAndResetGpuTimeUs()
    {
        var val = s_lastGpuTimeUs.Value;
        s_lastGpuTimeUs.Value = 0;
        return val;
    }

    public static double GetAndResetFlops()
    {
        var val = s_flops.Value;
        s_flops.Value = 0;
        return val;
    }

    public static double GetAndResetBytes()
    {
        var val = s_bytes.Value;
        s_bytes.Value = 0;
        return val;
    }
}
