namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>What the decision pass decided for an <see cref="Expressions.IrConditional"/>.</summary>
/// <param name="promotedType">
/// The C# promoted type of the branches when C++ would pick another type, or <see langword="null"/>.
/// </param>
/// <param name="convertsWhenTrue">Whether the true branch must be cast to <paramref name="promotedType"/>.</param>
/// <param name="convertsWhenFalse">Whether the false branch must be cast to <paramref name="promotedType"/>.</param>
internal sealed class IrConditionalDecision(ITypeSymbol? promotedType, bool convertsWhenTrue,
    bool convertsWhenFalse) : IrDecision
{
    /// <summary>
    /// The C# promoted type of the branches when C++ would pick another type, or <see langword="null"/>.
    /// </summary>
    public ITypeSymbol? PromotedType { get; } = promotedType;

    /// <summary>Whether the true branch must be cast to <see cref="PromotedType"/>.</summary>
    public bool ConvertsWhenTrue { get; } = convertsWhenTrue;

    /// <summary>Whether the false branch must be cast to <see cref="PromotedType"/>.</summary>
    public bool ConvertsWhenFalse { get; } = convertsWhenFalse;
}
