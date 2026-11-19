#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Compares the null and call decisions before and after the split, and the shifts of a spilled operand to unproven
/// (#1561).
/// </summary>
internal static class IrNullPostSplitComparer
{
    public static void ComparePreAndPost(IrExpression pre, IrExpression post, string where,
        IrNullAgreementCounts counts)
    {
        var label = $"{where}: {pre.GetType().Name} {pre.Source.Span}";
        switch (pre, post)
        {
            case (IrElementReference pe, IrElementReference qe):
                CompareElementPost(pe, qe, label, counts);
                break;
            case (IrAssignment pa, IrAssignment qa):
                CompareAssignmentPost(pa, qa, label, counts);
                break;
            case (IrNullTest pn, IrNullTest qn):
                if (pn.Decided.Form != qn.Decided.Form ||
                    pn.Decided.SkippedConversions != qn.Decided.SkippedConversions ||
                    pn.Decided.OperandIsStackArrayView != qn.Decided.OperandIsStackArrayView)
                {
                    counts.Mismatches.Add($"{label}: post-split IrNullTest mismatch");
                }
                break;
            case (IrCoalesce pc, IrCoalesce qc):
                if (pc.Decided.LeftForm != qc.Decided.LeftForm ||
                    pc.Decided.CastsToResult != qc.Decided.CastsToResult ||
                    pc.Decided.LeftIsStackArrayView != qc.Decided.LeftIsStackArrayView)
                {
                    counts.Mismatches.Add($"{label}: post-split IrCoalesce mismatch");
                }
                break;
            case (IrPropertyReference pm, IrPropertyReference qm):
                CompareMemberPost(pm, qm, label, counts);
                break;
            case (IrCall pcall, IrCall qcall):
                CompareCallPost(pcall, qcall, label, counts);
                break;
        }
    }

    private static void CompareElementPost(IrElementReference pe, IrElementReference qe, string label,
        IrNullAgreementCounts counts)
    {
        var pd = pe.Decided;
        var qd = qe.Decided;
        if (pd.IndexNeedsOverflowCheck != qd.IndexNeedsOverflowCheck)
        {
            counts.Mismatches.Add($"{label}: post-split IndexNeedsOverflowCheck mismatch");
        }

        if (pd.Check == qd.Check)
        {
            CompareReceiverSite(pd.Site, qd.Site, qe, qe.Receiver, $"{label}: post-split ElementSite", counts);
            return;
        }

        if (pd.Check == IrElementCheck.None && qd.Check == IrElementCheck.NullChecked &&
            IsSpilledReceiverSite(qe, qe.Receiver, qd.Site))
        {
            counts.SpillShifts.Add($"{label}: ElementCheck shifted None->NullChecked on temp receiver");
            return;
        }

        counts.Mismatches.Add($"{label}: post-split ElementCheck {pd.Check} -> {qd.Check}");
    }

    private static void CompareAssignmentPost(IrAssignment pa, IrAssignment qa, string label,
        IrNullAgreementCounts counts)
    {
        var ps = pa.Decided.Store;
        var qs = qa.Decided.Store;
        if ((ps is not null) != (qs is not null))
        {
            counts.Mismatches.Add($"{label}: post-split Store presence mismatch");
            return;
        }

        if (ps is not { } pstore || qs is not { } qstore)
        {
            return;
        }

        if (pstore.BindsArrayByReference != qstore.BindsArrayByReference)
        {
            counts.Mismatches.Add($"{label}: post-split BindsArrayByReference mismatch");
        }

        if ((pstore.Site is not null) == (qstore.Site is not null))
        {
            if (qa.Target is IrElementReference target)
            {
                CompareReceiverSite(pstore.Site, qstore.Site, target, target.Receiver,
                    $"{label}: post-split Store.Site", counts);
            }

            return;
        }

        if (pstore.Site is null && qa.Target is IrElementReference qe &&
            IsSpilledReceiverSite(qe, qe.Receiver, qstore.Site))
        {
            counts.SpillShifts.Add($"{label}: Store.Site shifted null->non-null on temp receiver");
            return;
        }

        counts.Mismatches.Add($"{label}: post-split Store.Site mismatch");
    }

    private static void CompareMemberPost(IrPropertyReference pm, IrPropertyReference qm, string label,
        IrNullAgreementCounts counts)
    {
        var pd = pm.Decided;
        var qd = qm.Decided;
        if (pd.Form != qd.Form || pd.StackArrayLength != qd.StackArrayLength)
        {
            counts.Mismatches.Add($"{label}: post-split IrMemberDecision Form/Length mismatch");
            return;
        }

        if ((pd.ReceiverSite is not null) == (qd.ReceiverSite is not null))
        {
            if (qm.Receiver is { } spilled)
            {
                CompareReceiverSite(pd.ReceiverSite, qd.ReceiverSite, qm, spilled,
                    $"{label}: post-split Member.ReceiverSite", counts);
            }

            return;
        }

        if (pd.ReceiverSite is null && qm.Receiver is { } receiver &&
            IsSpilledReceiverSite(qm, receiver, qd.ReceiverSite))
        {
            counts.SpillShifts.Add($"{label}: Member.ReceiverSite shifted null->non-null on temp receiver");
            return;
        }

        counts.Mismatches.Add($"{label}: post-split Member.ReceiverSite mismatch");
    }

    private static void CompareCallPost(IrCall pcall, IrCall qcall, string label, IrNullAgreementCounts counts)
    {
        var pd = pcall.Decided;
        var qd = qcall.Decided;
        if (pd.Claim != qd.Claim || pd.Target != qd.Target || !Equals(pd.Managed, qd.Managed) ||
            pd.Arguments.Length != qd.Arguments.Length)
        {
            counts.Mismatches.Add($"{label}: post-split IrCall header mismatch");
            return;
        }

        for (var i = 0; i < pd.Arguments.Length; i++)
        {
            var pa = pd.Arguments[i];
            var qa = qd.Arguments[i];
            if (!SymbolEqualityComparer.Default.Equals(pa.ConvertedTo, qa.ConvertedTo))
            {
                counts.Mismatches.Add($"{label}: post-split Arg[{i}].ConvertedTo mismatch");
            }

            if (pa.Check.Kind == qa.Check.Kind)
            {
                CompareArgumentSites(pa.Check, qa.Check, $"{label}: post-split Arg[{i}]", counts);
                continue;
            }

            if (pa.Check.Kind == ArgumentCheckKind.None && qa.Check.Kind != ArgumentCheckKind.None &&
                qcall.Arguments[i].Value is IrVariableReference { Variable.Kind: IrVariableKind.Temporary } temporary &&
                ArgumentSitesAt(qa.Check, temporary.SpellingSpan))
            {
                counts.SpillShifts.Add($"{label}: Call.Arg[{i}].Check shifted None->{qa.Check.Kind} on temp");
                continue;
            }

            counts.Mismatches.Add($"{label}: post-split Arg[{i}].Check {pa.Check.Kind} -> {qa.Check.Kind}");
        }
    }

    private static void CompareArgumentSites(IrArgumentCheck pre, IrArgumentCheck post, string label,
        IrNullAgreementCounts counts)
    {
        CompareSite(pre.ArraySite, post.ArraySite, label + " ArraySite", counts);
        if (pre.StructChecks.Length != post.StructChecks.Length)
        {
            counts.Mismatches.Add(
                $"{label} StructChecks count {pre.StructChecks.Length} -> {post.StructChecks.Length}");
            return;
        }

        for (var k = 0; k < pre.StructChecks.Length; k++)
        {
            CompareSite(pre.StructChecks[k].Site, post.StructChecks[k].Site, $"{label} StructChecks[{k}]", counts);
        }
    }

    // The site of a check is reported at a place of the source; the split moves no place
    private static void CompareSite(IrErrorSite? pre, IrErrorSite? post, string label, IrNullAgreementCounts counts)
    {
        if (pre is { } p && post is { } q &&
            (p.Source.TreeOrdinal != q.Source.TreeOrdinal || p.Source.Span != q.Source.Span ||
             p.TrapKind != q.TrapKind || p.Message != q.Message))
        {
            counts.Mismatches.Add($"{label} {p.Source.Span} -> {q.Source.Span}");
        }
    }

    // A receiver that the split put in a temporary keeps its site where a ! written around it is left, and moves to
    // the whole access when only parentheses were written around it: the temporary takes those (as the syntax path)
    private static void CompareReceiverSite(IrErrorSite? pre, IrErrorSite? post, IrExpression owner,
        IrExpression receiver, string label, IrNullAgreementCounts counts)
    {
        if (IsBareTemporary(receiver))
        {
            if (post is { } q && q.Source.Span != owner.Source.Span)
            {
                counts.Mismatches.Add($"{label} {q.Source.Span} is not the access {owner.Source.Span}");
            }

            return;
        }

        CompareSite(pre, post, label, counts);
    }

    // A receiver that the split put in a temporary is no longer proven
    private static bool IsSpilledReceiverSite(IrExpression owner, IrExpression receiver, IrErrorSite? site) =>
        receiver is IrVariableReference { Variable.Kind: IrVariableKind.Temporary } &&
        site is { } s && s.Source.Span == (IsBareTemporary(receiver) ? owner.Source.Span : receiver.SpellingSpan);

    private static bool IsBareTemporary(IrExpression receiver) =>
        receiver is IrVariableReference { Variable.Kind: IrVariableKind.Temporary } &&
        !receiver.SpellingHasNullForgiving;

    // An argument that the split put in a temporary is checked at the argument as written
    private static bool ArgumentSitesAt(IrArgumentCheck check, TextSpan span) =>
        (check.ArraySite is not { } array || array.Source.Span == span) &&
        check.StructChecks.All(field => field.Site.Source.Span == span);
}
