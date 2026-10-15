namespace Kassyi.GSharp.Compiler.Transpiler.Dialects;

/// <summary>
/// Specifies the hardware and language capabilities supported by a specific transpiler dialect.
/// </summary>
/// <remarks>
/// <para>
/// This enumeration is marked with <see cref="FlagsAttribute"/> to permit a bitwise combination of member values.
/// It is evaluated during compilation and test execution to dynamically select or skip features that require
/// specific hardware capabilities (such as GPU warp primitives or hardware-accelerated atomics).
/// </para>
/// <para><b>Execution Target:</b> Host-only (compiler-time analysis). Not executed within GPU kernels.</para>
/// </remarks>
[Flags]
public enum DialectCapabilities
{
    /// <summary>
    /// Indicates no specific hardware acceleration or extended language capabilities are available.
    /// Represents standard portable C++ behavior.
    /// </summary>
    None = 0,

    /// <summary>
    /// Indicates that the dialect supports GPU warp-level communication intrinsics (e.g., <c>__syncwarp</c>, <c>__shfl_sync</c>).
    /// </summary>
    WarpIntrinsics = 1 << 0,

    /// <summary>
    /// Indicates that the dialect supports hardware-accelerated memory atomic operations (e.g., <c>atomicAdd</c>, <c>atomicCAS</c>).
    /// </summary>
    HardwareAtomics = 1 << 1,

    /// <summary>
    /// Indicates that the dialect supports thread-block scoped shared memory declarations and dynamic allocation.
    /// </summary>
    SharedMemory = 1 << 2,

    /// <summary>
    /// Indicates that the dialect can embed raw target assembly or native code through <c>Gpu.InlinePtx</c>.
    /// Dialects without this capability emit a runtime failure at the call site and report a warning.
    /// </summary>
    InlineAssembly = 1 << 3,

    /// <summary>
    /// Aggregates all standard GPU hardware acceleration features (warp intrinsics, atomics, shared memory, and inline assembly).
    /// </summary>
    AllGpuFeatures = WarpIntrinsics | HardwareAtomics | SharedMemory | InlineAssembly
}

