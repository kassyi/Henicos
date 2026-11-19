using Kassyi.GSharp.Compiler.Ir.Decisions;

namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>An element of a one-dimensional array.</summary>
/// <param name="receiver">The array.</param>
/// <param name="index">The index.</param>
/// <param name="type">The element type.</param>
/// <param name="category">How the holder uses the element.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrElementReference(IrExpression receiver, IrExpression index, ITypeSymbol type,
    IrCategory category, IrSource source) : IrExpression(type, category, default, source)
{
    /// <summary>The array.</summary>
    public IrExpression Receiver { get; } = receiver;

    /// <summary>The index.</summary>
    public IrExpression Index { get; } = index;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrElementReference Update(IrExpression receiver, IrExpression index) =>
        receiver == Receiver && index == Index
            ? this
            : KeepSpelling(new IrElementReference(receiver, index, Type, Category, Source));

    /// <summary>The decisions of the pass; an error before it ran.</summary>
    public IrElementDecision Decided => GetDecision<IrElementDecision>();

    /// <summary>This node with <paramref name="decision"/>.</summary>
    public IrElementReference WithDecision(IrElementDecision decision) =>
        IrExpression.WithDecision<IrElementReference>(this, decision);
}
