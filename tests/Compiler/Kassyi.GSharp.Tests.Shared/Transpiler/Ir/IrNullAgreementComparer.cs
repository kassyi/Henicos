#nullable enable
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.StackArrays;
using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Compares the IR null, element, assignment, null-test, coalesce, and member decisions against legacy syntax helpers
/// (#1561).
/// </summary>
internal static class IrNullAgreementComparer
{
    public static void CompareNode(IrExpression expression, ExpressionSyntax syntax, SemanticModel model,
        Compilation compilation, HashSet<ISymbol> frameSymbols, string where, IrNullAgreementCounts counts)
    {
        switch (expression)
        {
            case IrElementReference element when syntax is ElementAccessExpressionSyntax elemSyntax &&
                                                 elemSyntax.ArgumentList.Arguments is [{ } arg]:
                Interlocked.Increment(ref counts.ElementCount);
                CompareElement(element, elemSyntax, arg, model, frameSymbols, where, counts);
                break;
            case IrAssignment assignment when syntax is AssignmentExpressionSyntax assignSyntax:
                Interlocked.Increment(ref counts.AssignmentCount);
                CompareAssignment(assignment, assignSyntax, model, frameSymbols, where, counts);
                break;
            case IrNullTest nullTest:
                CompareNullTest(nullTest, syntax, model, where, counts);
                break;
            case IrCoalesce coalesce when syntax is BinaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.CoalesceExpression
            } coalesceSyntax:
                Interlocked.Increment(ref counts.CoalesceCount);
                CompareCoalesce(coalesce, coalesceSyntax, model, where, counts);
                break;
            case IrPropertyReference member:
                CompareMember(member, syntax, model, frameSymbols, where, counts);
                break;
            case IrCall call when syntax is InvocationExpressionSyntax invSyntax &&
                                   model.GetSymbol(invSyntax) is IMethodSymbol called:
                Interlocked.Increment(ref counts.CallCount);
                IrNullCallAgreementComparer.CompareCall(call, invSyntax, called, model, compilation, frameSymbols,
                    where, counts);
                break;
        }
    }

    private static void CompareElement(IrElementReference element, ElementAccessExpressionSyntax syntax,
        ArgumentSyntax arg, SemanticModel model, HashSet<ISymbol> frameSymbols, string where,
        IrNullAgreementCounts counts)
    {
        var d = element.Decided;
        var label = $"{where}: {syntax.Kind()} {syntax.Span}";
        var isIndexOrRange = arg.Expression is RangeExpressionSyntax ||
                             ArrayNullCheck.IsIndexOrRangeType(model.GetTypeSymbol(arg.Expression));
        var legacyCheck = !isIndexOrRange && ArrayNullCheck.NeedsCheck(syntax.Expression, model);
        var irCheck = d.Check == IrElementCheck.NullChecked;

        if (irCheck != legacyCheck)
        {
            if (!(!irCheck && legacyCheck &&
                  IrNullAgreementFrameTracker.TryRecordFrameVariable(syntax.Expression, model, frameSymbols, where,
                      counts)))
            {
                counts.Mismatches.Add($"{label}: ElementCheck syntax {legacyCheck} IR {d.Check}");
            }
        }
        else if (irCheck && (d.Site is not { } site || site.TrapKind != ArrayNullCheck.TrapKind ||
                             site.Message != ArrayNullCheck.SiteMessage || site.Source.Span != syntax.Expression.Span))
        {
            counts.Mismatches.Add($"{label}: ElementSite mismatch IR {d.Site}");
        }

        var legacyOverflow = ArrayElementIndex.NeedsOverflowCheck(syntax, model);
        if (d.IndexNeedsOverflowCheck != legacyOverflow)
        {
            counts.Mismatches.Add(
                $"{label}: IndexNeedsOverflowCheck syntax {legacyOverflow} IR {d.IndexNeedsOverflowCheck}");
        }
    }

    private static void CompareAssignment(IrAssignment assignment, AssignmentExpressionSyntax syntax,
        SemanticModel model, HashSet<ISymbol> frameSymbols, string where, IrNullAgreementCounts counts)
    {
        var d = assignment.Decided;
        var label = $"{where}: {syntax.Kind()} {syntax.Span}";
        var leftElem = syntax.Left as ElementAccessExpressionSyntax;
        var legacyIsArrayElem = !assignment.IsRef && leftElem is not null &&
                                ArrayNullCheck.IsArrayElement(leftElem, model);
        if ((d.Store is not null) != legacyIsArrayElem)
        {
            counts.Mismatches.Add($"{label}: Store syntax {legacyIsArrayElem} IR {d.Store is not null}");
            return;
        }

        if (d.Store is not { } store || leftElem is null)
        {
            return;
        }

        var legacyPlan = ArrayNullCheckEmitter.PlanElementStore(leftElem, model, table: null, currentMemberName: null);
        if (store.BindsArrayByReference != legacyPlan.BindsArrayByReference)
        {
            counts.Mismatches.Add(
                $"{label}: BindsArrayByReference syntax {legacyPlan.BindsArrayByReference} " +
                $"IR {store.BindsArrayByReference}");
        }

        var legacyHasSite = legacyPlan.Site is not null;
        var irHasSite = store.Site is not null;
        if (irHasSite != legacyHasSite &&
            !(!irHasSite && legacyHasSite &&
              IrNullAgreementFrameTracker.TryRecordFrameVariable(leftElem.Expression, model, frameSymbols, where,
                  counts)))
        {
            counts.Mismatches.Add($"{label}: Store.Site syntax {legacyHasSite} IR {irHasSite}");
        }
        else if (store.Site is { } site && legacyHasSite && site.Source.Span != leftElem.Expression.Span)
        {
            counts.Mismatches.Add($"{label}: Store.Site span IR {site.Source.Span}");
        }
    }

    private static void CompareNullTest(IrNullTest nullTest, ExpressionSyntax syntax, SemanticModel model,
        string where, IrNullAgreementCounts counts)
    {
        var tested = ExtractNullTestOperand(syntax, model);
        if (tested is null)
        {
            counts.AddExcluded("NullTestWithoutSyntaxOperand");
            return;
        }

        Interlocked.Increment(ref counts.NullTestCount);
        var skipped = SkipCastsCounting(tested, model, out var skippedCount);
        var expectedForm = NullRepresentation.GetNullForm(model.GetTypeSymbol(skipped));
        var expectedView = StackArrayLocals.NeedsView(skipped, model);
        var d = nullTest.Decided;
        var label = $"{where}: {syntax.Kind()} {syntax.Span}";

        if (d.Form != expectedForm || d.SkippedConversions != skippedCount || d.OperandIsStackArrayView != expectedView)
        {
            counts.Mismatches.Add(
                $"{label}: NullTest syntax ({expectedForm},{skippedCount},{expectedView}) " +
                $"IR ({d.Form},{d.SkippedConversions},{d.OperandIsStackArrayView})");
        }
    }

    private static ExpressionSyntax? ExtractNullTestOperand(ExpressionSyntax syntax, SemanticModel model)
    {
        if (syntax is IsPatternExpressionSyntax isPattern)
        {
            return isPattern.Expression;
        }

        if (syntax is not BinaryExpressionSyntax bin)
        {
            return null;
        }

        if (NullRepresentation.GetNullTestOperand(bin, model) is { } nonNull)
        {
            return nonNull;
        }

        return bin.Right.IsKind(SyntaxKind.NullLiteralExpression, SyntaxKind.DefaultLiteralExpression)
            ? bin.Left
            : bin.Left.IsKind(SyntaxKind.NullLiteralExpression, SyntaxKind.DefaultLiteralExpression)
                ? bin.Right
                : null;
    }

    private static ExpressionSyntax SkipCastsCounting(ExpressionSyntax operand, SemanticModel model, out int count)
    {
        count = 0;
        while (true)
        {
            switch (operand)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    operand = parenthesized.Expression;
                    break;
                case PostfixUnaryExpressionSyntax suppressed
                    when suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    operand = suppressed.Operand;
                    break;
                case CastExpressionSyntax cast when NullRepresentation.PreservesNull(model.GetTypeSymbol(cast),
                    model.GetTypeSymbol(cast.Expression)):
                    operand = cast.Expression;
                    count++;
                    break;
                default:
                    return operand;
            }
        }
    }

    private static void CompareCoalesce(IrCoalesce coalesce, BinaryExpressionSyntax syntax, SemanticModel model,
        string where, IrNullAgreementCounts counts)
    {
        var leftType = model.GetTypeSymbol(syntax.Left);
        var rightType = model.GetTypeSymbol(syntax.Right);
        var resultType = model.GetTypeSymbol(syntax);
        var leftForm = NullRepresentation.GetNullForm(leftType);
        var castsToResult = leftForm != NullForm.ArrayView &&
                            NullRepresentation.CoalesceCastsToResult(leftType, rightType, resultType);
        var leftView = leftForm == NullForm.ArrayView &&
                       StackArrayLocals.NeedsView(NullRepresentation.SkipNullPreservingConversions(syntax.Left, model),
                           model);

        var d = coalesce.Decided;
        if (d.LeftForm != leftForm || d.CastsToResult != castsToResult || d.LeftIsStackArrayView != leftView)
        {
            counts.Mismatches.Add(
                $"{where}: {syntax.Kind()} {syntax.Span}: Coalesce syntax ({leftForm},{castsToResult},{leftView}) " +
                $"IR ({d.LeftForm},{d.CastsToResult},{d.LeftIsStackArrayView})");
        }
    }

    // A form the IR leaves to the writer (Other) is right only where the legacy path writes neither a constant length
    // of a stack array (a direct local) nor a checked array member (any receiver that is not a stack array); a size
    // that is not a constant is left to the writer as well
    private static void CompareUndecidedMember(IrMemberForm form, ExpressionSyntax syntax, SemanticModel model,
        string where, IrNullAgreementCounts counts)
    {
        counts.AddExcluded($"MemberForm.{form}");
        if (form != IrMemberForm.Other || syntax is not MemberAccessExpressionSyntax memberSyntax)
        {
            return;
        }

        var sized = StackArrayLengthAccess.TryGetStackArraySize(memberSyntax, model, out var size);
        if ((sized && size.Count is not null) ||
            (!sized && model.GetSymbol(memberSyntax) is IPropertySymbol property &&
             ArrayNullCheck.IsArrayMember(property) &&
             !StackArrayLocals.IsStackArrayReference(memberSyntax.Expression, model)))
        {
            counts.Mismatches.Add($"{where}: {syntax.Kind()} {syntax.Span}: Member IR Other, legacy decides it");
        }
    }

    private static void CompareMember(IrPropertyReference member, ExpressionSyntax syntax, SemanticModel model,
        HashSet<ISymbol> frameSymbols, string where, IrNullAgreementCounts counts)
    {
        var d = member.Decided;
        if (d.Form is not (IrMemberForm.ArrayMember or IrMemberForm.StackArrayLength))
        {
            CompareUndecidedMember(d.Form, syntax, model, where, counts);
            return;
        }

        if (syntax is not MemberAccessExpressionSyntax memberSyntax ||
            model.GetSymbol(memberSyntax) is not IPropertySymbol propSymbol)
        {
            counts.AddExcluded("MemberNonMemberAccess");
            return;
        }

        Interlocked.Increment(ref counts.MemberCount);
        var label = $"{where}: {syntax.Kind()} {syntax.Span}";

        if (d.Form == IrMemberForm.StackArrayLength)
        {
            if (!StackArrayLengthAccess.TryGetStackArraySize(memberSyntax, model, out var size) ||
                size.Count != d.StackArrayLength)
            {
                counts.Mismatches.Add($"{label}: StackArrayLength IR={d.StackArrayLength}");
            }
            return;
        }

        if (!ArrayNullCheck.IsArrayMember(propSymbol))
        {
            counts.Mismatches.Add($"{label}: ArrayMember syntax false IR true");
            return;
        }

        // The syntax path tries the length of a stack array before the array member
        if (StackArrayLengthAccess.TryGetStackArraySize(memberSyntax, model, out _))
        {
            counts.Mismatches.Add($"{label}: ArrayMember IR, legacy writes the stack array size");
            return;
        }

        var legacyCheck = ArrayNullCheck.NeedsCheck(memberSyntax.Expression, model);
        var irCheck = d.ReceiverSite is not null;
        if (irCheck != legacyCheck)
        {
            if (!(!irCheck && legacyCheck &&
                  IrNullAgreementFrameTracker.TryRecordFrameVariable(memberSyntax.Expression, model, frameSymbols,
                      where, counts)))
            {
                counts.Mismatches.Add($"{label}: Member.ReceiverSite syntax {legacyCheck} IR {irCheck}");
            }
        }
        else if (irCheck && (d.ReceiverSite is not { } site || site.TrapKind != ArrayNullCheck.TrapKind ||
                             site.Message != ArrayNullCheck.SiteMessage ||
                             site.Source.Span != memberSyntax.Expression.Span))
        {
            counts.Mismatches.Add($"{label}: Member.ReceiverSite span mismatch IR {d.ReceiverSite}");
        }
    }
}
