namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The emission plan for the receiver of a struct instance device function call or a reduced extension call.
/// </summary>
internal readonly record struct ReceiverPlan(
    ReceiverForm Form,
    ExpressionSyntax? Expression,
    SelfForm SelfForm,
    INamedTypeSymbol? CopyType,
    bool ReadOnlyCallee,
    bool IsSelf = false,
    bool IsExtensionReceiver = false,
    ITypeSymbol? ValueType = null)
{
    /// <summary>Whether the receiver is <see langword="this"/> (implicit or explicit) copied before a mutating call.</summary>
    public bool IsCopiedSelf => Form == ReceiverForm.Copy && IsSelf;

    /// <summary>Whether this receiver participates as operand 0 in <see cref="Helpers.EvaluationOrder"/>.</summary>
    public bool HasSequencedOperand => Form != ReceiverForm.Self && (Expression != null || IsCopiedSelf);
}
