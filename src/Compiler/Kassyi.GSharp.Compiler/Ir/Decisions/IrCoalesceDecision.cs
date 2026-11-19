namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>Decisions for an <see cref="Expressions.IrCoalesce"/> node (#1561).</summary>
/// <param name="leftForm">How <see langword="null"/> is represented for the left operand.</param>
/// <param name="castsToResult">Whether both branches are cast to the result type (non-array left only).</param>
/// <param name="leftIsStackArrayView">Whether the left operand is a non-empty stack array local view.</param>
internal sealed class IrCoalesceDecision(NullForm leftForm, bool castsToResult, bool leftIsStackArrayView)
    : IrDecision
{
    /// <summary>How <see langword="null"/> is represented for the left operand.</summary>
    public NullForm LeftForm { get; } = leftForm;

    /// <summary>Whether both branches are cast to the result type (non-array left only).</summary>
    public bool CastsToResult { get; } = castsToResult;

    /// <summary>Whether the left operand is a non-empty stack array local view.</summary>
    public bool LeftIsStackArrayView { get; } = leftIsStackArrayView;
}
