namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The intrinsics that <see cref="DeviceFunctionEffects"/> classifies by name: those that write memory, and the pure
/// math and views.
/// </summary>
internal static class DeviceIntrinsicEffects
{
    // The atomics, asynchronous copies, inline PTX, barriers, fences and warp operations
#pragma warning disable HNT0003 // The warp type is checked with its namespace
    internal static bool IsMemoryIntrinsic(IMethodSymbol method) =>
        method.Name.StartsWith("Atomic", StringComparison.Ordinal) ||
        method.Name.StartsWith(TypeConstants.GpuMacros.CopyAsync, StringComparison.Ordinal) ||
        method.Name.StartsWith(TypeConstants.GpuMacros.InlinePtx, StringComparison.Ordinal) ||
        method.Name.StartsWith("Sync", StringComparison.Ordinal) ||
        method.Name.StartsWith("Fence", StringComparison.Ordinal) ||
        (method.ContainingType is { Name: TypeConstants.WarpType } warp && warp.ContainingNamespace.IsCoreNamespace());
#pragma warning restore HNT0003

    // The math of System (Math, MathF, the methods of the primitive types), System.Numerics, FastMath, Gpu.NotNull
    // (which returns its argument or throws), and the constructors of value types that have no body (the vector types,
    // Nullable<T>, spans)
#pragma warning disable HNT0003 // The types are checked with their namespaces
    internal static bool IsPureIntrinsic(IMethodSymbol method)
    {
        var type = method.ContainingType;
        if (type == null)
        {
            return false;
        }

        if (method.MethodKind == MethodKind.Constructor && type.IsValueType)
        {
            return true;
        }

        if (method is { MethodKind: MethodKind.PropertyGet, AssociatedSymbol: IPropertySymbol property } &&
            SpanWriteClassifier.ClassifyProperty(property) is not (SpanPropertyKind.None or SpanPropertyKind.Unsupported))
        {
            return true;
        }

        var ns = type.ContainingNamespace;
        return (type.SpecialType != SpecialType.None && type.SpecialType != SpecialType.System_Array) ||
               SpanWriteClassifier.IsPureViewMethod(method) ||
               GpuMacroClassifier.Classify(method).Kind == GpuMacroKind.NotNull ||
               (ns.IsSystemNamespace() && type.Name is TypeConstants.MathType or TypeConstants.MathFType) ||
               (ns.IsSystemOrNumerics() && !ns.IsSystemNamespace()) ||
               (ns.IsCoreNamespace() && type.Name == FastMathType);
    }
#pragma warning restore HNT0003

    private const string FastMathType = "FastMath";
}
