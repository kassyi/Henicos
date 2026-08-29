namespace Kassyi.GSharp.Compiler;

/// <summary>Defines well-known attribute names and reflection-free inspection helpers used during source generation.</summary>
/// <seealso cref="TypeConstants"/>
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public constants for compiler internals")]
public static class KnownAttributes
{
    public const string GpuKernel = "GpuKernel";
    public const string GpuDeviceFunction = "GpuDeviceFunction";
    public const string GpuConst = "GpuConst";
    public const string GpuInline = "GpuInline";
    public const string GpuNoInline = "GpuNoInline";
    public const string JitConstant = "JitConstant";
    public const string CudaIntrinsic = "CudaIntrinsic";
    public const string CudaIntrinsicPattern = "CudaIntrinsicPattern";
    public const string MethodImpl = "MethodImpl";

    /// <summary>
    /// Zero-allocation, reflection-free helper to match an attribute name against a short name or its Attribute-suffixed name.
    /// </summary>
    public static bool IsAttributeName(string? rawName, string shortName)
    {
        if (rawName is null)
        {
            return false;
        }

        var span = rawName.AsSpan();
        var target = shortName.AsSpan();

        if (span.SequenceEqual(target))
        {
            return true;
        }

        return span.Length == target.Length + 9 &&
               span.StartsWith(target) &&
               span.EndsWith("Attribute".AsSpan());
    }
}
