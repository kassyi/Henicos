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
        { IsArray: true, IsOptional: true } => PointerArgumentKind.OptionalArray,
        { IsArray: true } => PointerArgumentKind.Array,
        { IsByRef: true } => PointerArgumentKind.Reference,
        { IsCustomStruct: true } => PointerArgumentKind.ShadowStruct,
        _ => PointerArgumentKind.Value
    };

    /// <summary>Whether the pointer launcher takes <paramref name="kind"/> as a <c>GSharpArrayProxy</c>.</summary>
    public static bool IsProxy(PointerArgumentKind kind) =>
        kind is PointerArgumentKind.Array or PointerArgumentKind.OptionalArray;
}
