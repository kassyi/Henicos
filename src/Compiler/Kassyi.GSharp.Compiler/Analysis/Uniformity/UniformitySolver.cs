using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Solves the variance of the values and the influence regions of the branches of a <see cref="UniformityAnalysis"/>:
/// the forward data flow and the regions, alternately until neither grows, and what each write does to the state.
/// </summary>
internal sealed class UniformitySolver(UniformityState state)
{
    // The values written in the influence region of each branch, each once, computed on first use
    private ImmutableArray<int>?[] _writtenInRegion = [];

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
        _writtenInRegion = new ImmutableArray<int>?[count];

        var current = new ThreadVariance[values];
        do
        {
            Propagate(current);
        } while (UpdateRegions(current));
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
                    current[flowEvent.Value] = flowEvent.Variance;
                    break;
                case { Kind: UniformityEventKind.Write, Operation: { } operation, Graph: { } graph }:
                    ApplyWrite(operation, graph, current, flowEvent.Opaque);
                    break;
                case { Kind: UniformityEventKind.Observe, Operation: { } observed, Graph: { } observedGraph }
                    when state.Observed != null:
                    state.Reader.Evaluate(observed, observedGraph, current);
                    break;
                case { Kind: UniformityEventKind.Site, Operation: IInvocationOperation call, Graph: { } callGraph }
                    when state.SiteMasks != null:
                    state.Barriers.RecordSiteMasks(call, callGraph, current);
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

    /// <summary>The local or parameter of struct type whose member target writes (p.X, p.Inner.Y), or null for any other target.</summary>
    public static ISymbol? GetStructRoot(IOperation target)
    {
        var current = target;
        while (true)
        {
            var instance = current switch
            {
                IFieldReferenceOperation { Instance: { } fieldOwner } => fieldOwner,
                IPropertyReferenceOperation { Instance: { } propertyOwner } => propertyOwner,
                _ => null
            };

            if (instance is not { Type: { IsValueType: true, IsRefLikeType: false } })
            {
                return null;
            }

            switch (instance)
            {
                case ILocalReferenceOperation local:
                    return local.Local;
                case IParameterReferenceOperation parameter:
                    return parameter.Parameter;
                default:
                    current = instance;
                    continue;
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

    // The variance of each branch at the end of its node; adds the variance of a branch to the nodes of its region, and
    // to the values written in the region where its paths meet again
    private bool UpdateRegions(ThreadVariance[] current)
    {
        var changed = false;
        for (var node = 0; node < state.NodeCount; node++)
        {
            if (state.Graph.Nodes[node] is not { Condition: { } condition, Graph: { } graph } ||
                state.Successors[node].Length < 2)
            {
                continue;
            }

            RunNode(node, current);
            var variance = state.Branch[node] | state.Reader.Evaluate(condition, graph, current);
            if (variance == state.Branch[node])
            {
                continue;
            }

            state.Branch[node] = variance;
            var region = state.PostDominators.GetRegion(node);
            changed |= AddToRegion(region, variance);
            changed |= AddAtJoins(node, variance);
        }

        return changed;
    }

    private bool AddToRegion(ImmutableArray<int> region, ThreadVariance variance)
    {
        var changed = false;
        foreach (var member in region)
        {
            var merged = state.Region[member] | variance;
            if (merged != state.Region[member])
            {
                state.Region[member] = merged;
                changed = true;
            }
        }

        return changed;
    }

    // A value written in the region may differ where the paths of the branch meet again
    private bool AddAtJoins(int branch, ThreadVariance variance)
    {
        var written = GetWrittenInRegion(branch);
        var changed = false;
        foreach (var join in state.PostDominators.GetJoins(branch))
        {
            var target = state.In[join];
            foreach (var value in written)
            {
                var merged = target[value] | variance;
                changed |= merged != target[value];
                target[value] = merged;
            }
        }

        return changed;
    }

    // The values that the nodes of the influence region of branch write, each once; the region does not change, only the
    // variance of the branch grows
    private ImmutableArray<int> GetWrittenInRegion(int branch)
    {
        if (_writtenInRegion[branch] is { } cached)
        {
            return cached;
        }

        var seen = new bool[state.Recorder.ValueCount];
        var written = ImmutableArray.CreateBuilder<int>();
        foreach (var member in state.PostDominators.GetRegion(branch))
        {
            foreach (var value in state.NodeAt(member).Written)
            {
                if (!seen[value])
                {
                    seen[value] = true;
                    written.Add(value);
                }
            }
        }

        var result = written.ToImmutable();
        _writtenInRegion[branch] = result;
        return result;
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
        }
    }

    private void WriteCapture(IFlowCaptureOperation capture, ControlFlowGraph graph, ThreadVariance[] current) =>
        current[state.Recorder.ValueOf(graph, capture.Id)] = state.Reader.Evaluate(capture.Value, graph, current);

    // Assigning through a ref local writes the variable it refers to, which is not followed
    private void WriteSimple(ISimpleAssignmentOperation assignment, ControlFlowGraph graph, ThreadVariance[] current,
        SyntaxNode? opaque)
    {
        var rebinds = assignment.IsRef || assignment.Target is not ILocalReferenceOperation { Local.IsRef: true };
        Assign(assignment.Target, state.Reader.Evaluate(assignment.Value, graph, current), rebinds, current, opaque);
    }

    private void WriteCompound(ICompoundAssignmentOperation compound, ControlFlowGraph graph, ThreadVariance[] current,
        SyntaxNode? opaque) =>
        Assign(compound.Target,
            state.Reader.Evaluate(compound.Target, graph, current) | state.Reader.Evaluate(compound.Value, graph, current),
            strong: true, current, opaque);

    private void WriteIncrement(IIncrementOrDecrementOperation increment, ControlFlowGraph graph,
        ThreadVariance[] current, SyntaxNode? opaque) =>
        Assign(increment.Target, state.Reader.Evaluate(increment.Target, graph, current), strong: true, current, opaque);

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
    // function) or when any argument is (any other method); an out argument is always written
    private void WriteArguments(IInvocationOperation invocation, ControlFlowGraph graph, ThreadVariance[] current,
        SyntaxNode? opaque)
    {
        var reader = state.Reader;
        var written = reader.TryGetDeviceFunction(invocation.TargetMethod, out var callee, out var declaration)
            ? reader.GetCallVariance(callee, declaration, invocation, graph, current).ByReference
            : reader.EvaluateOperands(invocation, graph, current);
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

    // A write to a local or parameter: replaces its variance, or adds to it for a write that may not happen (through a
    // ref local, by ref, or in a lambda or local function to a local declared outside it)
    private void Assign(IOperation target, ThreadVariance variance, bool strong, ThreadVariance[] current,
        SyntaxNode? opaque)
    {
        ISymbol? symbol = target switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null
        };

        // A field or property of a struct local or parameter: the struct keeps its other members, so the write only
        // adds to its variance
        if (symbol == null && GetStructRoot(target) is { } root)
        {
            symbol = root;
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
