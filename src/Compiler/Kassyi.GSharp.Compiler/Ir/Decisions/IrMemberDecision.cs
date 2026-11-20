namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// Decisions for an <see cref="Expressions.IrPropertyReference"/> node (#1561).
/// <see cref="Form"/> values <see cref="IrMemberForm.DialectIntrinsic"/> and <see cref="IrMemberForm.Getter"/> and
/// <see cref="CopyType"/> are native-dialect decisions and are not read by managed writers.
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
/// <param name="copyType">
/// The struct type of the copy that a read calls the getter on when <paramref name="form"/> is
/// <see cref="IrMemberForm.Getter"/>; otherwise <see langword="null"/>.
/// </param>
internal sealed class IrMemberDecision(IrMemberForm form, IrErrorSite? receiverSite, int stackArrayLength,
    INamedTypeSymbol? copyType) : IrDecision
{
    /// <summary>
    /// The classified member form; <see cref="IrMemberForm.DialectIntrinsic"/> and <see cref="IrMemberForm.Getter"/>
    /// are native-dialect decisions.
    /// </summary>
    public IrMemberForm Form { get; } = form;

    /// <summary>The array-null error site for an unchecked <see cref="IrMemberForm.ArrayMember"/> receiver.</summary>
    public IrErrorSite? ReceiverSite { get; } = receiverSite;

    /// <summary>
    /// The constant stack array length, or <c>-1</c> when <see cref="Form"/> is not
    /// <see cref="IrMemberForm.StackArrayLength"/>.
    /// </summary>
    public int StackArrayLength { get; } = stackArrayLength;

    /// <summary>
    /// The struct type of the copy of the receiver that a read calls the getter on (#610): a
    /// <see cref="IrMemberForm.Getter"/> whose getter may change its receiver, read on a readonly variable;
    /// <see langword="null"/> for a call on the receiver itself and for the target of an assignment or a step. A
    /// native-dialect decision.
    /// </summary>
    public INamedTypeSymbol? CopyType { get; } = copyType;
}
