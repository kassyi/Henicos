using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// Computes the getter form and the receiver copy of <see cref="IrPropertyReference"/> and the setter call of
/// <see cref="IrAssignment"/> for the native dialect (#1561).
/// </summary>
internal static class IrAccessorDecider
{
    /// <summary>
    /// The decision of <paramref name="member"/> when it is none of the intrinsic, stack array and array member forms:
    /// <see cref="IrMemberForm.Getter"/> for an instance property with accessor functions, with the struct type of the
    /// copy its getter is called on when a read (not <paramref name="isTarget"/>) finds a readonly variable as the
    /// receiver; <see cref="IrMemberForm.Other"/> otherwise.
    /// </summary>
    public static IrMemberDecision DecideMember(IrPropertyReference member, bool isTarget, IrDecisionContext context)
    {
        var property = member.Property;
        if (member.Receiver is not { } receiver || !PropertyAccessorCall.IsGetter(property, context.Dialect))
        {
            return new IrMemberDecision(IrMemberForm.Other, null, -1, null);
        }

        // [WHY] The syntax path decides the copy on the original operation, so a temporary that a spill put in place of
        // the receiver is read as its initializer
        var copyType = !isTarget &&
                       PropertyAccessorCall.NeedsCopy(property, context.Dialect, context.Compilation) &&
                       IrReadOnlyReceivers.IsReadOnlyVariable(context.TemporaryInitializers.Resolve(receiver), context.Method)
            ? PropertyAccessorCall.CopyTypeOf(property, null, context.Compilation)
            : null;
        return new IrMemberDecision(IrMemberForm.Getter, null, -1, copyType);
    }

    /// <summary>
    /// How a simple assignment to <paramref name="property"/> is written by <paramref name="dialect"/>.
    /// </summary>
    public static IrAccessorStore DecideStore(IPropertySymbol property, ITranspilerDialect dialect)
    {
        if (PropertyAccessorCall.IsSetter(property, dialect))
        {
            return IrAccessorStore.CallsSetter;
        }

        return PropertyAccessorCall.WritesBackingField(property, dialect)
            ? IrAccessorStore.WritesBackingField
            : IrAccessorStore.None;
    }
}
