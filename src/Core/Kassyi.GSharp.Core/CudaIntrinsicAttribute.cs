namespace Kassyi.GSharp.Core;

/// <summary>
/// Specifies the exact CUDA C++ intrinsic or macro name to emit for this method or property.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class CudaIntrinsicAttribute(string targetName) : Attribute
{
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global", Justification = "Consumed by GSharp Transpiler")]
    [SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Consumed by GSharp Transpiler")]
    public string TargetName { get; } = targetName;
}

/// <summary>
/// Specifies a pattern to format the member name (using string.Format logic) into a CUDA C++ intrinsic name.
/// Use {0} to reference the member's lowercased name.
/// Example: [CudaIntrinsicPattern("threadIdx.{0}")] on a class makes property 'X' become 'threadIdx.x'.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class CudaIntrinsicPatternAttribute(string pattern) : Attribute
{
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global", Justification = "Consumed by GSharp Transpiler")]
    [SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Consumed by GSharp Transpiler")]
    public string Pattern { get; } = pattern;
}
