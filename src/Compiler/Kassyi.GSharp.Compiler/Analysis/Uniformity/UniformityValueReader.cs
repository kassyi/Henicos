using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Reads the variance of values for a <see cref="UniformityAnalysis"/>: of an operation in a given state, of the result
/// and the <c>ref</c> / <c>out</c> arguments of a call (analyzing a device function for the variance of its arguments,
/// cached per argument mask), and what a call of the analyzed method gives its caller.
/// </summary>
internal sealed class UniformityValueReader(UniformityState state)
{
    private static readonly ConditionalWeakTable<Compilation,
            ConcurrentDictionary<IMethodSymbol, ConcurrentDictionary<ArgumentMasks, CalleeVariance>>>
        s_callees = new();

    /// <summary>The variance of the value of <paramref name="operation"/>; records the variance of each expression while observing.</summary>
    public ThreadVariance Evaluate(IOperation operation, ControlFlowGraph graph, ThreadVariance[] current)
    {
        var recorder = state.Recorder;
        var variance = operation switch
        {
            ILocalReferenceOperation local =>
                recorder.TryGetValue(local.Local, out var index) ? current[index] : ThreadVariance.Uniform,
            IParameterReferenceOperation parameter => ReadParameter(parameter.Parameter, current),
            IFlowCaptureReferenceOperation capture =>
                recorder.TryGetCapture(graph, capture.Id, out var captured) ? current[captured] : ThreadVariance.Uniform,
            IPropertyReferenceOperation { Property: var property }
                when SyncIntrinsicClassifier.IsThreadVariantSource(property) => ThreadVariance.Block,
            IPropertyReferenceOperation { Property: var property }
                when SyncIntrinsicClassifier.IsGridVariantSource(property) => ThreadVariance.Grid,
            IInvocationOperation invocation => EvaluateCall(invocation, graph, current),
            IFlowAnonymousFunctionOperation => ThreadVariance.Uniform,
            ISimpleAssignmentOperation assignment => Evaluate(assignment.Value, graph, current),
            ICompoundAssignmentOperation compound =>
                Evaluate(compound.Target, graph, current) | Evaluate(compound.Value, graph, current),
            _ => EvaluateOperands(operation, graph, current)
        };

        if (state.Observed is { } observed && operation.Syntax is ExpressionSyntax expression)
        {
            observed[expression] = observed.TryGetValue(expression, out var seen) ? seen | variance : variance;
        }

        return variance;
    }

    /// <summary>The variance of the operands of <paramref name="operation"/> together.</summary>
    public ThreadVariance EvaluateOperands(IOperation operation, ControlFlowGraph graph, ThreadVariance[] current)
    {
        var variance = ThreadVariance.Uniform;
        foreach (var child in operation.ChildOperations)
        {
            variance |= Evaluate(child, graph, current);
        }

        return variance;
    }

    /// <summary>A device function with a body in this compilation.</summary>
    public bool TryGetDeviceFunction(IMethodSymbol method, [NotNullWhen(true)] out IMethodSymbol? callee,
        [NotNullWhen(true)] out MethodDeclarationSyntax? declaration)
    {
        callee = (method.ReducedFrom ?? method).OriginalDefinition;
        if (SuspensionAnalysis.TryGetDeviceFunctionBody(callee, state.Model.Compilation, out declaration))
        {
            return true;
        }

        callee = null;
        return false;
    }

    /// <summary>What <paramref name="callee"/> gives the call <paramref name="invocation"/> for the variance of its arguments.</summary>
    public CalleeVariance GetCallVariance(IMethodSymbol callee, MethodDeclarationSyntax declaration,
        IInvocationOperation invocation, ControlFlowGraph graph, ThreadVariance[] current)
    {
        var masks = GetArgumentMasks(invocation, graph, current);
        return GetCalleeSummary(callee, declaration, masks with { Identity = 0 });
    }

    /// <summary>
    /// Which parameters receive a value that varies within a block, or between blocks only (the identity mask comes from
    /// the probe runs: <see cref="CertaintyProber.GetIdentityMask"/>).
    /// </summary>
    public ArgumentMasks GetArgumentMasks(IInvocationOperation invocation, ControlFlowGraph graph,
        ThreadVariance[] current)
    {
        ulong blockMask = 0;
        ulong gridMask = 0;
        foreach (var argument in invocation.Arguments)
        {
            var variance = Evaluate(argument.Value, graph, current);
            if (argument.Parameter is not { } parameter || variance == ThreadVariance.Uniform)
            {
                continue;
            }

            var bit = ArgumentMasks.Bit(parameter.Ordinal);
            if (variance == ThreadVariance.Block)
            {
                blockMask |= bit;
            }
            else
            {
                gridMask |= bit;
            }
        }

        return new ArgumentMasks(blockMask, gridMask, 0);
    }

    /// <summary>What a call of <paramref name="callee"/> with <paramref name="masks"/> gives its caller, cached per compilation.</summary>
    public CalleeVariance GetCalleeSummary(IMethodSymbol callee, MethodDeclarationSyntax declaration,
        ArgumentMasks masks)
    {
        var cache = s_callees.GetValue(state.Model.Compilation,
                static _ => new ConcurrentDictionary<IMethodSymbol,
                    ConcurrentDictionary<ArgumentMasks, CalleeVariance>>(SymbolEqualityComparer.Default))
            .GetOrAdd(callee, static _ => new ConcurrentDictionary<ArgumentMasks, CalleeVariance>());
        if (cache.TryGetValue(masks, out var summary))
        {
            return summary;
        }

        // [WHY] A recursive call adds nothing that the other calls of the cycle do not; it is left out, not cached
        if (!state.CalleesInProgress.Add(callee))
        {
            return default;
        }

        summary = AnalyzeCallee(callee, declaration, masks);
        state.CalleesInProgress.Remove(callee);
        return cache.GetOrAdd(masks, summary);
    }

    /// <summary>
    /// What a call of the analyzed method gives its caller: the variance of what it returns (a value returned under a
    /// thread-variant branch included) and of what it leaves in its ref and out parameters.
    /// </summary>
    public CalleeVariance Summarize()
    {
        var returned = ThreadVariance.Uniform;
        var current = new ThreadVariance[state.Recorder.ValueCount];
        for (var node = 0; node < state.NodeCount; node++)
        {
            if (state.Graph.Nodes[node] is { ReturnValue: { } value, Graph: { } graph })
            {
                state.Solver.RunNode(node, current);
                returned |= Evaluate(value, graph, current) | state.Region[node];
            }
        }

        var byReference = ThreadVariance.Uniform;
        foreach (var parameter in state.Method.Parameters)
        {
            if (parameter.RefKind is RefKind.Ref or RefKind.Out && state.Recorder.TryGetValue(parameter, out var index))
            {
                byReference |= state.In[state.Graph.Exit][index];
            }
        }

        var barrier = state.Barriers.SummarizeBarriers();
        return new CalleeVariance(returned, byReference, barrier.Divergence, barrier.Certain);
    }

    // A parameter of the analyzed method has a value from the start; one of a lambda or local function that is not
    // defined where it is spliced belongs to a body whose callers are not followed
    private ThreadVariance ReadParameter(IParameterSymbol parameter, ThreadVariance[] current) =>
        state.Recorder.TryGetValue(parameter, out var index) ? current[index] :
        parameter.ContainingSymbol.IsEqualTo(state.Method) ? ThreadVariance.Uniform : ThreadVariance.Block;

    // A device function returns what its summary says for the variance of its arguments; an intrinsic that returns a
    // thread-variant value is thread-variant; any other call is as variant as its operands
    private ThreadVariance EvaluateCall(IInvocationOperation invocation, ControlFlowGraph graph, ThreadVariance[] current)
    {
        var method = invocation.TargetMethod;
        if (TryGetDeviceFunction(method, out var callee, out var declaration))
        {
            return GetCallVariance(callee, declaration, invocation, graph, current).Return;
        }

        var operands = EvaluateOperands(invocation, graph, current);
        return !method.ReturnsVoid && SyncIntrinsicClassifier.Classify(method).ResultIsThreadVariant
            ? ThreadVariance.Block
            : operands;
    }

    private CalleeVariance AnalyzeCallee(IMethodSymbol callee, MethodDeclarationSyntax declaration,
        ArgumentMasks masks)
    {
        var model = state.Model.Compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        return MethodFlowGraph.TryCreate(model, declaration, out var graph)
            ? UniformityAnalysis.AnalyzeBody(graph, model, callee, masks, state.CalleesInProgress).Summarize()
            : default;
    }
}
