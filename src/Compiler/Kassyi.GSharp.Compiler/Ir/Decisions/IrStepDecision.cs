namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>What the decision pass decided for an <see cref="Expressions.IrIncrement"/>.</summary>
/// <param name="helper">The Prelude helper that performs the step, or <see langword="null"/>.</param>
/// <param name="undoHelper">The Prelude helper that steps back for a postfix value, or <see langword="null"/>.</param>
/// <param name="narrowing">
/// The target type narrower than <see langword="int"/> to cast back to, or <see langword="null"/>.
/// </param>
internal sealed class IrStepDecision(string? helper, string? undoHelper, ITypeSymbol? narrowing) : IrDecision
{
    /// <summary>The Prelude helper that performs the step, or <see langword="null"/>.</summary>
    public string? Helper { get; } = helper;

    /// <summary>The Prelude helper that steps back for a postfix value, or <see langword="null"/>.</summary>
    public string? UndoHelper { get; } = undoHelper;

    /// <summary>
    /// The target type narrower than <see langword="int"/> to cast back to, or <see langword="null"/>.
    /// </summary>
    public ITypeSymbol? Narrowing { get; } = narrowing;
}
