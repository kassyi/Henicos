namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// The decisions of the driver initialization, apart from the driver calls, so that they can be tested without a GPU.
/// </summary>
internal static class CudaDriverStatus
{
    /// <summary>Classifies the driver from the results of <c>cuInit</c> and <c>cuDriverGetVersion</c>.</summary>
    /// <param name="initResult">The result of <c>cuInit(0)</c>.</param>
    /// <param name="versionResult">The result of <c>cuDriverGetVersion</c>; not read when <c>cuInit</c> failed.</param>
    /// <param name="driverVersion">The version <c>cuDriverGetVersion</c> returned.</param>
    /// <returns>
    /// <see cref="CudaDriverState.CpuFallback"/> when a call failed, <see cref="CudaDriverState.Unsupported"/> for a
    /// driver older than <see cref="CudaDriverVersion.Minimum"/>, otherwise <see cref="CudaDriverState.Usable"/>
    /// (the global context is created next).
    /// </returns>
    public static CudaDriverClassification Classify(CUresult initResult, CUresult versionResult, int driverVersion)
    {
        if (initResult != CUresult.CudaSuccess)
        {
            return new CudaDriverClassification(CudaDriverState.CpuFallback, $"cuInit failed: {initResult}");
        }

        if (versionResult != CUresult.CudaSuccess)
        {
            return new CudaDriverClassification(
                CudaDriverState.CpuFallback,
                $"cuDriverGetVersion failed: {versionResult}"
            );
        }

        return CudaDriverVersion.IsSupported(driverVersion)
            ? CudaDriverClassification.Usable
            : new CudaDriverClassification(
                CudaDriverState.Unsupported,
                CudaDriverVersion.DescribeUnsupported(driverVersion)
            );
    }

    /// <summary>Decides where work that may use the GPU runs.</summary>
    /// <param name="forceCpuFallback">Whether <see cref="GpuKernelLoader.ForceCpuFallback"/> is set.</param>
    /// <param name="driverState">Initializes the driver once and returns its state; not called when
    /// <paramref name="forceCpuFallback"/> is set, so a forced CPU run never touches the driver.</param>
    /// <returns>The place the work runs.</returns>
    public static GpuUse DecideGpuUse(bool forceCpuFallback, Func<CudaDriverState> driverState)
    {
        if (forceCpuFallback)
        {
            return GpuUse.Cpu;
        }

        return driverState() switch
        {
            CudaDriverState.Usable => GpuUse.Gpu,
            CudaDriverState.CpuFallback => GpuUse.Cpu,
            _ => GpuUse.Unsupported,
        };
    }
}
