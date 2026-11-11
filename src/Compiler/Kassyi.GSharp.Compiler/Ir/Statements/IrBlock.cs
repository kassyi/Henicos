namespace Kassyi.GSharp.Compiler.Ir.Statements;

/// <summary>A block of statements and the locals declared in its scope.</summary>
/// <param name="statements">The statements.</param>
/// <param name="locals">The locals whose scope is the block (an <c>out var</c> local of a statement included).</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrBlock(ImmutableArray<IrStatement> statements, ImmutableArray<IrVariable> locals,
    IrSource source) : IrStatement(source)
{
    /// <summary>The statements.</summary>
    public ImmutableArray<IrStatement> Statements { get; } = statements;

    /// <summary>The locals whose scope is the block.</summary>
    public ImmutableArray<IrVariable> Locals { get; } = locals;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrBlock Update(ImmutableArray<IrStatement> statements) =>
        statements == Statements ? this : new IrBlock(statements, Locals, Source);
}
