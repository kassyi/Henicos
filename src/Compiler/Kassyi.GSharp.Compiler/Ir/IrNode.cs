namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>
/// A node of the compiler's intermediate representation of a method body, built from Roslyn's <see cref="IOperation"/>
/// so that the order of evaluation, the value categories and the split points are decided once, on its nodes.
/// </summary>
/// <remarks>
/// Nodes are immutable and compared by reference: two reads of <c>x</c> are two nodes. The tree holds symbols and
/// syntax, so it lives within one generator transform or analyzer callback; it is never part of an incremental
/// pipeline model and never kept in a static field. The spelling fields (<see cref="IrExpression.Parentheses"/>,
/// <see cref="IrStatement.CommentsBefore"/>) are written only into a copy just made (<c>With…</c>, or the new node
/// of an <c>Update</c>), never into a node that exists.
/// </remarks>
/// <param name="source">Where the node comes from.</param>
internal abstract class IrNode(IrSource source)
{
    /// <summary>Where the node comes from in the user's source.</summary>
    public IrSource Source { get; } = source;
}
