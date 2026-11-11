namespace Kassyi.GSharp.Compiler.Ir.Statements;

/// <summary>An expression evaluated for its effects.</summary>
/// <param name="expression">The expression.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrExpressionStatement(IrExpression expression, IrSource source) : IrStatement(source)
{
    /// <summary>The expression.</summary>
    public IrExpression Expression { get; } = expression;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrExpressionStatement Update(IrExpression expression) =>
        expression == Expression ? this : new IrExpressionStatement(expression, Source);
}
