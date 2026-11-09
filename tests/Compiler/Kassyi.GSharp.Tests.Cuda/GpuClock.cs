using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>The clock rate of the device that the timing tests convert milliseconds into clock cycles with.</summary>
internal static class GpuClock
{
    // CU_DEVICE_ATTRIBUTE_CLOCK_RATE, in kHz
    private const int ClockRateAttribute = 13;

    /// <summary>The clock cycles of the device per millisecond at its base clock.</summary>
    public static long RateKilohertz()
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuCtxGetDevice(out var device));
        CudaException.ThrowIfError(
            CudaDriverApi.cuDeviceGetAttribute(out var rate, (CudaDriverApi.CuDeviceAttribute)ClockRateAttribute,
                device)
        );
        return rate;
    }
}
