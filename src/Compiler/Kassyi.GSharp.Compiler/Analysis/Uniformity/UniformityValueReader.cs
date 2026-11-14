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
    private static readonly
        PerCompilationSymbolCache<IMethodSymbol, ConcurrentDictionary<ArgumentMasks, CalleeVariance>> s_callees = new();

    /// <summary>The variance of the value of <paramref name="operation"/>; records the variance of each expression while observing.</summary>
    public ThreadVariance Evaluate(IOperation operation, ControlFlowGraph graph, ThreadVariance[] current)
    {
        var recorder = state.Recorder;
        var variance = operation switch
        {
            ILocalReferenceOperation { Local.IsRef: true } refLocal =>
                (recorder.TryGetValue(refLocal.Local, out var refIndex) ? current[refIndex] : ThreadVariance.Uniform) |
                recorder.Memory.Read(refLocal, current),
            ILocalReferenceOperation local =>
                recorder.TryGetValue(local.Local, out var index) ? current[index] : ThreadVariance.Uniform,
            IParameterReferenceOperation parameter => ReadParameter(parameter.Parameter, current),
            IFlowCaptureReferenceOperation capture =>
                recorder.TryGetCapture(graph, capture.Id, out var captured) ? current[captured] : ThreadVariance.Uniform,
            IPropertyReferenceOperation { Property: var property }
                when SyncIntrinsicClassifier.IsThreadVariantSource(property) => ThreadVariance.Block,
            IPropertyReferenceOperation { Property: var property }
                when SyncIntrinsicClassifier.IsBlockVariantSource(property) => ThreadVariance.Grid,
            IInvocationOperation { TargetMethod.ReturnsByRef: true } refCall =>
                EvaluateCall(refCall, graph, current) | recorder.Memory.Read(refCall, current),
            IInvocationOperation invocation => EvaluateCall(invocation, graph, current),
            IFlowAnonymousFunctionOperation => ThreadVariance.Uniform,
            IInstanceReferenceOperation { Type: { } self } =>
                recorder.TryGetValue(self, out var thisIndex) ? current[thisIndex] : ThreadVariance.Uniform,
            IPropertyReferenceOperation { Property.GetMethod: { } getter } read
                when TryGetSummarizedCallee(getter, out var callee, out var declaration) =>
                EvaluateGetter(read, callee, declaration, graph, current),
            IObjectCreationOperation { Constructor: { } constructor } creation
                when TryGetSummarizedCallee(constructor, out var constructorCallee, out var constructorDeclaration) =>
                GetCalleeSummary(constructorCallee, constructorDeclaration,
                    GetArgumentMasks(creation.Arguments, graph, current)).This,
            IBinaryOperation { OperatorMethod: { } binaryOperator } binary =>
                EvaluateOperator(binary, binaryOperator, binary.LeftOperand, binary.RightOperand, graph, current),
            IUnaryOperation { OperatorMethod: { } unaryOperator } unary =>
                EvaluateOperator(unary, unaryOperator, unary.Operand, null, graph, current),
            IConversionOperation { OperatorMethod: { } conversionOperator } conversion =>
                EvaluateOperator(conversion, conversionOperator, conversion.Operand, null, graph, current),
            IIncrementOrDecrementOperation { OperatorMethod: { } incrementOperator } increment =>
                EvaluateOperator(increment, incrementOperator, increment.Target, null, graph, current),
            ICompoundAssignmentOperation { OperatorMethod: { } compoundOperator } compoundCall =>
                EvaluateOperator(compoundCall, compoundOperator, compoundCall.Target, compoundCall.Value, graph,
                    current),
            IArrayElementReferenceOperation or IPropertyReferenceOperation { Property.IsIndexer: true } =>
                EvaluateOperands(operation, graph, current) | recorder.Memory.Read(operation, current),
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

    /// <summary>
    /// A function with a body in this compilation whose calls are summarized: a <c>[GpuDeviceFunction]</c>, or a
    /// method, property getter, operator, conversion or constructor of a struct that G# defines on the device
    /// (<paramref name="declaration"/> is then its declaration, or the expression body of the property).
    /// </summary>
    public bool TryGetSummarizedCallee(IMethodSymbol method, [NotNullWhen(true)] out IMethodSymbol? callee,
        [NotNullWhen(true)] out SyntaxNode? declaration)
    {
        callee = (method.ReducedFrom ?? method).OriginalDefinition;
        var compilation = state.Model.Compilation;
        if (SuspensionAnalysis.TryGetDeviceFunctionBody(callee, compilation, out var deviceFunction))
        {
            declaration = deviceFunction;
            return true;
        }

        if (callee is
            {
                MethodKind: MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.UserDefinedOperator
                or MethodKind.Conversion or MethodKind.Constructor,
                ContainingType: { } type
            } &&
            GlobalCppNames.IsEmittedUserType(type) &&
            callee.TryGetDeclaringSyntax(out declaration) &&
            compilation.ContainsSyntaxTree(declaration.SyntaxTree) &&
            HasBody(declaration))
        {
            return true;
        }

        callee = null;
        declaration = null;
        return false;
    }

    /// <summary>What <paramref name="callee"/> gives the call <paramref name="invocation"/> for the variance of its arguments.</summary>
    public CalleeVariance GetCallVariance(IMethodSymbol callee, SyntaxNode declaration,
        IInvocationOperation invocation, ControlFlowGraph graph, ThreadVariance[] current)
    {
        var masks = GetArgumentMasks(invocation.Arguments, graph, current);
        return GetCalleeSummary(callee, declaration, masks);
    }

    /// <summary>Which parameters receive a value that varies within a block, or between blocks only.</summary>
    public ArgumentMasks GetArgumentMasks(ImmutableArray<IArgumentOperation> arguments, ControlFlowGraph graph,
        ThreadVariance[] current)
    {
        ulong blockMask = 0;
        ulong gridMask = 0;
        foreach (var argument in arguments)
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

        return new ArgumentMasks(blockMask, gridMask);
    }

    /// <summary>What a call of <paramref name="callee"/> with <paramref name="masks"/> gives its caller, cached per compilation.</summary>
    public CalleeVariance GetCalleeSummary(IMethodSymbol callee, SyntaxNode declaration, ArgumentMasks masks)
    {
        var cache = s_callees.For(state.Model.Compilation)
            .GetOrAdd(callee, static _ => new ConcurrentDictionary<ArgumentMasks, CalleeVariance>());
        if (cache.TryGetValue(masks, out var summary))
        {
            return summary;
        }

        // [WHY] A recursive call with the same argument masks adds nothing that the other calls of the cycle do not; it is
        // left out, not cached (one with other masks may make other values thread-variant, so it is analyzed). A
        // summary computed on the way into such a call took it as uniform, so it depends on where the cycle was entered:
        // it is not cached either, and the analyses that use it are marked the same way up to the cycle's entry
        var key = new CalleeCall(callee, masks);
        if (!state.CalleesInProgress.Add(key))
        {
            state.MetCalleeInProgress = true;
            return default;
        }

        summary = AnalyzeCallee(callee, declaration, masks, out var metCalleeInProgress);
        state.CalleesInProgress.Remove(key);
        if (metCalleeInProgress)
        {
            state.MetCalleeInProgress = true;
            return summary;
        }

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

        var self = state.Recorder.TryGetValue(state.Method.ContainingType, out var thisIndex)
            ? state.In[state.Graph.Exit][thisIndex]
            : ThreadVariance.Uniform;
        var (memory, fieldMemory, barrierOnEveryPath) =
            state.Recorder.Memory.Summarize(state.Method, state.In[state.Graph.Exit]);
        return new CalleeVariance(returned, byReference, state.Barriers.SummarizeBarriers(), memory, fieldMemory,
            barrierOnEveryPath, self);
    }

    // A parameter of the analyzed method has a value from the start; one of a lambda or local function that is not
    // defined where it is spliced belongs to a body whose callers are not followed
    private ThreadVariance ReadParameter(IParameterSymbol parameter, ThreadVariance[] current) =>
        state.Recorder.TryGetValue(parameter, out var index) ? current[index] :
        parameter.ContainingSymbol.IsEqualTo(state.Method) ? ThreadVariance.Uniform : ThreadVariance.Block;

    /// <summary>
    /// Whether a call that is neither a device function with a body nor a local function may give each thread a
    /// different result or <c>ref</c> / <c>out</c> argument whatever its operands: an intrinsic that returns a
    /// thread-variant value, or a call through a delegate, whose target body is not followed.
    /// </summary>
    public static bool IsThreadVariantCall(IMethodSymbol method) =>
        method.MethodKind == MethodKind.DelegateInvoke ||
        SyncIntrinsicClassifier.Classify(method).ResultIsThreadVariant;

    // A device function returns what its summary says for the variance of its arguments, and as much as its receiver
    // varies; a local function what its body, spliced at the call, returned (anything for a call whose body is not
    // spliced, a recursive one); an intrinsic that returns a thread-variant value and a call through a delegate are
    // thread-variant; any other call is as variant as its operands
    private ThreadVariance EvaluateCall(IInvocationOperation invocation, ControlFlowGraph graph, ThreadVariance[] current)
    {
        var method = invocation.TargetMethod;
        if (method.MethodKind == MethodKind.LocalFunction)
        {
            return state.Recorder.TryGetResult(invocation.Syntax, out var result)
                ? current[result]
                : ThreadVariance.Block;
        }

        if (TryGetSummarizedCallee(method, out var callee, out var declaration))
        {
            return GetCallVariance(callee, declaration, invocation, graph, current).Return |
                   EvaluateReceiver(invocation.Instance, graph, current);
        }

        var operands = EvaluateOperands(invocation, graph, current);
        return !method.ReturnsVoid && IsThreadVariantCall(method)
            ? ThreadVariance.Block
            : operands;
    }

    // A method, operator, conversion, constructor or accessor with a block or expression body, or the expression body
    // of a property or indexer
    private static bool HasBody(SyntaxNode declaration) => declaration switch
    {
        BaseMethodDeclarationSyntax method => method.Body != null || method.ExpressionBody != null,
        AccessorDeclarationSyntax accessor => accessor.Body != null || accessor.ExpressionBody != null,
        _ => declaration is ArrowExpressionClauseSyntax
    };

    // A user-defined operator or conversion with a body returns what its summary says for the variance of its operands
    // (by ordinal); any other is as variant as its operands
    private ThreadVariance EvaluateOperator(IOperation operation, IMethodSymbol method, IOperation first,
        IOperation? second, ControlFlowGraph graph, ThreadVariance[] current)
    {
        if (!TryGetSummarizedCallee(method, out var callee, out var declaration))
        {
            return EvaluateOperands(operation, graph, current);
        }

        ulong blockMask = 0;
        ulong gridMask = 0;
        AddOperandMask(Evaluate(first, graph, current), 0, ref blockMask, ref gridMask);
        if (second != null)
        {
            AddOperandMask(Evaluate(second, graph, current), 1, ref blockMask, ref gridMask);
        }

        return GetCalleeSummary(callee, declaration, new ArgumentMasks(blockMask, gridMask)).Return;
    }

    private static void AddOperandMask(ThreadVariance variance, int ordinal, ref ulong blockMask, ref ulong gridMask)
    {
        if (variance == ThreadVariance.Block)
        {
            blockMask |= ArgumentMasks.Bit(ordinal);
        }
        else if (variance == ThreadVariance.Grid)
        {
            gridMask |= ArgumentMasks.Bit(ordinal);
        }
    }

    // A getter with a body returns what its body returns for the variance of the indexer arguments; an indexer also
    // reads the memory it is an element of
    private ThreadVariance EvaluateGetter(IPropertyReferenceOperation read, IMethodSymbol getter,
        SyntaxNode declaration, ControlFlowGraph graph, ThreadVariance[] current)
    {
        var masks = GetArgumentMasks(read.Arguments, graph, current);
        var variance = GetCalleeSummary(getter, declaration, masks).Return |
                       EvaluateReceiver(read.Instance, graph, current);
        return read.Property.IsIndexer ? variance | state.Recorder.Memory.Read(read, current) : variance;
    }

    // The body of an instance member reads the fields of this as uniform: the member returns as much as its receiver
    // varies
    private ThreadVariance EvaluateReceiver(IOperation? receiver, ControlFlowGraph graph, ThreadVariance[] current) =>
        receiver == null ? ThreadVariance.Uniform : Evaluate(receiver, graph, current);

    private CalleeVariance AnalyzeCallee(IMethodSymbol callee, SyntaxNode declaration,
        ArgumentMasks masks, out bool metCalleeInProgress)
    {
        metCalleeInProgress = false;
        var model = state.Model.Compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        if (!MethodFlowGraph.TryCreate(model, declaration, out var graph))
        {
            return default;
        }

        var analysis = UniformityAnalysis.AnalyzeBody(graph, model, callee, masks, state.CalleesInProgress);
        var summary = analysis.Summarize();
        metCalleeInProgress = analysis.MetCalleeInProgress;
        return summary;
    }
}
