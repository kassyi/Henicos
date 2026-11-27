using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Where <see cref="LivenessRecorder"/> places the events of a statement around a call that splits: where the lowered
/// code runs them, not where C# evaluates them.
/// </summary>
internal static class LivenessSplitPlacement
{
    /// <summary>
    /// The events of the statement inside <paramref name="call"/> go to <paramref name="node"/>: the uses evaluated
    /// once before the call (a spilled argument) and the changes before the split, the other uses inside the call
    /// after it; the events outside the call stay in the statement.
    /// </summary>
    public static void PlaceAroundSplit(LivenessEventLog log, SemanticModel model, IOperation call,
        IMethodSymbol? target, int node)
    {
        var statement = log.Statement;
        var events = log.EventsOf(node);
        var after = ImmutableArray.CreateBuilder<LivenessEvent>();
        var kept = 0;
        for (var i = 0; i < statement.Count; i++)
        {
            var (flowEvent, syntax) = statement[i];
            if (syntax.SyntaxTree != call.Syntax.SyntaxTree || !call.Syntax.Span.Contains(syntax.Span))
            {
                if (IsEvaluatedOutsideBeforeSplit(model, call.Syntax, target, syntax))
                {
                    events.Add(flowEvent);
                }
                else
                {
                    statement[kept++] = (flowEvent, syntax);
                }
            }
            else if (flowEvent.Kind == LivenessEventKind.Change || IsEvaluatedBeforeSplit(model, call, syntax))
            {
                events.Add(flowEvent);
            }
            else
            {
                after.Add(flowEvent);
            }
        }

        statement.Count = kept;
        events.Add(LivenessEvent.Split);
        events.AddRange(after);
    }

    // Whether a use outside the call is in the left operand of a binary expression whose right operand holds the call,
    // which the lowering evaluates before the split (SpilledArguments.IsSpilledLeftOperand, #1535), or on the right side
    // of an assignment whose left side calls a property or indexer setter (which evaluates the right side before
    // invoking the setter). Not covered: a split in an arm of a ?: / && / || inside the right operand, which
    // ConditionalEvaluationRewriter rewrites first, so the left operand still runs after it there; that order is the open
    // part of #991 (Suspending call arguments still misordered on CPU)
    private static bool IsEvaluatedOutsideBeforeSplit(SemanticModel model, SyntaxNode call, IMethodSymbol? target,
        SyntaxNode use)
    {
        if (use.SyntaxTree != call.SyntaxTree)
        {
            return false;
        }

        var isSetter = target is { MethodKind: MethodKind.PropertySet } ||
                       (target?.AssociatedSymbol is IPropertySymbol { SetMethod: { } setter } &&
                        target.IsEqualTo(setter));
        for (var node = call.Parent; node is ExpressionSyntax or ArgumentSyntax; node = node.Parent)
        {
            if (node is BinaryExpressionSyntax binary &&
                binary.Right.Span.Contains(call.Span) &&
                binary.Left.Span.Contains(use.Span))
            {
                return SpilledArguments.IsSpilledLeftOperand(binary, model);
            }

            if (isSetter &&
                node is AssignmentExpressionSyntax assignment &&
                assignment.Left.Span.Contains(call.Span) &&
                assignment.Right.Span.Contains(use.Span))
            {
                return assignment.Right.UnwrapParentheses() is not IdentifierNameSyntax identifier ||
                       model.GetTypeSymbol(identifier) is not { IsRefLikeType: true };
            }
        }

        return false;
    }

    // Whether a syntax use inside call is evaluated once before the call splits via SpilledArguments
    private static bool IsEvaluatedBeforeSplit(SemanticModel model, IOperation call, SyntaxNode syntax)
    {
        var arguments = call.Syntax switch
        {
            InvocationExpressionSyntax invocation => invocation.ArgumentList.Arguments,
            BaseObjectCreationExpressionSyntax { ArgumentList: { } argumentList } => argumentList.Arguments,
            _ => default
        };
        if (arguments.Count == 0 && call.Syntax is not (InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax))
        {
            return true;
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Span.Contains(syntax.Span))
            {
                return SpilledArguments.IsPassedByReference(arguments[i], model)
                    ? SpilledArguments.IsInSpilledLocationPart(arguments[i].Expression, syntax, arguments, i, model)
                    : SpilledArguments.IsSpilledArgument(arguments, i, model);
            }
        }

        return false;
    }
}
