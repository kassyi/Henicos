namespace Kassyi.GSharp.Compiler.Ir.Expressions;

/// <summary>The default value of a type that has no constant default (<c>default(S)</c> of a struct).</summary>
/// <param name="type">The type.</param>
/// <param name="category">How the holder uses the value.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrDefaultValue(ITypeSymbol type, IrCategory category, IrSource source)
    : IrExpression(type, category, default, source);
