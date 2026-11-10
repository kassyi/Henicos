namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>
/// How the node that holds an expression uses it: as a value, or as a location that it writes or passes by reference.
/// The user of an expression decides it, as Roslyn keeps the ref kind on the use (an argument, an assignment).
/// </summary>
/// <remarks>
/// A span is not a category: that it cannot be stored in a frame is a property of its type
/// (<see cref="ITypeSymbol.IsRefLikeType"/>), and its value is copied like any other value.
/// </remarks>
internal enum IrCategory
{
    /// <summary>
    /// The value is read. Also the forms where C# reads a value into a hidden copy: a non-variable passed to an
    /// <see langword="in"/> parameter, a non-variable struct receiver.
    /// </summary>
    Value,

    /// <summary>
    /// A location that is written or passed by reference: an assignment target, the target of a compound assignment or
    /// an increment, a <see langword="ref"/> or <see langword="out"/> argument, a <see langword="ref"/> local
    /// initializer,
    /// a returned reference, a struct receiver whose member may change it, the operand of <c>&amp;</c>.
    /// </summary>
    Location,

    /// <summary>
    /// A location that is passed by reference and not written: a variable passed to an <see langword="in"/> or
    /// <see langword="ref readonly"/> parameter, the receiver of a <see langword="readonly"/> member.
    /// </summary>
    ReadOnlyLocation,

    /// <summary>A value of a pointer type (the result of <c>&amp;x</c>, a pointer variable that is read).</summary>
    Pointer
}
