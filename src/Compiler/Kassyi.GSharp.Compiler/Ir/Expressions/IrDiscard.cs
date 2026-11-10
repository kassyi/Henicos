namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>A discard, <c>out _</c> or the target of <c>_ = x</c>.</summary>
/// <param name="type">The type of the discarded value.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrDiscard(ITypeSymbol type, IrSource source)
    : IrExpression(type, IrCategory.Location, default, source);
