namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>How the <c>Launch{Kernel}Ptr</c> launcher takes one kernel parameter.</summary>
internal enum PointerArgumentKind
{
    /// <summary>A scalar, taken as declared and marshaled as the array launcher marshals it.</summary>
    Value,

    /// <summary>A user struct passed by value, taken as its native shadow struct and passed by address.</summary>
    ShadowStruct,

    /// <summary>
    /// An array-like parameter (<c>T[]</c>, <c>T[]?</c>, <c>Memory&lt;T&gt;</c>), taken as a <c>GSharpArrayProxy</c>
    /// that carries the length the kernel sees; a null or empty proxy is passed as a null or empty array, as the array
    /// launcher passes one.
    /// </summary>
    Array,

    /// <summary>
    /// A nullable array-like value (<c>Memory&lt;T&gt;?</c>, <c>ArraySegment&lt;T&gt;?</c>), taken as a
    /// <c>GSharpArrayProxy?</c>: <see langword="null"/> has no value, and a proxy has one, also a null proxy.
    /// </summary>
    NullableArray,

    /// <summary>A <c>ref</c> or <c>out</c> parameter, taken as the device address of the value.</summary>
    Reference
}
