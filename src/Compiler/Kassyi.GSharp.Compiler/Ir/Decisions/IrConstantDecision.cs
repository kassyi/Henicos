namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// What the decision pass decided for an <see cref="Expressions.IrConstant"/>. Only applies when
/// <see cref="Expressions.IrConstant.Origin"/> is a <see langword="const"/> field (literals use their literal text
/// and <see langword="const"/> locals are spelled by name).
/// </summary>
/// <param name="typeAtUse">The type C# gives the constant where it is used.</param>
internal sealed class IrConstantDecision(ITypeSymbol typeAtUse) : IrDecision
{
    /// <summary>The type C# gives the constant where it is used.</summary>
    public ITypeSymbol TypeAtUse { get; } = typeAtUse;
}
