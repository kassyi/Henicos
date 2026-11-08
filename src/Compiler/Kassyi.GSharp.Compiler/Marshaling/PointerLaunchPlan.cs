namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>How the <c>Launch{Kernel}Ptr</c> launcher takes one kernel parameter.</summary>
internal enum PointerArgumentKind
{
    /// <summary>A scalar, taken as declared and marshaled as the array launcher marshals it.</summary>
    Value,

    /// <summary>A user struct passed by value, taken as its native shadow struct and passed by address.</summary>
    ShadowStruct,

    /// <summary>
    /// A non-optional array-like parameter, taken as a <c>GSharpArrayProxy</c> that carries the length the kernel sees;
    /// a null, empty or negative-length proxy is rejected as the array launcher rejects a null or empty array.
    /// </summary>
    Array,

    /// <summary>
    /// An optional array-like parameter (<c>T[]?</c>, <c>Memory&lt;T&gt;?</c>), taken as a <c>GSharpArrayProxy</c>; a null
    /// or empty proxy is passed as a null array, as the array launcher passes an empty optional array.
    /// </summary>
    OptionalArray,

    /// <summary>A <c>ref</c> or <c>out</c> parameter, taken as the device address of the value.</summary>
    Reference
}

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
