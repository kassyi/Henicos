namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>
/// A node of the compiler's intermediate representation of a method body, built from Roslyn's <see cref="IOperation"/>
/// so that the order of evaluation, the value categories and the split points are decided once, on its nodes.
/// </summary>
/// <remarks>
/// Nodes are immutable and compared by reference: two reads of <c>x</c> are two nodes. The tree holds symbols and
/// syntax, so it lives within one generator transform or analyzer callback; it is never part of an incremental
/// pipeline model and never kept in a static field.
/// </remarks>
/// <param name="source">Where the node comes from.</param>
internal abstract class IrNode(IrSource source)
{
    /// <summary>Where the node comes from in the user's source.</summary>
    public IrSource Source { get; } = source;
}
