namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>Why the builder kept a construct as an opaque leaf instead of building nodes for it.</summary>
/// <remarks>
/// The builder checks the reasons in the order they are declared and records the first that applies.
/// </remarks>
internal enum IrOpaqueReason
{
    /// <summary>The operation does not bind: an invalid operation, or one of an error type.</summary>
    Invalid,

    /// <summary>
    /// The kind of operation has no node yet (<see cref="OperationKind.None"/> included, which Roslyn 4.8 gives
    /// <c>stackalloc</c>, collection expressions and pointer element access).
    /// </summary>
    UnsupportedOperation,

    /// <summary>The kind of operation has a node, but this operation has no type.</summary>
    Typeless,

    /// <summary>
    /// The kind of operation has a node, but not this shape of it (a lifted operator, a multidimensional array, a
    /// nested
    /// member initializer, a delegate invocation, ...).
    /// </summary>
    UnsupportedShape,

    /// <summary>Roslyn gives no operation for the syntax.</summary>
    NoOperation
}
