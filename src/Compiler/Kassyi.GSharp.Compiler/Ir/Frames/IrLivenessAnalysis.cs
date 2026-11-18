using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Frames;

/// <summary>Computes state-machine variable liveness, cross-state touches, and pin reachability.</summary>
internal static class IrLivenessAnalysis
{
    /// <summary>The liveness of the variables of <paramref name="machine"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// The machine holds an opaque expression, an <see cref="IrSequence"/>, or a variable that is used without a
    /// declaration.
    /// </exception>
    public static IrLiveness Analyze(IrStateMachine machine)
    {
        var (variables, indices) = BuildVariables(machine);
        var hasSplit = ContainsSplit(machine);
        var words = IrTransfer.WordCount(variables.Length);
        if (!hasSplit)
        {
            return new IrLiveness(
                variables,
                new ulong[words],
                new ulong[words],
                new ulong[words],
                new ulong[words],
                hasSplit: false);
        }

        var graph = IrLivenessGraph.Build(machine);
        var transfers = BuildTransfers(graph, indices, variables.Length);
        var liveIn = SolveLiveIn(graph, transfers, words);
        var (liveAcrossSourceSplit, liveAcrossAnySplit) = CollectLiveAcrossSplits(graph, liveIn, words);
        var touchedInAnotherState = ComputeTouchedInAnotherState(variables, words);
        var pinned = SolvePinnedAcrossSplits(graph, indices, words);
        return new IrLiveness(
            variables, liveAcrossSourceSplit, liveAcrossAnySplit, touchedInAnotherState, pinned, hasSplit: true);
    }

    /// <summary>Whether a statement of <paramref name="machine"/> holds an opaque expression.</summary>
    public static bool HoldsOpaque(IrStateMachine machine) =>
        IrNodeChildren.Exists(machine, static node => node is IrOpaqueExpression or IrOpaqueStatement);

    private static bool ContainsSplit(IrStateMachine machine) =>
        machine.States
            .AsStackEnumerable()
            .Any(static state => state.Statements
                .AsStackEnumerable()
                .Any(static statement => statement is IrSuspend or IrResumeCall)
            );

    private static (ImmutableArray<IrLivenessVariable> Variables, Dictionary<IrVariable, int> Indices) BuildVariables(
        IrStateMachine machine)
    {
        var declarations =
            ImmutableArray.CreateBuilder<(IrVariable Variable, IrStatePosition Position, IrDeclarationForm Form)>();
        var declaredSet = new HashSet<IrVariable>();
        var referencesByVar = new Dictionary<IrVariable, SortedSet<int>>();
        for (var s = 0; s < machine.States.Length; s++)
        {
            var state = machine.States[s];
            if (state.Number != s)
            {
                throw new InvalidOperationException(
                    $"State number {state.Number} does not match state table index {s}.");
            }

            for (var stmtIndex = 0; stmtIndex < state.Statements.Length; stmtIndex++)
            {
                var pos = new IrStatePosition(state.Number, stmtIndex);
                WalkNode(state.Statements[stmtIndex], pos, isTopLevel: true, isResumeResult: false,
                    declarations, declaredSet, referencesByVar);
            }
        }

        return FinalizeVariables(declarations, declaredSet, referencesByVar);
    }

    private static void WalkNode(IrNode node, IrStatePosition position, bool isTopLevel, bool isResumeResult,
        ImmutableArray<(IrVariable Variable, IrStatePosition Position, IrDeclarationForm Form)>.Builder declarations,
        HashSet<IrVariable> declaredSet, Dictionary<IrVariable, SortedSet<int>> referencesByVar)
    {
        switch (node)
        {
            case IrOpaqueExpression or IrOpaqueStatement or IrSequence or IrLabel or IrGoto or IrConditionalGoto:
                throw new InvalidOperationException($"Unsupported IR node '{node.GetType().Name}' in liveness.");
            case IrLocalDeclaration declaration:
                var form = isTopLevel ? IrDeclarationForm.Statement : IrDeclarationForm.Nested;
                RecordDeclaration(declaration.Variable, position, form, declarations, declaredSet, referencesByVar);
                break;
            case IrVariableReference reference when IsTracked(reference.Variable):
                if (reference.IsDeclaration)
                {
                    var refForm = isResumeResult ? IrDeclarationForm.ResumeResult : IrDeclarationForm.Reference;
                    RecordDeclaration(reference.Variable, position, refForm, declarations, declaredSet,
                        referencesByVar);
                }
                else
                {
                    RecordReference(reference.Variable, position.State, referencesByVar);
                }

                return;
        }

        WalkChildren(node, position, declarations, declaredSet, referencesByVar);
    }

    private static void WalkChildren(IrNode node, IrStatePosition position,
        ImmutableArray<(IrVariable Variable, IrStatePosition Position, IrDeclarationForm Form)>.Builder declarations,
        HashSet<IrVariable> declaredSet, Dictionary<IrVariable, SortedSet<int>> referencesByVar)
    {
        switch (node)
        {
            case IrCall call:
                WalkOptional(call.Receiver, position, declarations, declaredSet, referencesByVar);
                WalkOrderedArguments(call.Arguments, position, declarations, declaredSet, referencesByVar);
                return;
            case IrPropertyReference property:
                WalkOptional(property.Receiver, position, declarations, declaredSet, referencesByVar);
                WalkOrderedArguments(property.Arguments, position, declarations, declaredSet, referencesByVar);
                return;
            case IrStructCreation creation:
                WalkOrderedArguments(creation.Arguments, position, declarations, declaredSet, referencesByVar);
                foreach (var init in creation.Initializers)
                {
                    WalkNode(init, position, false, false, declarations, declaredSet, referencesByVar);
                }

                return;
            case IrResumeCall resume:
                WalkNode(resume.Call, position, false, false, declarations, declaredSet, referencesByVar);
                if (resume.ResultTarget is { } target)
                {
                    WalkNode(target, position, false, isResumeResult: true, declarations, declaredSet,
                        referencesByVar);
                }

                return;
            default:
                foreach (var child in IrNodeChildren.Of(node))
                {
                    WalkNode(child, position, isTopLevel: false, isResumeResult: false, declarations, declaredSet,
                        referencesByVar);
                }

                return;
        }
    }

    private static void WalkOptional(IrNode? node, IrStatePosition position,
        ImmutableArray<(IrVariable Variable, IrStatePosition Position, IrDeclarationForm Form)>.Builder declarations,
        HashSet<IrVariable> declaredSet, Dictionary<IrVariable, SortedSet<int>> referencesByVar)
    {
        if (node is not null)
        {
            WalkNode(node, position, isTopLevel: false, isResumeResult: false, declarations, declaredSet,
                referencesByVar);
        }
    }

    private static void WalkOrderedArguments(ImmutableArray<IrArgument> arguments, IrStatePosition position,
        ImmutableArray<(IrVariable Variable, IrStatePosition Position, IrDeclarationForm Form)>.Builder declarations,
        HashSet<IrVariable> declaredSet, Dictionary<IrVariable, SortedSet<int>> referencesByVar)
    {
        if (arguments.IsEmpty)
        {
            return;
        }

        var ordered = new IrArgument[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            ordered[i] = arguments[i];
        }

        Array.Sort(ordered, static (a, b) => a.EvaluationIndex.CompareTo(b.EvaluationIndex));
        foreach (var argument in ordered)
        {
            WalkNode(argument.Value, position, isTopLevel: false, isResumeResult: false, declarations, declaredSet,
                referencesByVar);
        }
    }

    private static void RecordDeclaration(IrVariable variable, IrStatePosition position, IrDeclarationForm form,
        ImmutableArray<(IrVariable Variable, IrStatePosition Position, IrDeclarationForm Form)>.Builder declarations,
        HashSet<IrVariable> declaredSet, Dictionary<IrVariable, SortedSet<int>> referencesByVar)
    {
        if (!IsTracked(variable))
        {
            return;
        }

        if (!declaredSet.Add(variable))
        {
            throw new InvalidOperationException($"Variable '{variable.Name}' is declared more than once.");
        }

        declarations.Add((variable, position, form));
        RecordReference(variable, position.State, referencesByVar);
    }

    private static void RecordReference(IrVariable variable, int state,
        Dictionary<IrVariable, SortedSet<int>> referencesByVar)
    {
        if (!referencesByVar.TryGetValue(variable, out var states))
        {
            states = [];
            referencesByVar[variable] = states;
        }

        states.Add(state);
    }

    private static (ImmutableArray<IrLivenessVariable> Variables, Dictionary<IrVariable, int> Indices)
        FinalizeVariables(
            ImmutableArray<(IrVariable Variable, IrStatePosition Position, IrDeclarationForm Form)>.Builder
                declarations,
            HashSet<IrVariable> declaredSet, Dictionary<IrVariable, SortedSet<int>> referencesByVar)
    {
        foreach (var referenced in referencesByVar.Keys)
        {
            if (!declaredSet.Contains(referenced))
            {
                throw new InvalidOperationException(
                    $"Tracked variable '{referenced.Name}' was used without a declaration.");
            }
        }

        var result = ImmutableArray.CreateBuilder<IrLivenessVariable>(declarations.Count);
        var indices = new Dictionary<IrVariable, int>(declarations.Count);
        for (var i = 0; i < declarations.Count; i++)
        {
            var (variable, position, form) = declarations[i];
            var states = ImmutableArray.CreateRange(referencesByVar[variable]);
            result.Add(new IrLivenessVariable(variable, i, position, form, states));
            indices.Add(variable, i);
        }

        return (result.MoveToImmutable(), indices);
    }

    private static IrTransfer[] BuildTransfers(IrLivenessGraph graph, Dictionary<IrVariable, int> indices,
        int variableCount)
    {
        var transfers = new IrTransfer[graph.NodeCount];
        for (var i = 0; i < graph.NodeCount; i++)
        {
            transfers[i] = IrLivenessTransfer.OfStatement(graph.StatementOf(i), indices, variableCount);
        }

        return transfers;
    }

    private static ulong[][] SolveLiveIn(IrLivenessGraph graph, IrTransfer[] transfers, int words)
    {
        var count = graph.NodeCount;
        var liveIn = new ulong[count][];
        for (var i = 0; i < count; i++)
        {
            liveIn[i] = new ulong[words];
        }

        var liveOut = new ulong[words];
        var nextIn = new ulong[words];
        bool changed;
        do
        {
            changed = false;
            for (var i = count - 1; i >= 0; i--)
            {
                Array.Clear(liveOut, 0, words);
                foreach (var successor in graph.SuccessorsOf(i))
                {
                    OrInto(liveOut, liveIn[successor]);
                }

                IrTransfer.Apply(transfers[i], liveOut, nextIn);
                if (!nextIn.AsSpan().SequenceEqual(liveIn[i]))
                {
                    nextIn.AsSpan().CopyTo(liveIn[i]);
                    changed = true;
                }
            }
        } while (changed);

        return liveIn;
    }

    private static (ulong[] LiveAcrossSourceSplit, ulong[] LiveAcrossAnySplit) CollectLiveAcrossSplits(
        IrLivenessGraph graph, ulong[][] liveIn, int words)
    {
        var source = new ulong[words];
        var any = new ulong[words];
        for (var i = 0; i < graph.NodeCount; i++)
        {
            if (graph.IsAnySplitEntry(i))
            {
                OrInto(any, liveIn[i]);
            }

            if (graph.IsSourceSplitEntry(i))
            {
                OrInto(source, liveIn[i]);
            }
        }

        return (source, any);
    }

    private static ulong[] ComputeTouchedInAnotherState(ImmutableArray<IrLivenessVariable> variables, int words)
    {
        var touched = new ulong[words];
        foreach (var variable in variables)
        {
            if (variable.ReferencingStates
                .AsStackEnumerable()
                .Any(variable.Declaration.State, static (state, declState) => state != declState))
            {
                SetBit(touched, variable.Index);
            }
        }

        return touched;
    }

    private static ulong[] SolvePinnedAcrossSplits(IrLivenessGraph graph, Dictionary<IrVariable, int> indices,
        int words)
    {
        var pinned = new ulong[words];
        var (pinGen, hasPins) = CollectNodePins(graph, indices, words);
        if (!hasPins)
        {
            return pinned;
        }

        var pinnedIn = new ulong[graph.NodeCount][];
        for (var i = 0; i < pinnedIn.Length; i++)
        {
            pinnedIn[i] = new ulong[words];
        }

        var pinnedOut = new ulong[words];
        bool changed;
        do
        {
            changed = false;
            for (var i = 0; i < graph.NodeCount; i++)
            {
                pinnedIn[i].AsSpan().CopyTo(pinnedOut);
                OrInto(pinnedOut, pinGen[i]);
                if (graph.IsSplitTrigger(i))
                {
                    OrInto(pinned, pinnedOut);
                }

                foreach (var successor in graph.SuccessorsOf(i))
                {
                    changed |= OrInto(pinnedIn[successor], pinnedOut);
                }
            }
        } while (changed);

        return pinned;
    }

    private static (ulong[][] PinGen, bool HasPins) CollectNodePins(IrLivenessGraph graph,
        Dictionary<IrVariable, int> indices, int words)
    {
        var pinGen = new ulong[graph.NodeCount][];
        var hasPins = false;
        for (var i = 0; i < graph.NodeCount; i++)
        {
            var nodePins = new ulong[words];
            var statement = graph.StatementOf(i);
            var rootNode = statement is IrResumeCall resume ? (IrNode)resume.Call : statement;
            if (CollectPins(rootNode, indices, nodePins))
            {
                hasPins = true;
            }

            pinGen[i] = nodePins;
        }

        return (pinGen, hasPins);
    }

    private static bool CollectPins(IrNode node, Dictionary<IrVariable, int> indices, ulong[] pins)
    {
        switch (node)
        {
            case IrAddressOf address:
                var found = PinRoots(address.Operand, indices, pins);
                return CollectPins(address.Operand, indices, pins) | found;
            case IrElementReference element:
                var receiverPinned = IrStayInPlace.WithoutBuiltInImplicitConversions(element.Receiver) is not
                    IrVariableReference && CollectPins(element.Receiver, indices, pins);
                return CollectPins(element.Index, indices, pins) | receiverPinned;
            case IrPropertyReference property:
                return CollectPropertyPins(property, indices, pins);
            case IrVariableReference reference when IsStackArrayVariable(reference.Variable, indices, out var index):
                SetBit(pins, index);
                return true;
            default:
                var any = false;
                foreach (var child in IrNodeChildren.Of(node))
                {
                    any |= CollectPins(child, indices, pins);
                }

                return any;
        }
    }

    private static bool CollectPropertyPins(IrPropertyReference property, Dictionary<IrVariable, int> indices,
        ulong[] pins)
    {
        var any = false;
        if (property.Receiver is { } receiver)
        {
            var isArrayLength = property.Property.Name == "Length" &&
                                property.Property.ContainingType.SpecialType == SpecialType.System_Array;
            if (!(isArrayLength &&
                  IrStayInPlace.WithoutBuiltInImplicitConversions(receiver) is IrVariableReference))
            {
                any |= CollectPins(receiver, indices, pins);
            }
        }

        foreach (var argument in property.Arguments)
        {
            any |= CollectPins(argument.Value, indices, pins);
        }

        return any;
    }

    private static bool PinRoots(IrExpression expression, Dictionary<IrVariable, int> indices, ulong[] pins) =>
        expression switch
        {
            IrVariableReference reference when indices.TryGetValue(reference.Variable, out var index) =>
                SetBitAndReturn(pins, index),
            IrFieldReference field when !field.Field.IsStatic && field.Receiver is { } receiver &&
                                        receiver.Type.IsValueType =>
                PinRoots(receiver, indices, pins),
            IrConditional { IsRef: true } conditional =>
                PinRoots(conditional.WhenTrue, indices, pins) | PinRoots(conditional.WhenFalse, indices, pins),
            _ => false
        };

    private static bool IsStackArrayVariable(IrVariable variable, Dictionary<IrVariable, int> indices,
        out int index)
    {
        if (indices.TryGetValue(variable, out index) &&
            variable.Symbol is ILocalSymbol local &&
            StackArrayLocals.IsStackArray(local, out _))
        {
            return true;
        }

        index = -1;
        return false;
    }

    private static bool IsTracked(IrVariable variable) =>
        variable.Kind is IrVariableKind.Local or IrVariableKind.Temporary;

    private static bool SetBitAndReturn(ulong[] bits, int index)
    {
        SetBit(bits, index);
        return true;
    }

    private static void SetBit(ulong[] bits, int index) =>
        bits[index / 64] |= 1UL << (index % 64);

    private static bool OrInto(ulong[] target, ulong[] bits)
    {
        var changed = false;
        for (var w = 0; w < target.Length; w++)
        {
            var merged = target[w] | bits[w];
            changed |= merged != target[w];
            target[w] = merged;
        }

        return changed;
    }
}
