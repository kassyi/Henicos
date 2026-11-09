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
    public static void PlaceAroundSplit(LivenessEventLog log, SemanticModel model, IInvocationOperation call, int node)
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
                if (IsInSpilledLeftOperand(model, call.Syntax, syntax))
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
    // which the lowering evaluates before the split (SpilledArguments.IsSpilledLeftOperand, #1535). Not covered: a
    // split in an arm of a ?: / && / || inside the right operand, which ConditionalEvaluationRewriter rewrites first, so
    // the left operand still runs after it there; that order is the open part of #991 (Suspending call arguments still
    // misordered on CPU)
    private static bool IsInSpilledLeftOperand(SemanticModel model, SyntaxNode call, SyntaxNode use)
    {
        if (use.SyntaxTree != call.SyntaxTree)
        {
            return false;
        }

        for (var node = call.Parent; node is ExpressionSyntax or ArgumentSyntax; node = node.Parent)
        {
            if (node is BinaryExpressionSyntax binary &&
                binary.Right.Span.Contains(call.Span) &&
                binary.Left.Span.Contains(use.Span))
            {
                return SpilledArguments.IsSpilledLeftOperand(binary, model);
            }
        }

        return false;
    }

    // Whether a syntax use inside call is evaluated once before the call splits via SpilledArguments
    private static bool IsEvaluatedBeforeSplit(SemanticModel model, IInvocationOperation call, SyntaxNode syntax)
    {
        if (call.Syntax is not InvocationExpressionSyntax invocation)
        {
            return true;
        }

        var arguments = invocation.ArgumentList.Arguments;
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
