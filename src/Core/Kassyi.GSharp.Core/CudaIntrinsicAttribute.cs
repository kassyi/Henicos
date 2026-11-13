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
    // ReSharper disable once UnusedAutoPropertyAccessor.Global -- Consumed by GSharp Transpiler
    // ReSharper disable once UnusedMember.Global -- Consumed by GSharp Transpiler
    public string TargetName { get; } = targetName;

    /// <summary>
    /// Gets or sets the dialects that do not implement the intrinsic, as <c>GSharpTargetDialect</c> names them
    /// (<c>Cpu</c>, <c>Cuda</c>). A call in a project of such a dialect is reported as a warning (GSH0054) and becomes
    /// a runtime failure when it is reached.
    /// </summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global -- Consumed by GSharp Transpiler
    public string[]? UnsupportedDialects { get; set; }
}
