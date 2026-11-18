namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>What the decision pass decided for an <see cref="Expressions.IrUnary"/>.</summary>
/// <param name="folded">The constant or negated literal value when folded, or <see langword="null"/>.</param>
/// <param name="helper">The Prelude negation helper name, or <see langword="null"/>.</param>
/// <param name="narrowEnumType">
/// The underlying type of a narrow enum operation to cast back to, or <see langword="null"/>.
/// </param>
/// <param name="promotedType">
/// The C# promoted type when C++ integral promotion differs, or <see langword="null"/>.
/// </param>
/// <param name="convertsOperand">Whether the operand must be cast to <paramref name="promotedType"/>.</param>
internal sealed class IrUnaryDecision(IrFoldedConstant? folded, string? helper, ITypeSymbol? narrowEnumType,
    ITypeSymbol? promotedType, bool convertsOperand) : IrDecision
{
    /// <summary>The constant or negated literal value when folded, or <see langword="null"/>.</summary>
    public IrFoldedConstant? Folded { get; } = folded;

    /// <summary>The Prelude negation helper name, or <see langword="null"/>.</summary>
    public string? Helper { get; } = helper;

    /// <summary>The underlying type of a narrow enum operation to cast back to, or <see langword="null"/>.</summary>
    public ITypeSymbol? NarrowEnumType { get; } = narrowEnumType;

    /// <summary>The C# promoted type when C++ integral promotion differs, or <see langword="null"/>.</summary>
    public ITypeSymbol? PromotedType { get; } = promotedType;

    /// <summary>Whether the operand must be cast to <see cref="PromotedType"/>.</summary>
    public bool ConvertsOperand { get; } = convertsOperand;
}
