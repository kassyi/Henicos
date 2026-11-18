namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>What the decision pass decided for an <see cref="Expressions.IrBinary"/>.</summary>
/// <param name="folded">The constant value when folded, or <see langword="null"/>.</param>
/// <param name="floatingRemainder">
/// The C math remainder helper (<c>fmodf</c> / <c>fmod</c>), or <see langword="null"/>.
/// </param>
/// <param name="helper">The Prelude integer helper name, or <see langword="null"/>.</param>
/// <param name="site">
/// The divisor check error site when <paramref name="helper"/> takes one, or <see langword="null"/>.
/// </param>
/// <param name="shiftMask">The shift count mask (31 or 63), or 0 when unmasked.</param>
/// <param name="narrowEnumType">
/// The underlying type of a narrow enum operation to cast back to, or <see langword="null"/>.
/// </param>
/// <param name="promotedType">
/// The C# promoted type when C++ usual arithmetic conversions differ, or <see langword="null"/>.
/// </param>
/// <param name="convertsLeft">Whether the left operand must be cast to <paramref name="promotedType"/>.</param>
/// <param name="convertsRight">Whether the right operand must be cast to <paramref name="promotedType"/>.</param>
/// <param name="managedDivision">The managed division plan when applicable, or <see langword="null"/>.</param>
internal sealed class IrBinaryDecision(IrFoldedConstant? folded, string? floatingRemainder, string? helper,
    IrErrorSite? site, int shiftMask, ITypeSymbol? narrowEnumType, ITypeSymbol? promotedType, bool convertsLeft,
    bool convertsRight, ManagedDivisionPlan? managedDivision) : IrDecision
{
    /// <summary>The constant value when folded, or <see langword="null"/>.</summary>
    public IrFoldedConstant? Folded { get; } = folded;

    /// <summary>The C math remainder helper (<c>fmodf</c> / <c>fmod</c>), or <see langword="null"/>.</summary>
    public string? FloatingRemainder { get; } = floatingRemainder;

    /// <summary>The Prelude integer helper name, or <see langword="null"/>.</summary>
    public string? Helper { get; } = helper;

    /// <summary>The divisor check error site when <see cref="Helper"/> takes one, or <see langword="null"/>.</summary>
    public IrErrorSite? Site { get; } = site;

    /// <summary>The shift count mask (31 or 63), or 0 when unmasked.</summary>
    public int ShiftMask { get; } = shiftMask;

    /// <summary>The underlying type of a narrow enum operation to cast back to, or <see langword="null"/>.</summary>
    public ITypeSymbol? NarrowEnumType { get; } = narrowEnumType;

    /// <summary>The C# promoted type when C++ usual arithmetic conversions differ, or <see langword="null"/>.</summary>
    public ITypeSymbol? PromotedType { get; } = promotedType;

    /// <summary>Whether the left operand must be cast to <see cref="PromotedType"/>.</summary>
    public bool ConvertsLeft { get; } = convertsLeft;

    /// <summary>Whether the right operand must be cast to <see cref="PromotedType"/>.</summary>
    public bool ConvertsRight { get; } = convertsRight;

    /// <summary>The managed division plan when applicable, or <see langword="null"/>.</summary>
    public ManagedDivisionPlan? ManagedDivision { get; } = managedDivision;
}
