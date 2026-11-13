namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// Why the split of a statement is not planned on the IR: the statement stays on the syntax-based lowering.
/// </summary>
internal enum IrSpillFallback
{
    /// <summary>The statement is planned.</summary>
    None,

    /// <summary>A split point is inside an opaque node, whose parts the IR does not have.</summary>
    OpaqueHoldsSplit,

    /// <summary>
    /// An opaque node must be evaluated before a split, or is an argument of a call that splits (the body of
    /// <c>Gpu.SyncGrid(body)</c>, which the pre-lowering expansion writes out).
    /// </summary>
    OpaqueOperand,

    /// <summary>
    /// The statement holds an operator macro: what runs is its target, which the node does not name, so its effects
    /// and its splits are not known.
    /// </summary>
    OperatorMacro,

    /// <summary>
    /// A location that must be evaluated before a split, or that holds one, cannot be rebuilt from parts: a property or
    /// call that returns a reference, a <see langword="ref"/> conditional, a <see langword="ref"/> local or
    /// assignment, an address.
    /// </summary>
    LocationNotRebuildable,

    /// <summary>
    /// A value of a ref struct type (a span) other than a local must be evaluated before a split: no temporary can keep
    /// it across the split.
    /// </summary>
    RefLikeOperand,

    /// <summary>
    /// A field written or passed by reference has a struct receiver that is a value, not a variable: spilling the
    /// receiver would write the field of a copy.
    /// </summary>
    StructValueReceiverTarget,

    /// <summary>
    /// A method or accessor that is not <see langword="readonly"/> is called on a struct receiver that is a read-only
    /// variable (an <see langword="in"/> or <see langword="ref readonly"/> parameter or local, a
    /// <see langword="readonly"/> field, <see langword="this"/> of a <see langword="readonly"/> member), and an
    /// argument holds a split: C# calls it on a copy of the receiver taken before the split, which the IR does not
    /// represent.
    /// </summary>
    ReadOnlyVariableReceiver
}
