namespace Kassyi.GSharp.Compiler.Ir.Statements;

/// <summary>
/// A statement the builder does not represent with nodes yet; a leaf whose syntax goes to the syntax-based rules.
/// </summary>
/// <param name="syntax">The syntax.</param>
/// <param name="operation">The operation, when Roslyn gives one.</param>
/// <param name="reason">Why it is opaque.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrOpaqueStatement(SyntaxNode syntax, IOperation? operation, IrOpaqueReason reason,
    IrSource source) : IrStatement(source)
{
    /// <summary>The syntax.</summary>
    public SyntaxNode Syntax { get; } = syntax;

    /// <summary>The operation, when Roslyn gives one.</summary>
    public IOperation? Operation { get; } = operation;

    /// <summary>Why it is opaque.</summary>
    public IrOpaqueReason Reason { get; } = reason;
}
