namespace Kassyi.GSharp.Stdlib.GpuMath;

/// <summary>
/// Specifies the GPU kernel execution engine backend.
/// </summary>
public enum GpuExecutionEngine
{
    /// <summary>Executes pre-compiled GSharp device kernels via Ahead-Of-Time (AOT) loaded modules.</summary>
    GSharpAot
}
