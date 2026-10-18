namespace Kassyi.GSharp.Compiler;

/// <summary>Defines well-known attribute names and reflection-free inspection helpers used during source generation.</summary>
/// <seealso cref="TypeConstants"/>
// ReSharper disable once UnusedMember.Global -- Public constants for compiler internals
public static class KnownAttributes
{
    public const string GpuKernel = nameof(GpuKernel);
    public const string GpuDeviceFunction = nameof(GpuDeviceFunction);
    public const string GpuReadOnly = nameof(GpuReadOnly);
    public const string GpuInline = nameof(GpuInline);
    public const string GpuNoInline = nameof(GpuNoInline);
    public const string Constant = nameof(Constant);
    public const string CudaIntrinsic = nameof(CudaIntrinsic);
    public const string CudaIntrinsicPattern = nameof(CudaIntrinsicPattern);
    public const string MethodImpl = nameof(MethodImpl);
    public const string SkipLocalsInit = nameof(SkipLocalsInit);
    public const string InlineArray = nameof(InlineArray);
    public const string GpuOperatorMacro = nameof(GpuOperatorMacro);
    public const string GpuResource = nameof(GpuResource);
    public const string TakesOwnership = nameof(TakesOwnership);
    public const string CudaImport = nameof(CudaImport);
    public const string CudaImportMetadataName = $"{TypeConstants.CoreNamespace}.{CudaImport}Attribute";
    public const string BclIntrinsicMap = nameof(BclIntrinsicMap);
    public const string BclIntrinsicMapMetadataName = $"{TypeConstants.CoreNamespace}.{BclIntrinsicMap}Attribute";
    public const string StructLayout = nameof(StructLayout);
    public const string In = nameof(In);
    public const string Out = nameof(Out);

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
