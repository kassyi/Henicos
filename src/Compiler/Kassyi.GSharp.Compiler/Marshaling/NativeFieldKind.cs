namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>How a struct member is stored in the native shadow struct and copied by its marshal methods.</summary>
internal enum NativeFieldKind
{
    /// <summary>
    /// A <c>fixed</c> buffer or the element block of an <c>[InlineArray]</c> struct: copied as bytes.
    /// </summary>
    FixedBuffer,

    /// <summary>
    /// An array or another array-like type: a <c>GSharpArrayProxy</c>, filled by the array copy helpers.
    /// </summary>
    ArrayProxy,

    /// <summary>
    /// A user struct or a <c>Nullable&lt;T&gt;</c>: its own shadow struct, marshaled by its own methods.
    /// </summary>
    UserStruct,

    /// <summary>A primitive or an enum: the shadow field has the same type, copied by assignment.</summary>
    DirectValue,

    /// <summary>
    /// Any other value type (a core vector type, an explicit-layout struct): a byte buffer written through a pointer.
    /// </summary>
    FixedBytesFallback,
}
