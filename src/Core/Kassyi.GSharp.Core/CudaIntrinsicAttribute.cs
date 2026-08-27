namespace Kassyi.GSharp.Core;

/// <summary>
/// Specifies the exact CUDA C++ intrinsic or instruction name to emit for an annotated member.
/// </summary>
/// <remarks>
/// <para>Used by the GSharp Transpiler to substitute C# method, property, or field calls directly with corresponding GPU primitives (e.g., <c>atomicAdd</c>, <c>__syncthreads</c>).</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class CudaIntrinsicAttribute(string targetName) : Attribute
{
    /// <summary>
    /// Gets the target CUDA C++ intrinsic or function name.
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global", Justification = "Consumed by GSharp Transpiler")]
    [SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Consumed by GSharp Transpiler")]
    public string TargetName { get; } = targetName;
}

/// <summary>
/// Specifies a pattern format string used to map members of a class or struct into CUDA C++ intrinsic names.
/// </summary>
/// <remarks>
/// <para>Use <c>{0}</c> to reference the lowercase name of the invoked member.</para>
/// <para><b>Example:</b> <c>[CudaIntrinsicPattern("threadIdx.{0}")]</c> on a type translates property <c>X</c> into <c>threadIdx.x</c>.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class CudaIntrinsicPatternAttribute(string pattern) : Attribute
{
    /// <summary>
    /// Gets the composite format pattern string.
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global", Justification = "Consumed by GSharp Transpiler")]
    [SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Consumed by GSharp Transpiler")]
    public string Pattern { get; } = pattern;
}
