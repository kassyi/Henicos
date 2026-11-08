using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// How <see cref="SplicedFlowBuilder"/> splices a body into the graph where it runs: a lambda where it is created, a
/// local function where it is called or converted to a delegate, and the body of a <c>Gpu.ForUnroll</c> or
/// <c>Gpu.SyncGrid(body)</c> call.
/// </summary>
internal static class SplicedBodySplicer
{
    /// <summary>A lambda where it is created.</summary>
    public static void SpliceLambda(SplicedFlowContext context, IFlowAnonymousFunctionOperation function,
        ControlFlowGraph graph)
    {
        var body = new SplicedBody(SplicedBodyKind.Lambda, function.Syntax, function.Symbol.Parameters);
        SpliceGraph(context, body, graph.GetAnonymousFunctionControlFlowGraphInScope(function), gaps: null,
            isLoop: false);
    }

    /// <summary>
    /// A local function where it is called (with the call) or converted to a delegate, once on each path (a recursive
    /// call adds nothing).
    /// </summary>
    public static void SpliceLocalFunction(SplicedFlowContext context, IMethodSymbol function, ControlFlowGraph graph,
        SplicedBodyKind kind, IInvocationOperation? call = null)
    {
        if (!function.TryGetDeclaringSyntax(out LocalFunctionStatementSyntax? syntax) ||
            !context.LocalFunctionsInProgress.Add(function))
        {
            return;
        }

        var body = new SplicedBody(
            kind,
            syntax,
            function.Parameters,
            Call: call,
            CallGraph: call != null ? graph : null
        );
        SpliceGraph(context, body, graph.GetLocalFunctionControlFlowGraphInScope(function), gaps: null,
            isLoop: false);
        context.LocalFunctionsInProgress.Remove(function);
    }

    /// <summary>before -> body -> after, and after the body back to its entry for a loop.</summary>
    public static void SpliceGraph(SplicedFlowContext context, SplicedBody body, ControlFlowGraph bodyGraph,
        SyncGridGaps? gaps, bool isLoop)
    {
        var before = context.Current;
        var range = context.TopRange;
        foreach (var recorder in context.Recorders)
        {
            recorder.OnBodyEntered(body, before.Index);
        }

        var (entry, exit) = SplicedFlowBuilder.BuildGraph(context, bodyGraph, gaps, isRoot: false);
        var after = context.NewNode();
        after.First = range;
        after.Last = range;
        SplicedFlowContext.Connect(before, entry);
        SplicedFlowContext.Connect(exit, after);
        if (isLoop)
        {
            SplicedFlowContext.Connect(exit, entry);
        }

        context.Current = after;
        foreach (var recorder in context.Recorders)
        {
            recorder.OnBodyExited(body, after.Index);
        }
    }
}
