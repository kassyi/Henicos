using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Solves the variance of the values and the influence regions of the branches of a <see cref="UniformityAnalysis"/>:
/// the forward data flow and the regions, alternately until neither grows, and what each write does to the state.
/// </summary>
internal sealed class UniformitySolver(UniformityState state)
{
    // The influence regions of the branches
    private readonly UniformityRegions _regions = new(state);

    // The values fixed as uniform during EvaluateWithUniformParameters
    private HashSet<int>? _uniformValues;

    /// <summary>The forward data flow and the influence regions, alternately until the regions stop growing; both only grow.</summary>
    public void Solve()
    {
        var count = state.NodeCount;
        var values = state.Recorder.ValueCount;
        state.In = new ThreadVariance[count][];
        for (var i = 0; i < count; i++)
        {
            state.In[i] = new ThreadVariance[values];
        }

        state.Region = new ThreadVariance[count];
        state.Branch = new ThreadVariance[count];
        _regions.Reset(count);

        var current = new ThreadVariance[values];
        do
        {
            Propagate(current);
        } while (_regions.Update(this, current));
    }

    /// <summary>The variance of every value at the end of <paramref name="node"/>, into <paramref name="current"/>.</summary>
    public void RunNode(int node, ThreadVariance[] current)
    {
        state.In[node].AsSpan().CopyTo(current);
        foreach (var flowEvent in state.NodeAt(node).Events)
        {
            switch (flowEvent)
            {
                case { Kind: UniformityEventKind.Define }:
                    current[flowEvent.Value] = _uniformValues != null && _uniformValues.Contains(flowEvent.Value)
                        ? ThreadVariance.Uniform
                        : flowEvent.Variance;
                    break;
                case { Kind: UniformityEventKind.Bind, Operation: { } bound, Graph: { } boundGraph }:
                    current[flowEvent.Value] = state.Reader.Evaluate(bound, boundGraph, current);
                    break;
                case
                {
                    Kind: UniformityEventKind.BindLambdaResult, Operation: { } returned, Graph: { } returnedGraph,
                    Opaque: { } lambda
                }:
                    current[flowEvent.Value] = state.Reader.EvaluateWithUniformParameters(
                        state.Recorder.GetLambdaParameters(lambda),
                        returned,
                        returnedGraph,
                        current
                    );
                    break;
                case { Kind: UniformityEventKind.Write, Operation: { } operation, Graph: { } graph }:
                    ApplyWrite(operation, graph, current, flowEvent.Opaque);
                    break;
                case { Kind: UniformityEventKind.Observe, Operation: { } observed, Graph: { } observedGraph }
                    when state.Observed != null:
                    state.Reader.Evaluate(observed, observedGraph, current);
                    break;
                case { Kind: UniformityEventKind.Site, Operation: { } callOperation, Graph: { } callGraph }
                    when state.SiteMasks != null:
                    state.Barriers.RecordSiteMasks(callOperation, callGraph, current,
                        flowEvent.Site ?? callOperation.Syntax);
                    break;
                case { Kind: UniformityEventKind.MemoryWrite or UniformityEventKind.MemorySync or UniformityEventKind.MemoryCall }:
                    state.Recorder.Memory.Apply(flowEvent, current, state.Reader, state.Model.Compilation);
                    break;
            }
        }
    }

    /// <summary>Records the variance of every expression the graph evaluates, from the solved state.</summary>
    public void Observe()
    {
        state.Observed = new Dictionary<SyntaxNode, ThreadVariance>();
        var current = new ThreadVariance[state.Recorder.ValueCount];
        for (var node = 0; node < state.NodeCount; node++)
        {
            RunNode(node, current);
            if (state.Graph.Nodes[node] is { Condition: { } condition, Graph: { } graph })
            {
                state.Reader.Evaluate(condition, graph, current);
            }
        }
    }

    /// <summary>The observed variance of <paramref name="expression"/>, unwrapping parentheses.</summary>
    public ThreadVariance GetObservedVariance(SyntaxNode expression)
    {
        if (state.Observed == null)
        {
            Observe();
        }

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.UnwrapParentheses();
        }

        if (state.Observed != null && state.Observed.TryGetValue(expression, out var variance))
        {
            return variance;
        }

        var combined = ThreadVariance.Uniform;
        if (state.Observed != null)
        {
            foreach (var node in expression.DescendantNodes())
            {
                if (state.Observed.TryGetValue(node, out var childVariance))
                {
                    combined |= childVariance;
                }
            }
        }

        return combined;
    }

    /// <summary>
    /// The variance of <paramref name="expression"/> when <paramref name="uniformParameters"/> are taken as uniform.
    /// </summary>
    public ThreadVariance EvaluateWithUniformParameters(SyntaxNode expression,
        ImmutableArray<IParameterSymbol> uniformParameters)
    {
        var uniformValues = new HashSet<int>();
        foreach (var parameter in uniformParameters)
        {
            if (state.Recorder.TryGetValue(parameter, out var index))
            {
                uniformValues.Add(index);
            }
        }

        var (savedIn, savedRegion, savedBranch, savedObserved) = (state.In, state.Region, state.Branch, state.Observed);
        _uniformValues = uniformValues;
        try
        {
            Solve();
            Observe();
            return GetObservedVariance(expression);
        }
        finally
        {
            _uniformValues = null;
            (state.In, state.Region, state.Branch, state.Observed) = (savedIn, savedRegion, savedBranch, savedObserved);
        }
    }

    // A member of a flow capture written: the capture, and the locals a ref conditional lets it refer to, keep their
    // other members, so the write only adds to them
    private void AddToCaptureRoot(IFlowCaptureReferenceOperation capture, ThreadVariance variance,
        ThreadVariance[] current)
    {
        var recorder = state.Recorder;
        if (recorder.TryGetCaptureRoot(capture, out var captured))
        {
            current[captured] |= variance;
        }

        foreach (var alias in recorder.GetCaptureRootAliases(capture))
        {
            if (recorder.TryGetValue(alias, out var index))
            {
                current[index] |= variance;
            }
        }
    }

    /// <summary>Whether <paramref name="symbol"/> is declared inside <paramref name="scope"/>.</summary>
    public static bool IsDeclaredIn(ISymbol symbol, SyntaxNode scope) =>
        symbol.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(scope, static (reference, s) =>
                reference.SyntaxTree == s.SyntaxTree && s.Span.Contains(reference.Span)
            );

    private void Propagate(ThreadVariance[] current)
    {
        bool changed;
        do
        {
            changed = false;
            for (var node = 0; node < state.NodeCount; node++)
            {
                RunNode(node, current);
                foreach (var successor in state.Successors[node])
                {
                    changed |= Merge(state.In[successor], current);
                }
            }
        } while (changed);
    }

    // target |= values; whether target changed
    private static bool Merge(ThreadVariance[] target, ThreadVariance[] values)
    {
        var changed = false;
        for (var value = 0; value < values.Length; value++)
        {
            var merged = target[value] | values[value];
            if (merged != target[value])
            {
                target[value] = merged;
                changed = true;
            }
        }

        return changed;
    }

    private void ApplyWrite(IOperation operation, ControlFlowGraph graph, ThreadVariance[] current, SyntaxNode? opaque)
    {
        switch (operation)
        {
            case IFlowCaptureOperation capture: WriteCapture(capture, graph, current); break;
            case IDeconstructionAssignmentOperation deconstruction:
                Deconstruct(deconstruction.Target, deconstruction.Value, graph, current, opaque); break;
            case ISimpleAssignmentOperation assignment: WriteSimple(assignment, graph, current, opaque); break;
            case ICompoundAssignmentOperation compound: WriteCompound(compound, graph, current, opaque); break;
            case IIncrementOrDecrementOperation increment: WriteIncrement(increment, graph, current, opaque); break;
            case IInvocationOperation invocation: WriteArguments(invocation, graph, current, opaque); break;
            case IObjectCreationOperation creation: WriteCreationArguments(creation, graph, current, opaque); break;
        }
    }

    private void WriteCapture(IFlowCaptureOperation capture, ControlFlowGraph graph, ThreadVariance[] current) =>
        current[state.Recorder.ValueOf(graph, capture.Id)] = state.Reader.Evaluate(capture.Value, graph, current);

    // Assigning through a ref local writes the variable it refers to, which is not followed
    private void WriteSimple(ISimpleAssignmentOperation assignment, ControlFlowGraph graph, ThreadVariance[] current,
        SyntaxNode? opaque)
    {
        var rebinds = assignment.IsRef || assignment.Target is not ILocalReferenceOperation { Local.IsRef: true };
        var target = assignment.Target is IFlowCaptureReferenceOperation captureRef &&
                     assignment.Syntax.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                     state.Recorder.TryGetCapturedProperty(graph, captureRef.Id, out var capturedProperty)
            ? capturedProperty
            : assignment.Target;
        var assigned = EvaluateAssigned(target, assignment.Value, graph, current, opaque);
        Assign(target, assigned, rebinds, current, opaque);
    }

    private void WriteCompound(ICompoundAssignmentOperation compound, ControlFlowGraph graph, ThreadVariance[] current,
        SyntaxNode? opaque) =>
        Assign(compound.Target, EvaluateAssigned(compound.Target, compound, graph, current, opaque), strong: true,
            current, opaque);

    private void WriteIncrement(IIncrementOrDecrementOperation increment, ControlFlowGraph graph,
        ThreadVariance[] current, SyntaxNode? opaque) =>
        Assign(increment.Target, EvaluateAssigned(increment.Target, increment, graph, current, opaque),
            strong: true, current, opaque);

    // The value assigned to a local that a lambda body declares is read with the parameters of the lambda, and of the
    // lambdas that enclose it, as uniform, as the result of the lambda is: the call that takes the lambda decides what
    // it passes them. A write to anything the body does not declare keeps them thread-variant, since it is what the
    // call passes in.
    private ThreadVariance EvaluateAssigned(IOperation target, IOperation value, ControlFlowGraph graph,
        ThreadVariance[] current, SyntaxNode? opaque)
    {
        if (opaque != null &&
            target is ILocalReferenceOperation { Local: var local } &&
            IsDeclaredIn(local, opaque) &&
            state.Recorder.GetLambdaParameters(opaque) is { IsEmpty: false } parameters)
        {
            return state.Reader.EvaluateWithUniformParameters(parameters, value, graph, current);
        }

        return state.Reader.Evaluate(value, graph, current);
    }

    // (a, b) = (x, y): each element from its own value when both sides are tuples, from the whole value otherwise
    private void Deconstruct(IOperation target, IOperation value, ControlFlowGraph graph, ThreadVariance[] current,
        SyntaxNode? opaque)
    {
        target = target is IDeclarationExpressionOperation declaration ? declaration.Expression : target;
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        if (target is not ITupleOperation targets)
        {
            Assign(target, state.Reader.Evaluate(value, graph, current), strong: true, current, opaque);
            return;
        }

        if (value is ITupleOperation values && values.Elements.Length == targets.Elements.Length)
        {
            for (var i = 0; i < targets.Elements.Length; i++)
            {
                Deconstruct(targets.Elements[i], values.Elements[i], graph, current, opaque);
            }

            return;
        }

        var variance = state.Reader.Evaluate(value, graph, current);
        foreach (var element in targets.Elements)
        {
            AssignAll(element, variance, current, opaque);
        }
    }

    // The parameter of a spliced body holds what the call leaves in its argument; a call whose body is not spliced (a
    // recursive call) may leave anything
    private void WriteLocalFunctionArguments(IInvocationOperation invocation, ThreadVariance[] current,
        SyntaxNode? opaque)
    {
        var recorder = state.Recorder;
        var spliced = recorder.TryGetResult(invocation.Syntax, out _);
        var parameters = invocation.TargetMethod.OriginalDefinition.Parameters;
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is not { RefKind: RefKind.Ref or RefKind.Out } parameter)
            {
                continue;
            }

            var written = spliced && recorder.TryGetValue(parameters[parameter.Ordinal], out var index)
                ? current[index]
                : ThreadVariance.Block;
            var target =
                argument.Value is IDeclarationExpressionOperation declared ? declared.Expression : argument.Value;
            Assign(target, written, strong: parameter.RefKind == RefKind.Out, current, opaque);
        }
    }

    private void AssignAll(IOperation target, ThreadVariance variance, ThreadVariance[] current, SyntaxNode? opaque)
    {
        target = target is IDeclarationExpressionOperation declaration ? declaration.Expression : target;
        if (target is ITupleOperation tuple)
        {
            foreach (var element in tuple.Elements)
            {
                AssignAll(element, variance, current, opaque);
            }

            return;
        }

        Assign(target, variance, strong: true, current, opaque);
    }

    // M(ref x), M(out y): what the callee writes through them, thread-variant when its summary says so (a device
    // function), when the intrinsic returns a thread-variant value (an atomic) or the call goes through a delegate, or
    // when any argument is (any other method); what a local function body spliced at the call left in its parameters;
    // an out argument is always written
    private void WriteArguments(IInvocationOperation invocation, ControlFlowGraph graph, ThreadVariance[] current,
        SyntaxNode? opaque)
    {
        if (invocation.TargetMethod.MethodKind == MethodKind.LocalFunction)
        {
            WriteLocalFunctionArguments(invocation, current, opaque);
            return;
        }

        var reader = state.Reader;
        CalleeVariance? summary =
            reader.TryGetSummarizedCallee(invocation.TargetMethod, out var callee, out var declaration)
                ? reader.GetCallVariance(callee, declaration, invocation, graph, current)
                : null;
        var written = summary?.ByReference ??
                      (UniformityValueReader.IsThreadVariantCall(invocation.TargetMethod)
                          ? ThreadVariance.Block
                          : reader.EvaluateOperands(invocation, graph, current));

        // A member of a struct writes its receiver through this: the receiver keeps what the member did not write
        if (summary is { } called && UniformityWrites.WritesReceiver(invocation) && invocation.Instance is { } receiver)
        {
            Assign(receiver, called.This, strong: false, current, opaque);
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
            {
                var target = argument.Value is IDeclarationExpressionOperation declared
                    ? declared.Expression
                    : argument.Value;
                Assign(target, written, strong: argument.Parameter.RefKind == RefKind.Out, current, opaque);
            }
        }
    }

    private void WriteCreationArguments(IObjectCreationOperation creation, ControlFlowGraph graph,
        ThreadVariance[] current, SyntaxNode? opaque)
    {
        if (creation.Constructor is not { } constructor)
        {
            return;
        }

        var reader = state.Reader;
        CalleeVariance? summary = reader.TryGetSummarizedCallee(constructor, out var callee, out var declaration)
            ? reader.GetCalleeSummary(callee, declaration, reader.GetArgumentMasks(creation.Arguments, graph, current))
            : null;
        var written = summary?.ByReference ?? EvaluateCreationOperands(creation.Arguments, graph, current);
        foreach (var argument in creation.Arguments)
        {
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
            {
                var target = argument.Value is IDeclarationExpressionOperation declared
                    ? declared.Expression
                    : argument.Value;
                Assign(target, written, strong: argument.Parameter.RefKind == RefKind.Out, current, opaque);
            }
        }
    }

    private ThreadVariance EvaluateCreationOperands(ImmutableArray<IArgumentOperation> arguments, ControlFlowGraph graph,
        ThreadVariance[] current)
    {
        var variance = ThreadVariance.Uniform;
        foreach (var argument in arguments)
        {
            if (argument.Parameter?.RefKind != RefKind.Out)
            {
                variance |= state.Reader.Evaluate(argument.Value, graph, current);
            }
        }

        return variance;
    }

    // A write to a local or parameter: replaces its variance, or adds to it for a write that may not happen (through a
    // ref local, by ref, or in a lambda or local function to a local declared outside it)
    private void Assign(IOperation target, ThreadVariance variance, bool strong, ThreadVariance[] current,
        SyntaxNode? opaque)
    {
        ISymbol? symbol = target switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            IInstanceReferenceOperation { Type: { } self } => self,
            _ => null
        };

        // A field or property of a struct local, parameter or flow capture: the struct keeps its other members, so the
        // write only adds to its variance
        if (symbol == null && UniformityWrites.GetStructRoot(target) is { } root)
        {
            if (root is IFlowCaptureReferenceOperation capture)
            {
                AddToCaptureRoot(capture, variance, current);
                return;
            }

            symbol = UniformityWrites.GetRootSymbol(root);
            strong = false;
        }

        if (symbol == null || !state.Recorder.TryGetValue(symbol, out var index))
        {
            return;
        }

        if (strong && (opaque == null || IsDeclaredIn(symbol, opaque)))
        {
            current[index] = variance;
        }
        else
        {
            current[index] |= variance;
        }
    }
}
