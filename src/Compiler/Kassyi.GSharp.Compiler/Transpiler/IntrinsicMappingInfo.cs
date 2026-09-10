namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Encapsulates metadata describing target dialect intrinsic function mapping and type-casting semantics.
/// </summary>
/// <param name="CudaIntrinsicName">The identifier of the target CUDA/C++ intrinsic function.</param>
/// <param name="CastFirstArgumentTo">The target type to cast the first argument to, or <see langword="null"/> if no casting is required.</param>
/// <remarks>
/// <para><b>Performance:</b> Readonly value type ensuring zero-allocation storage in symbol lookup tables.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public readonly record struct IntrinsicMappingInfo(string CudaIntrinsicName, string? CastFirstArgumentTo)
{
    /// <summary>
    /// Gets the target intrinsic function identifier.
    /// </summary>
    public readonly string CudaIntrinsicName = CudaIntrinsicName;

    /// <summary>
    /// Gets the optional type cast applied to the first parameter before invocation.
    /// </summary>
    public readonly string? CastFirstArgumentTo = CastFirstArgumentTo;
}

