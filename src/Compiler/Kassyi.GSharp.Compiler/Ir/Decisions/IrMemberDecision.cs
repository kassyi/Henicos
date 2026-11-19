namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// Decisions for an <see cref="Expressions.IrPropertyReference"/> node (#1561).
/// <see cref="Form"/> value <see cref="IrMemberForm.DialectIntrinsic"/> is a native-dialect decision and is not read
/// by managed writers.
/// </summary>
/// <param name="form">The classified member form.</param>
/// <param name="receiverSite">
/// The array-null error site when <paramref name="form"/> is <see cref="IrMemberForm.ArrayMember"/> and the receiver
/// may be null.
/// </param>
/// <param name="stackArrayLength">
/// The constant length when <paramref name="form"/> is <see cref="IrMemberForm.StackArrayLength"/>; otherwise
/// <c>-1</c>.
/// </param>
internal sealed class IrMemberDecision(IrMemberForm form, IrErrorSite? receiverSite, int stackArrayLength) : IrDecision
{
    /// <summary>
    /// The classified member form; <see cref="IrMemberForm.DialectIntrinsic"/> is a native-dialect decision.
    /// </summary>
    public IrMemberForm Form { get; } = form;

    /// <summary>The array-null error site for an unchecked <see cref="IrMemberForm.ArrayMember"/> receiver.</summary>
    public IrErrorSite? ReceiverSite { get; } = receiverSite;

    /// <summary>
    /// The constant stack array length, or <c>-1</c> when <see cref="Form"/> is not
    /// <see cref="IrMemberForm.StackArrayLength"/>.
    /// </summary>
    public int StackArrayLength { get; } = stackArrayLength;
}
