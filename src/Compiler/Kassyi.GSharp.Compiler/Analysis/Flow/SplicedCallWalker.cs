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

        WalkReceiverAndArguments(context, invocation, graph);
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

    // The receiver, then the arguments that the call reads
    private static void WalkReceiverAndArguments(SplicedFlowContext context, IInvocationOperation invocation,
        ControlFlowGraph graph)
    {
        if (invocation.Instance is { } instance)
        {
            SplicedOperationWalker.Walk(context, instance, graph);
        }

        foreach (var argument in invocation.Arguments)
        {
            if (!IsWrittenByCallee(argument))
            {
                SplicedOperationWalker.Walk(context, argument.Value, graph);
            }
        }
    }

    // M(out local), M(out parameter): the callee writes the variable when it returns (OnCallReturned) and reads nothing
    // of it; a ref local or ref parameter passed as out is a location, which is read
    internal static bool IsWrittenByCallee(IArgumentOperation argument) =>
        argument is
        {
            Parameter.RefKind: RefKind.Out,
            Value: ILocalReferenceOperation { Local.IsRef: false } or
            IParameterReferenceOperation { Parameter.RefKind: RefKind.None }
        };

    // Where a call splits the state machine; a barrier, or a call that reaches a block barrier (with the grid scope
    // when it reaches a grid barrier), is also a site of GSH0083
    private static SplitPoint? GetSplit(Compilation compilation, IInvocationOperation invocation,
        ControlFlowGraph graph)
    {
        if (TryGetImplicitUsingDispose(invocation, compilation, out var dispose, out var usingSyntax))
        {
            return GetMemberSplit(compilation, dispose, invocation.Arguments, invocation, graph, usingSyntax);
        }

        var intrinsic = SyncIntrinsicClassifier.Classify(invocation.TargetMethod);
        if (intrinsic.Kind is SyncIntrinsicKind.BlockBarrier or SyncIntrinsicKind.GridBarrier)
        {
            return new SplitPoint(SplitKind.Call, invocation.Syntax, intrinsic.Scope, null, graph, invocation);
        }

        return intrinsic.SplitsStateMachine
            ? new SplitPoint(SplitKind.Call, invocation.Syntax, null, null, graph, invocation)
            : GetSuspendingSplit(compilation, invocation, graph);
    }

    // Roslyn's ControlFlowGraph lowers `using` / `using var` Dispose() calls into Finally blocks as an
    // IInvocationOperation on the VariableDeclaratorSyntax or the UsingStatementSyntax expression, targeting
    // IDisposable.Dispose (or the ref-struct Dispose pattern). Map it back to the concrete Dispose method and the
    // enclosing using syntax so SplitsAt and Sites match DirectCallees.Of.
    private static bool TryGetImplicitUsingDispose(IInvocationOperation invocation, Compilation compilation,
        [NotNullWhen(true)] out IMethodSymbol? dispose, [NotNullWhen(true)] out SyntaxNode? usingSyntax)
    {
        usingSyntax = invocation.Syntax switch
        {
            VariableDeclaratorSyntax { Parent.Parent: (UsingStatementSyntax or LocalDeclarationStatementSyntax) and var owner } =>
                owner,
            var expr when invocation.TargetMethod is { Name: nameof(IDisposable.Dispose), Parameters.Length: 0, ReturnsVoid: true } =>
                GetEnclosingUsingStatement(expr),
            _ => null
        };

        if (usingSyntax == null)
        {
            dispose = null;
            return false;
        }

        if (invocation.Instance?.Type is INamedTypeSymbol resourceType &&
            UsingDisposeLookup.Find(resourceType, compilation) is { } concreteDispose)
        {
            dispose = concreteDispose;
            return true;
        }

        dispose = invocation.TargetMethod;
        return true;
    }

    private static UsingStatementSyntax? GetEnclosingUsingStatement(SyntaxNode expression)
    {
        var current = expression;
        while (current.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            current = parenthesized;
        }

        return current.Parent is UsingStatementSyntax usingStmt && usingStmt.Expression == current
            ? usingStmt
            : null;
    }

    // A call of a device function that suspends: a barrier site when it reaches a barrier
    private static SplitPoint? GetSuspendingSplit(Compilation compilation, IInvocationOperation invocation,
        ControlFlowGraph graph) =>
        GetMemberSplit(compilation, invocation.TargetMethod, invocation.Arguments, invocation, graph);

    internal static SplitPoint? GetMemberSplit(Compilation compilation, IMethodSymbol method,
        ImmutableArray<IArgumentOperation> arguments, IOperation operation, ControlFlowGraph graph,
        SyntaxNode? siteSyntax = null)
    {
        var syntax = siteSyntax ?? operation.Syntax;
        if (!SuspensionAnalysis.IsSuspendingFunction(method, compilation))
        {
            return null;
        }

        return SuspensionAnalysis.ReachesBlockBarrier(method, compilation)
            ? new SplitPoint(SplitKind.Call, syntax, SuspensionAnalysis.GetBarrierScope(method, compilation),
                new BarrierCallSite(method, arguments, operation, syntax), graph, operation, method)
            : new SplitPoint(SplitKind.Call, syntax, null, null, graph, operation, method);
    }

    internal static void RecordMethodSplitIfSuspending(SplicedFlowContext context, IMethodSymbol? method,
        IOperation operation, ControlFlowGraph graph, SyntaxNode? siteSyntax = null)
    {
        if (method != null &&
            GetMemberSplit(context.Model.Compilation, method, ImmutableArray<IArgumentOperation>.Empty, operation, graph,
                siteSyntax) is { } split)
        {
            context.Record((recorder, node) => recorder.OnSplit(split, node), context.Current.Index);
        }
    }

    // <arguments> <body>: the body where it is called, once for SyncGrid with a split in each gap, in a loop for
    // ForUnroll; a SyncGrid body with a gap is a grid barrier where it is called
    private static void WalkMacro(SplicedFlowContext context, IInvocationOperation invocation, GpuMacroCall macro,
        ControlFlowGraph graph)
    {
        var body = WalkMacroArguments(context, invocation, macro, graph);
        foreach (var recorder in context.Recorders)
        {
            recorder.OnCall(invocation, context.Current.Index);
        }

        var gaps = macro.Kind == GpuMacroKind.SyncGrid ? RecordSyncGridSplit(context, invocation, body, graph) : null;
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

    // The arguments other than the body, walked; the body lambda, when the call has one
    private static IFlowAnonymousFunctionOperation? WalkMacroArguments(SplicedFlowContext context,
        IInvocationOperation invocation, GpuMacroCall macro, ControlFlowGraph graph)
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

        return body;
    }

    // The gaps of a Gpu.SyncGrid(body); a body with a gap makes the call a grid barrier where it is called
    private static SyncGridGaps? RecordSyncGridSplit(SplicedFlowContext context, IInvocationOperation invocation,
        IFlowAnonymousFunctionOperation? body, ControlFlowGraph graph)
    {
        if (body?.Syntax is not LambdaExpressionSyntax syntax)
        {
            return null;
        }

        var gaps = new SyncGridGaps(SyncGridBody.Flatten(syntax, context.Model).Elements, context.Model);
        if (gaps.HasAnyGap)
        {
            var split = new SplitPoint(SplitKind.SyncGridCall, invocation.Syntax, SyncScope.Grid, null, graph);
            context.Record((recorder, node) => recorder.OnSplit(split, node), context.Current.Index);
        }

        return gaps;
    }

    private static IFlowAnonymousFunctionOperation? UnwrapLambda(IOperation operation) => operation switch
    {
        IFlowAnonymousFunctionOperation lambda => lambda,
        IDelegateCreationOperation creation => UnwrapLambda(creation.Target),
        IConversionOperation conversion => UnwrapLambda(conversion.Operand),
        _ => null
    };
}
