namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Decides how the <c>Launch{Kernel}Ptr</c> launcher takes each kernel parameter.</summary>
/// <remarks>
/// The signature, the argument checks, the marshaling and the typed <c>GpuBuffer&lt;T&gt;</c> overload of the launcher
/// all write from this decision, so they cannot disagree on which parameters carry a length.
/// </remarks>
internal static class PointerLaunchPlan
{
    /// <summary>Classifies <paramref name="shape"/> for the pointer launcher.</summary>
    public static PointerArgumentKind Classify(in KernelParameterShape shape) => shape switch
    {
        { IsArray: true, IsNullableValue: true } => PointerArgumentKind.NullableArray,
        { IsArray: true } => PointerArgumentKind.Array,
        { IsByRef: true } => PointerArgumentKind.Reference,
        // An int? keeps its type: only a Nullable<T> whose CUDA layout differs is taken as its shadow struct
        { IsCustomStruct: true, IsNullableStruct: false } or { IsCustomStruct: true, Cuda.IsIdenticalStruct: false } =>
            PointerArgumentKind.ShadowStruct,
        _ => PointerArgumentKind.Value
    };

    /// <summary>Whether the pointer launcher takes <paramref name="kind"/> as a <c>GSharpArrayProxy</c>.</summary>
    public static bool IsProxy(PointerArgumentKind kind) =>
        kind is PointerArgumentKind.Array or PointerArgumentKind.NullableArray;

    /// <summary>
    /// The proxy that the pointer launcher reads for <paramref name="shape"/>: the parameter, or its value (a
    /// <c>GSharpArrayProxy?</c> without a value reads as a null proxy).
    /// </summary>
    public static string GetProxyExpression(in KernelParameterShape shape) =>
        Classify(shape) == PointerArgumentKind.NullableArray
            ? $"{shape.Identifier}.GetValueOrDefault()"
            : shape.Identifier;
}
