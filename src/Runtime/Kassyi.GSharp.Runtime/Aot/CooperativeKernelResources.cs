namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Hardware resource usage and device SM limits used to diagnose why a cooperative kernel cannot fit on an SM.
/// </summary>
/// <param name="KernelName">The kernel name for diagnostics.</param>
/// <param name="NumRegs">Registers used per thread.</param>
/// <param name="StaticSharedMemBytes">Static shared memory per block in bytes.</param>
/// <param name="DynamicSharedMemBytes">Dynamic shared memory per block in bytes.</param>
/// <param name="DeviceMajor">Device compute capability major version.</param>
/// <param name="DeviceMinor">Device compute capability minor version.</param>
/// <param name="MaxRegistersPerMultiprocessor">Maximum 32-bit registers per streaming multiprocessor.</param>
/// <param name="MaxRegistersPerBlock">Maximum 32-bit registers per block.</param>
/// <param name="MaxSharedMemPerMultiprocessor">Maximum shared memory per streaming multiprocessor in bytes.</param>
/// <param name="MaxThreadsPerBlock">Maximum threads per block.</param>
public readonly record struct CooperativeKernelResources(
    string? KernelName,
    int NumRegs,
    int StaticSharedMemBytes,
    int DynamicSharedMemBytes,
    int DeviceMajor,
    int DeviceMinor,
    int MaxRegistersPerMultiprocessor,
    int MaxRegistersPerBlock,
    int MaxSharedMemPerMultiprocessor,
    int MaxThreadsPerBlock);
