namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>Decisions for an <see cref="Expressions.IrNullTest"/> node (#1561).</summary>
/// <param name="form">How <see langword="null"/> is represented for the tested operand.</param>
/// <param name="skippedConversions">Number of explicit null-preserving casts skipped from the operand.</param>
/// <param name="operandIsStackArrayView">Whether the operand is a non-empty stack array local view.</param>
internal sealed class IrNullTestDecision(NullForm form, int skippedConversions, bool operandIsStackArrayView)
    : IrDecision
{
    /// <summary>How <see langword="null"/> is represented for the tested operand.</summary>
    public NullForm Form { get; } = form;

    /// <summary>Number of explicit null-preserving casts skipped from the operand.</summary>
    public int SkippedConversions { get; } = skippedConversions;

    /// <summary>Whether the operand is a non-empty stack array local view.</summary>
    public bool OperandIsStackArrayView { get; } = operandIsStackArrayView;
}
