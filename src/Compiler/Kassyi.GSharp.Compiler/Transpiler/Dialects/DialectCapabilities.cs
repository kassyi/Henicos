using System;

namespace Kassyi.GSharp.Compiler.Transpiler.Dialects;

/// <summary>
/// Defines the capabilities and features supported by a specific transpiler dialect.
/// Used to conditionally execute or skip tests and compiler features based on the target execution environment.
/// </summary>
[Flags]
public enum DialectCapabilities
{
    None = 0,

    /// <summary>
    /// Indicates that the dialect supports GPU Warp-level intrinsics (e.g., __syncwarp, __shfl).
    /// </summary>
    WarpIntrinsics = 1 << 0,

    /// <summary>
    /// Indicates that the dialect supports highly concurrent hardware atomic operations (e.g., atomicAdd).
    /// </summary>
    HardwareAtomics = 1 << 1,

    /// <summary>
    /// Indicates that the dialect supports explicitly managed thread-block shared memory.
    /// </summary>
    SharedMemory = 1 << 2,

    /// <summary>
    /// A convenience flag aggregating all standard GPU-accelerated execution features.
    /// </summary>
    AllGpuFeatures = WarpIntrinsics | HardwareAtomics | SharedMemory
}
