using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// The struct receivers that C# copies before a call: a read-only variable whose method or accessor is not
/// <see langword="readonly"/> is called on a copy taken where the receiver is evaluated (C# §12.8.10.2: a hidden copy
/// protects the variable).
/// </summary>
/// <remarks>
/// The builder makes such a receiver a <see cref="IrCategory.Location"/> (it looks at the member, not at the variable),
/// so a split in the arguments would make the call see what the split wrote. Until the builder makes it the value that
/// C# copies, the planner leaves such a call to the syntax-based lowering
/// (<see cref="IrSpillFallback.ReadOnlyVariableReceiver"/>).
/// </remarks>
internal static class IrReadOnlyReceivers
{
    /// <summary>
    /// Whether C# calls <paramref name="member"/> on a copy of <paramref name="receiver"/> that the IR does not show:
    /// the receiver is a struct location that is a read-only variable and the member is not <see langword="readonly"/>.
    /// </summary>
    public static bool IsCopiedBeforeTheCall(IrExpression? receiver, IMethodSymbol? member) =>
        receiver is { Category: IrCategory.Location, Type: { IsValueType: true, IsRefLikeType: false } type } &&
        member is { IsReadOnly: false, IsStatic: false } &&
        !type.IsReadOnly &&
        IsReadOnlyVariable(receiver);

    /// <summary>
    /// Whether <paramref name="location"/> is a variable C# does not let a member write: an <see langword="in"/> or
    /// <see langword="ref readonly"/> parameter or local, <see langword="this"/> of a <see langword="readonly"/>
    /// member, a <see langword="readonly"/> field, a field of such a struct variable, or a reference returned as
    /// <see langword="ref readonly"/>.
    /// </summary>
    public static bool IsReadOnlyVariable(IrExpression location) => location switch
    {
        IrVariableReference { Variable.RefKind: RefKind.In or RefKind.RefReadOnlyParameter } => true,
        IrFieldReference { Field.IsReadOnly: true } => true,
        IrFieldReference { Receiver: { Type.IsValueType: true } receiver } => IsReadOnlyVariable(receiver),
        IrCall { Method.ReturnsByRefReadonly: true } => true,
        IrPropertyReference { Property.ReturnsByRefReadonly: true } => true,
        _ => false
    };
}
