namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>What the runtime found when it initialized the CUDA driver.</summary>
internal enum CudaDriverState
{
    /// <summary>The driver is supported and the global context exists.</summary>
    Usable,

    /// <summary>No driver library, no device, or a driver call failed: kernels run on the CPU.</summary>
    CpuFallback,

    /// <summary>The driver is older than <see cref="CudaDriverVersion.Minimum"/>: GPU use throws.</summary>
    Unsupported,
}
