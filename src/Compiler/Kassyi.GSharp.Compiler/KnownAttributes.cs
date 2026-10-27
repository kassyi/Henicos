namespace Kassyi.GSharp.Compiler;

/// <summary>Defines well-known attribute names and reflection-free inspection helpers used during source generation.</summary>
/// <seealso cref="TypeConstants"/>
// ReSharper disable once UnusedMember.Global -- Constants kept together for compiler internals
internal static class KnownAttributes
{
    private const string AttributeSuffix = "Attribute";

    public const string GpuKernel = nameof(GpuKernel);
    public const string GpuKernelMetadataName = $"{TypeConstants.CoreNamespace}.{GpuKernel}Attribute";
    public const string GpuDeviceFunction = nameof(GpuDeviceFunction);
    public const string GpuDeviceFunctionMetadataName = $"{TypeConstants.CoreNamespace}.{GpuDeviceFunction}Attribute";
    public const string GpuReadOnly = nameof(GpuReadOnly);
    public const string GpuInline = nameof(GpuInline);
    public const string GpuNoInline = nameof(GpuNoInline);
    public const string GpuConstexpr = nameof(GpuConstexpr);
    public const string GpuConstexprMetadataName = $"{TypeConstants.CoreNamespace}.{GpuConstexpr}Attribute";
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
    public const string FieldOffset = nameof(FieldOffset);
    public const string In = nameof(In);
    public const string Out = nameof(Out);
    public const string DefaultParameterValue = nameof(DefaultParameterValue);

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

        return span.Length == target.Length + AttributeSuffix.Length &&
               span.StartsWith(target) &&
               span.EndsWith(AttributeSuffix.AsSpan());
    }

    /// <summary>
    /// Whether <paramref name="declaringNamespace"/> is the namespace that declares the attribute known as <paramref name="shortName"/>,
    /// so that an attribute that only shares the simple name (a user's own <c>GpuKernelAttribute</c>) is not taken for the real one.
    /// A name that is not a known attribute has no owner and always matches.
    /// </summary>
    public static bool IsDeclaredInOwnerNamespace(INamespaceSymbol? declaringNamespace, string shortName) => shortName switch
    {
        GpuKernel or GpuDeviceFunction or GpuReadOnly or GpuInline or GpuNoInline or GpuConstexpr or CudaIntrinsic
            or CudaIntrinsicPattern or GpuOperatorMacro or GpuResource or TakesOwnership or CudaImport or BclIntrinsicMap =>
            declaringNamespace.IsCoreNamespace(),
        MethodImpl or SkipLocalsInit or InlineArray => declaringNamespace.IsSystemRuntimeCompilerServices(),
        StructLayout or FieldOffset or In or Out or DefaultParameterValue => declaringNamespace.IsSystemRuntimeInteropServices(),
        _ => true
    };
}
