namespace Kassyi.GSharp.Compiler.Ir.Classifying;

/// <summary>
/// Whether the trap rules of <see cref="IrTraps"/> and <see cref="IrEffects"/> read the decisions of the nodes.
/// </summary>
internal enum IrTrapReading
{
    /// <summary>
    /// The rules of the node kinds alone, for a tree that has not been decided (the split plans): the length of an
    /// array may throw.
    /// </summary>
    Undecided,

    /// <summary>
    /// The rules that also read the decisions of a decided tree (the sequence pass): the length of an array whose
    /// receiver is proven non-null does not throw. A node without its decision is an internal error.
    /// </summary>
    Decided
}
