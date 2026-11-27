using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// Computes the null-check, store, null-test, coalesce and member decisions of <see cref="IrElementReference"/>,
/// <see cref="IrAssignment"/>, <see cref="IrNullTest"/>, <see cref="IrCoalesce"/> and <see cref="IrPropertyReference"/>
/// (#1561).
/// </summary>
internal static class IrNullDecider
{
    /// <summary>Decides the null check, site, and index overflow check for <paramref name="element"/>.</summary>
    public static IrElementDecision DecideElement(IrElementReference element, IrDecisionContext context)
    {
        // C# converts the index to ulong (from ulong, nuint or a user-defined implicit conversion) and then checks it
        // with conv.ovf.i.un
        var indexNeedsOverflow =
            element.Index.Type?.SpecialType == SpecialType.System_UInt64 &&
            element.Receiver.Type is IArrayTypeSymbol { Rank: 1 };
        if (ArrayNullCheck.IsIndexOrRangeType(element.Index.Type) ||
            !IrNonNullProof.NeedsCheck(element.Receiver, context))
        {
            return new IrElementDecision(IrElementCheck.None, null, indexNeedsOverflow);
        }

        return new IrElementDecision(
            IrElementCheck.NullChecked,
            CreateArrayNullSite(element, element.Receiver),
            indexNeedsOverflow);
    }

    /// <summary>Decides the element-store plan or the accessor store for <paramref name="assignment"/>.</summary>
    public static IrAssignmentDecision DecideAssignment(IrAssignment assignment, IrDecisionContext context)
    {
        if (assignment is { IsRef: false, Target: IrPropertyReference { Property: var property } })
        {
            return new IrAssignmentDecision(null, IrAccessorDecider.DecideStore(property, context.Dialect));
        }

        if (assignment.IsRef ||
            assignment.Target is not IrElementReference element ||
            ArrayNullCheck.IsIndexOrRangeType(element.Index.Type) ||
            element.Receiver.Type is not IArrayTypeSymbol { Rank: 1 })
        {
            return IrAssignmentDecision.None;
        }

        var bindsByRef = IrNonNullProof.IsStackArrayReference(element.Receiver);
        var site = IrNonNullProof.NeedsCheck(element.Receiver, context)
            ? CreateArrayNullSite(element, element.Receiver)
            : (IrErrorSite?)null;
        return new IrAssignmentDecision(new IrElementStore(bindsByRef, site), IrAccessorStore.None);
    }

    /// <summary>
    /// Decides the null form, skipped casts, and stack-array-view flag for <paramref name="nullTest"/>.
    /// </summary>
    public static IrNullTestDecision DecideNullTest(IrNullTest nullTest, IrDecisionContext context)
    {
        var operand = SkipNullPreservingCasts(nullTest.Operand, out var skipped);
        var form = NullRepresentation.GetNullForm(operand.Type);
        var isView = TryGetStackArrayLocal(operand, unwrapNotNull: true, out var local) &&
                     StackArrayLocals.NeedsView(local, context.Compilation);
        return new IrNullTestDecision(form, skipped, isView);
    }

    /// <summary>
    /// Decides the left null form, result-cast flag, and stack-array-view flag for <paramref name="coalesce"/>.
    /// </summary>
    public static IrCoalesceDecision DecideCoalesce(IrCoalesce coalesce, IrDecisionContext context)
    {
        var leftForm = NullRepresentation.GetNullForm(coalesce.Value.Type);
        var castsToResult = leftForm != NullForm.ArrayView &&
                            NullRepresentation.CoalesceCastsToResult(
                                coalesce.Value.Type,
                                IrNumericFacts.SourceType(coalesce.WhenNull, context.TemporaryInitializers),
                                coalesce.Type);
        var leftOperand = SkipNullPreservingCasts(coalesce.Value, out _);
        var leftIsView = leftForm == NullForm.ArrayView &&
                         TryGetStackArrayLocal(leftOperand, unwrapNotNull: true, out var local) &&
                         StackArrayLocals.NeedsView(local, context.Compilation);
        return new IrCoalesceDecision(leftForm, castsToResult, leftIsView);
    }

    /// <summary>
    /// Decides the member form, receiver site, stack-array length, and receiver copy for <paramref name="member"/>;
    /// <paramref name="isTarget"/> tells that it is the target of an assignment or a step.
    /// </summary>
    public static IrMemberDecision DecideMember(IrPropertyReference member, bool isTarget, IrDecisionContext context)
    {
        if (context.Dialect.TryMapIntrinsic(member.Property, out _))
        {
            return new IrMemberDecision(IrMemberForm.DialectIntrinsic, null, -1, null);
        }

        // [WHY] The syntax path writes the Length of a local whose declaration gives a size before any other member
        // form, whatever the type of the local; a size that is not a constant is left to the writer
        if (member is { Receiver: { } named, Property.Name: "Length" } &&
            IrNonNullProof.UnwrapImplicitReferenceConversions(named) is
                IrVariableReference { Variable.Symbol: ILocalSymbol local } &&
            StackArrayLengthAccess.TryGetStackArraySize(local, context.Compilation, out var size))
        {
            return size.Count is { } length
                ? new IrMemberDecision(IrMemberForm.StackArrayLength, null, length, null)
                : new IrMemberDecision(IrMemberForm.Other, null, -1, null);
        }

        if (member.Receiver is { } receiver && ArrayNullCheck.IsArrayMember(member.Property))
        {
            if (IrNonNullProof.IsStackArrayReference(receiver))
            {
                return new IrMemberDecision(IrMemberForm.Other, null, -1, null);
            }

            var site = IrNonNullProof.NeedsCheck(receiver, context)
                ? CreateArrayNullSite(member, receiver)
                : (IrErrorSite?)null;
            return new IrMemberDecision(IrMemberForm.ArrayMember, site, -1, null);
        }

        return IrAccessorDecider.DecideMember(member, isTarget, context);
    }

    // The receiver as written, outer parentheses and ! included. A temporary that a spill put in its place carries the
    // spelling of the expression it replaced; the syntax path replaces the parentheses written directly around it as
    // well, and reports the temporary at the ! (and what encloses it) left around it, or at the whole member or
    // element access when only parentheses were written
    private static IrErrorSite CreateArrayNullSite(IrExpression owner, IrExpression receiver)
    {
        var unwrapped = IrNonNullProof.UnwrapImplicitReferenceConversions(receiver);
        var source = unwrapped is IrVariableReference { Variable.Kind: IrVariableKind.Temporary } &&
                     !unwrapped.SpellingHasNullForgiving
            ? owner.Source
            : unwrapped.SpellingSource;
        return new IrErrorSite(source, ArrayNullCheck.TrapKind, ArrayNullCheck.SiteMessage);
    }

    private static IrExpression SkipNullPreservingCasts(IrExpression operand, out int skipped)
    {
        skipped = 0;
        while (true)
        {
            operand = IrNonNullProof.UnwrapImplicitReferenceConversions(operand);
            if (operand is IrConversion { IsImplicit: false, Conversion.Method: null } cast &&
                NullRepresentation.PreservesNull(cast.Type, cast.Operand.Type))
            {
                skipped++;
                operand = cast.Operand;
                continue;
            }

            return operand;
        }
    }

    private static bool TryGetStackArrayLocal(IrExpression expression, bool unwrapNotNull,
        [NotNullWhen(true)] out ILocalSymbol? local)
    {
        while (true)
        {
            expression = IrNonNullProof.UnwrapImplicitReferenceConversions(expression);
            if (unwrapNotNull &&
                expression is IrCall { Arguments: [{ } only] } call &&
                NonNullProof.IsNotNullMethod(call.Method))
            {
                expression = only.Value;
                continue;
            }

            break;
        }

        local = expression is IrVariableReference { Variable.Symbol: ILocalSymbol candidate }
            ? candidate
            : null;
        return local is not null;
    }
}
