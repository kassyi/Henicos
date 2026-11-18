namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>What the decision pass decided for an <see cref="Expressions.IrConversion"/>.</summary>
/// <param name="form">The conversion form.</param>
/// <param name="helper">
/// The Prelude float-to-integer helper when <paramref name="form"/> is <see cref="IrConversionForm.FloatToInteger"/>,
/// or <see langword="null"/>.
/// </param>
/// <param name="needsCast">Whether the helper result is cast to a narrow or enum target type.</param>
/// <param name="folded">
/// The constant value when <paramref name="form"/> is <see cref="IrConversionForm.Folded"/>, or
/// <see langword="null"/>.
/// </param>
internal sealed class IrConversionDecision(IrConversionForm form, string? helper, bool needsCast,
    IrFoldedConstant? folded) : IrDecision
{
    /// <summary>The conversion form.</summary>
    public IrConversionForm Form { get; } = form;

    /// <summary>
    /// The Prelude float-to-integer helper when <see cref="Form"/> is <see cref="IrConversionForm.FloatToInteger"/>,
    /// or <see langword="null"/>.
    /// </summary>
    public string? Helper { get; } = helper;

    /// <summary>Whether the helper result is cast to a narrow or enum target type.</summary>
    public bool NeedsCast { get; } = needsCast;

    /// <summary>
    /// The constant value when <see cref="Form"/> is <see cref="IrConversionForm.Folded"/>, or
    /// <see langword="null"/>.
    /// </summary>
    public IrFoldedConstant? Folded { get; } = folded;
}
