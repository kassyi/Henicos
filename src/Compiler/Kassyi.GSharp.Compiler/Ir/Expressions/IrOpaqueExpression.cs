namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>
/// An expression the builder does not represent with nodes yet; a leaf whose syntax goes to the syntax-based rules.
/// </summary>
/// <remarks>
/// Nothing inside it is built, so no decision about its parts is made on the IR. The operation is kept so that a later
/// pass can ask what it contains (a split point) without binding again; passes other than the builder read no more of
/// it than that.
/// </remarks>
/// <param name="syntax">The syntax.</param>
/// <param name="operation">The operation, when Roslyn gives one.</param>
/// <param name="reason">Why it is opaque.</param>
/// <param name="split">Whether its syntax holds a split point; unclassified when the builder creates it.</param>
/// <param name="type">The type, or <see langword="void"/> when the operation has none.</param>
/// <param name="category">How the holder uses it.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrOpaqueExpression(SyntaxNode syntax, IOperation? operation, IrOpaqueReason reason,
    IrOpaqueSplit split, ITypeSymbol type, IrCategory category, IrSource source)
    : IrExpression(type, category, operation?.ConstantValue ?? default, source)
{
    /// <summary>The syntax.</summary>
    public SyntaxNode Syntax { get; } = syntax;

    /// <summary>The operation, when Roslyn gives one.</summary>
    public IOperation? Operation { get; } = operation;

    /// <summary>Why it is opaque.</summary>
    public IrOpaqueReason Reason { get; } = reason;

    /// <summary>Whether its syntax holds a split point.</summary>
    public IrOpaqueSplit Split { get; } = split;

    /// <summary>This node with <paramref name="split"/>; this node itself when it is the same.</summary>
    public IrOpaqueExpression WithSplit(IrOpaqueSplit split) =>
        split == Split ? this : new IrOpaqueExpression(Syntax, Operation, Reason, split, Type, Category, Source);
}
