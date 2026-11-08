using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The walk of <see cref="SplicedFlowBuilder"/> over calls: the receiver and arguments, the local function body, the
/// split, then the <c>ref</c> and <c>out</c> arguments written; and the <c>Gpu.ForUnroll</c> and
/// <c>Gpu.SyncGrid(body)</c> calls that expand their body.
/// </summary>
internal static class SplicedCallWalker
{
    // <receiver> <arguments> <local function body> <split> <ref and out arguments written>
    public static void WalkInvocation(SplicedFlowContext context, IInvocationOperation invocation,
        ControlFlowGraph graph)
    {
        var method = invocation.TargetMethod;
        var macro = GpuMacroClassifier.Classify(method);
        if (macro is { Kind: GpuMacroKind.ForUnroll or GpuMacroKind.SyncGrid, HasBody: true })
        {
            WalkMacro(context, invocation, macro, graph);
            return;
        }

        if (invocation.Instance is { } instance)
        {
            SplicedOperationWalker.Walk(context, instance, graph);
        }

        foreach (var argument in invocation.Arguments)
        {
            // M(out local): the callee writes the local when it returns (OnCallReturned)
            if (argument is not
                { Parameter.RefKind: RefKind.Out, Value: ILocalReferenceOperation { Local.IsRef: false } })
            {
                SplicedOperationWalker.Walk(context, argument.Value, graph);
            }
        }

        if (method.MethodKind == MethodKind.LocalFunction)
        {
            SplicedBodySplicer.SpliceLocalFunction(context, method.OriginalDefinition, graph,
                SplicedBodyKind.LocalFunctionCall, invocation);
        }

        foreach (var recorder in context.Recorders)
        {
            recorder.OnCall(invocation, context.Current.Index);
        }

        if (GetSplit(context.Model.Compilation, invocation, graph) is { } split)
        {
            context.Record((recorder, node) => recorder.OnSplit(split, node), context.Current.Index);
        }

        foreach (var recorder in context.Recorders)
        {
            recorder.OnCallReturned(invocation, graph, context.Current.Index);
        }
    }

    // Where a call splits the state machine; a barrier, or a call that reaches a block barrier (with the grid scope
    // when it reaches a grid barrier), is also a site of GSH0083
    private static SplitPoint? GetSplit(Compilation compilation, IInvocationOperation invocation,
        ControlFlowGraph graph)
    {
        var method = invocation.TargetMethod;
        var intrinsic = SyncIntrinsicClassifier.Classify(method);
        if (intrinsic.Kind is SyncIntrinsicKind.BlockBarrier or SyncIntrinsicKind.GridBarrier)
        {
            return new SplitPoint(SplitKind.Call, invocation.Syntax, intrinsic.Scope, null, graph);
        }

        if (intrinsic.SplitsStateMachine)
        {
            return new SplitPoint(SplitKind.Call, invocation.Syntax, null, null, graph);
        }

        if (!SuspensionAnalysis.IsSuspendingFunction(method, compilation))
        {
            return null;
        }

        return SuspensionAnalysis.ReachesBlockBarrier(method, compilation)
            ? new SplitPoint(SplitKind.Call, invocation.Syntax, SuspensionAnalysis.GetBarrierScope(method, compilation),
                invocation, graph)
            : new SplitPoint(SplitKind.Call, invocation.Syntax, null, null, graph);
    }

    // <arguments> <body>: the body where it is called, once for SyncGrid with a split in each gap, in a loop for
    // ForUnroll; a SyncGrid body with a gap is a grid barrier where it is called
    private static void WalkMacro(SplicedFlowContext context, IInvocationOperation invocation, GpuMacroCall macro,
        ControlFlowGraph graph)
    {
        IFlowAnonymousFunctionOperation? body = null;
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == macro.BodyOrdinal && UnwrapLambda(argument.Value) is { } lambda)
            {
                body = lambda;
                continue;
            }

            SplicedOperationWalker.Walk(context, argument.Value, graph);
        }

        foreach (var recorder in context.Recorders)
        {
            recorder.OnCall(invocation, context.Current.Index);
        }

        var gaps = macro.Kind == GpuMacroKind.SyncGrid && body?.Syntax is LambdaExpressionSyntax syntax
            ? new SyncGridGaps(SyncGridBody.Flatten(syntax, context.Model).Elements, context.Model)
            : null;
        if (gaps is { HasAnyGap: true })
        {
            var split = new SplitPoint(SplitKind.SyncGridCall, invocation.Syntax, SyncScope.Grid, null, graph);
            context.Record((recorder, node) => recorder.OnSplit(split, node), context.Current.Index);
        }

        if (body == null)
        {
            return;
        }

        var isLoop = macro.Kind == GpuMacroKind.ForUnroll;
        var spliced = new SplicedBody(SplicedBodyKind.Macro, body.Syntax, body.Symbol.Parameters,
            HasUniformIndex: isLoop);
        SplicedBodySplicer.SpliceGraph(context, spliced, graph.GetAnonymousFunctionControlFlowGraphInScope(body), gaps,
            isLoop);
    }

    private static IFlowAnonymousFunctionOperation? UnwrapLambda(IOperation operation) => operation switch
    {
        IFlowAnonymousFunctionOperation lambda => lambda,
        IDelegateCreationOperation creation => UnwrapLambda(creation.Target),
        IConversionOperation conversion => UnwrapLambda(conversion.Operand),
        _ => null
    };
}
