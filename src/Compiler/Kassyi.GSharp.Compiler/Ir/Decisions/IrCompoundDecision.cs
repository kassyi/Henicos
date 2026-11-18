namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>What the decision pass decided for an <see cref="Expressions.IrCompoundAssignment"/>.</summary>
/// <param name="floatingRemainder">
/// The C math remainder helper (<c>fmodf</c> / <c>fmod</c>), or <see langword="null"/>.
/// </param>
/// <param name="helper">The Prelude integer helper name, or <see langword="null"/>.</param>
/// <param name="site">
/// The divisor check error site when <paramref name="helper"/> takes one, or <see langword="null"/>.
/// </param>
/// <param name="shiftMask">The shift count mask (31 or 63), or 0 when unmasked or using a helper.</param>
/// <param name="narrowing">
/// The target type narrower than the operator type to cast back to, or <see langword="null"/>.
/// </param>
/// <param name="operatorType">The promoted operator type of the compound assignment.</param>
/// <param name="convertsValue">Whether a floating-point remainder converts the right value to the target type.</param>
internal sealed class IrCompoundDecision(string? floatingRemainder, string? helper, IrErrorSite? site, int shiftMask,
    ITypeSymbol? narrowing, SpecialType operatorType, bool convertsValue) : IrDecision
{
    /// <summary>The C math remainder helper (<c>fmodf</c> / <c>fmod</c>), or <see langword="null"/>.</summary>
    public string? FloatingRemainder { get; } = floatingRemainder;

    /// <summary>The Prelude integer helper name, or <see langword="null"/>.</summary>
    public string? Helper { get; } = helper;

    /// <summary>The divisor check error site when <see cref="Helper"/> takes one, or <see langword="null"/>.</summary>
    public IrErrorSite? Site { get; } = site;

    /// <summary>The shift count mask (31 or 63), or 0 when unmasked or using a helper.</summary>
    public int ShiftMask { get; } = shiftMask;

    /// <summary>The target type narrower than the operator type to cast back to, or <see langword="null"/>.</summary>
    public ITypeSymbol? Narrowing { get; } = narrowing;

    /// <summary>The promoted operator type of the compound assignment.</summary>
    public SpecialType OperatorType { get; } = operatorType;

    /// <summary>Whether a floating-point remainder converts the right value to the target type.</summary>
    public bool ConvertsValue { get; } = convertsValue;
}
