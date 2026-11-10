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
/// <param name="type">The type, or <see langword="void"/> when the operation has none.</param>
/// <param name="category">How the holder uses it.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrOpaqueExpression(SyntaxNode syntax, IOperation? operation, IrOpaqueReason reason,
    ITypeSymbol type, IrCategory category, IrSource source)
    : IrExpression(type, category, operation?.ConstantValue ?? default, source)
{
    /// <summary>The syntax.</summary>
    public SyntaxNode Syntax { get; } = syntax;

    /// <summary>The operation, when Roslyn gives one.</summary>
    public IOperation? Operation { get; } = operation;

    /// <summary>Why it is opaque.</summary>
    public IrOpaqueReason Reason { get; } = reason;
}
