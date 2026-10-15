namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Single entry point for skipping tests that need a CUDA device.
/// </summary>
public static class CudaCapability
{
    /// <summary>
    /// Skips the current test when the CUDA driver cannot be initialized or no device is available.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.IsDriverAvailable"/>, which runs the same
    /// initialization the runtime performs and reports <see langword="false"/> for a missing driver, a faulty driver,
    /// or the CPU fallback mode. A bare <c>cuInit</c> check would not cover a host that has the driver but no device.
    /// </remarks>
    public static void EnsureGpuAvailable() =>
        Skip.IfNot(Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.IsDriverAvailable,
            "NVIDIA GPU and CUDA Driver are required to run this test.");
}
