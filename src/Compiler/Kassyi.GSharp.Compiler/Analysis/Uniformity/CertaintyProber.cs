using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Decides, for a <see cref="UniformityAnalysis"/>, whether a divergent branch certainly diverges (C10): a sparse
/// conditional constant propagation over the graph with the thread (or block) coordinate fixed to 0 and to 1.
/// </summary>
internal sealed class CertaintyProber(UniformityState state)
{
    // The condition of each branch in each probe run, computed on first use: [probe][coordinate][node]
    private readonly Constant[]?[,] _probeConditions = new Constant[]?[2, 2];

    // The argument values of each barrier-reaching call in the block runs, by coordinate and parameter ordinal
    private readonly Dictionary<SyntaxNode, Constant[]>?[] _probeSiteArguments = new Dictionary<SyntaxNode, Constant[]>?[2];

    /// <summary>
    /// Whether the divergent branch <paramref name="branch"/> certainly diverges (C10): run as thread 0 and as thread 1
    /// of a block (<c>ThreadIndex.X</c> and <c>Gpu.LaneId()</c> 0 or 1, every other coordinate 0), its condition is a
    /// constant in both runs and the two differ. A grid barrier also counts block 0 and block 1 of the grid
    /// (<c>BlockIndex.X</c>). Any launch whose dimension holds at least two threads (blocks) has both, so the branch
    /// diverges whatever the launch configuration.
    /// </summary>
    /// <remarks>
    /// [WHY] Only the X coordinate counts: a one-dimensional launch has a Y and Z width of 1, where a branch on
    /// <c>ThreadIndex.Y == 0</c> is taken by every thread, so judging it certain would reject correct code.
    /// </remarks>
    public bool IsCertain(int branch, SyncScope scope) =>
        IsCertain(branch, Probe.Block) || (scope == SyncScope.Grid && IsCertain(branch, Probe.Grid));

    private bool IsCertain(int branch, Probe probe)
    {
        var first = GetProbeConditions(probe, 0)[branch];
        var second = GetProbeConditions(probe, 1)[branch];
        return first.IsValue && second.IsValue && (first.Value != 0) != (second.Value != 0);
    }

    private Constant[] GetProbeConditions(Probe probe, int coordinate)
    {
        if (_probeConditions[(int)probe, coordinate] is { } cached)
        {
            return cached;
        }

        var conditions = RunProbe(new ProbeRun(probe, coordinate));
        _probeConditions[(int)probe, coordinate] = conditions;
        return conditions;
    }

    // A sparse conditional constant propagation over the graph with the coordinate fixed; the condition of each branch
    // that the run reaches, Bottom for the others
    private Constant[] RunProbe(ProbeRun run)
    {
        var entry = new Constant[]?[state.NodeCount];
        entry[0] = new Constant[state.Recorder.ValueCount];
        var current = new Constant[state.Recorder.ValueCount];
        bool changed;
        do
        {
            changed = PropagateProbe(entry, current, run);
        } while (changed);

        var conditions = new Constant[state.NodeCount];
        var siteArguments = run.Probe == Probe.Block ? new Dictionary<SyntaxNode, Constant[]>() : null;
        for (var node = 0; node < state.NodeCount; node++)
        {
            if (entry[node] is { } nodeEntry)
            {
                conditions[node] = RunProbeNode(node, nodeEntry, current, run, siteArguments);
            }
        }

        if (siteArguments != null)
        {
            _probeSiteArguments[run.Coordinate] = siteArguments;
        }

        return conditions;
    }

    // One pass over the reached nodes (those with an entry state): joins the state at the end of each into the entry of
    // the successors the run takes; whether an entry changed
    private bool PropagateProbe(Constant[]?[] entry, Constant[] current, ProbeRun run)
    {
        var changed = false;
        for (var node = 0; node < state.NodeCount; node++)
        {
            if (entry[node] is not { } nodeEntry)
            {
                continue;
            }

            var condition = RunProbeNode(node, nodeEntry, current, run);
            foreach (var successor in FeasibleSuccessors(node, condition))
            {
                if (entry[successor] is not { } target)
                {
                    target = new Constant[current.Length];
                    entry[successor] = target;
                    changed = true;
                }

                changed |= Join(target, current);
            }
        }

        return changed;
    }

    // target joined with values; whether target changed
    private static bool Join(Constant[] target, Constant[] values)
    {
        var changed = false;
        for (var value = 0; value < values.Length; value++)
        {
            var merged = target[value].Join(values[value]);
            if (!merged.Equals(target[value]))
            {
                target[value] = merged;
                changed = true;
            }
        }

        return changed;
    }

    // The successors a run takes from node: one side of a branch whose condition is a constant, all of them otherwise
    private ImmutableArray<int> FeasibleSuccessors(int node, Constant condition)
    {
        var flowNode = state.Graph.Nodes[node];
        if (!condition.IsValue || flowNode.WhenTrue < 0)
        {
            return state.Successors[node];
        }

        return ImmutableArray.Create(condition.Value != 0 ? flowNode.WhenTrue : flowNode.WhenFalse);
    }

    // Runs the events of node from its entry state into current; returns the value of its condition (Bottom without one)
    private Constant RunProbeNode(int node, Constant[] entry, Constant[] current, ProbeRun run,
        Dictionary<SyntaxNode, Constant[]>? siteArguments = null)
    {
        entry.AsSpan().CopyTo(current);
        var flowNode = state.Graph.Nodes[node];
        foreach (var flowEvent in state.NodeAt(node).Events)
        {
            switch (flowEvent.Kind)
            {
                case UniformityEventKind.Define:
                    current[flowEvent.Value] = DefineInProbe(flowEvent.Value, run);
                    break;
                case UniformityEventKind.Write when flowEvent is { Operation: { } operation, Graph: { } graph }:
                    WriteInProbe(operation, graph, current, flowEvent.Opaque, run);
                    break;
                case UniformityEventKind.Site when siteArguments != null &&
                                             flowEvent is
                                             {
                                                 Operation: IInvocationOperation call,
                                                 Graph: { } callGraph
                                             }:
                    RecordSiteArguments(call, callGraph, current, run, siteArguments);
                    break;
            }
        }

        return flowNode is { Condition: { } condition, Graph: { } conditionGraph }
            ? EvaluateInProbe(condition, conditionGraph, current, run)
            : default;
    }

    private void RecordSiteArguments(IInvocationOperation call, ControlFlowGraph graph, Constant[] current, ProbeRun run,
        Dictionary<SyntaxNode, Constant[]> siteArguments)
    {
        var values = new Constant[Math.Min(call.TargetMethod.Parameters.Length, 64)];
        foreach (var argument in call.Arguments)
        {
            if (argument.Parameter is { } parameter && parameter.Ordinal < values.Length)
            {
                values[parameter.Ordinal] = EvaluateInProbe(argument.Value, graph, current, run);
            }
        }

        if (siteArguments.TryGetValue(call.Syntax, out var seen))
        {
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = values[i].Join(seen[i]);
            }
        }

        siteArguments[call.Syntax] = values;
    }

    /// <summary>
    /// The parameters of the barrier-reaching call <paramref name="call"/> whose argument is 0 for thread 0 and 1 for
    /// thread 1 of a block, as the thread index is: the callee may then be judged with that parameter as the
    /// coordinate (C10, decision 2.4).
    /// </summary>
    public ulong GetIdentityMask(SyntaxNode call)
    {
        GetProbeConditions(Probe.Block, 0);
        GetProbeConditions(Probe.Block, 1);
        if (_probeSiteArguments[0] is not { } first || _probeSiteArguments[1] is not { } second ||
            !first.TryGetValue(call, out var zero) || !second.TryGetValue(call, out var one))
        {
            return 0;
        }

        ulong mask = 0;
        for (var i = 0; i < zero.Length && i < one.Length; i++)
        {
            if (zero[i] is { IsValue: true, Value: 0 } && one[i] is { IsValue: true, Value: 1 })
            {
                mask |= ArgumentMasks.Bit(i);
            }
        }

        return mask;
    }

    // A parameter that receives the thread index itself is the coordinate of the run; any other is unknown
    private Constant DefineInProbe(int value, ProbeRun run) =>
        state.Recorder.Parameters.TryGetValue(value, out var parameter) &&
        (state.IdentityMask & ArgumentMasks.Bit(parameter.Ordinal)) != 0
            ? Constant.Of(run.Probe == Probe.Block ? run.Coordinate : 0)
            : Constant.Unknown;

    private void WriteInProbe(IOperation operation, ControlFlowGraph graph, Constant[] current, SyntaxNode? opaque,
        ProbeRun run)
    {
        switch (operation)
        {
            case IFlowCaptureOperation capture:
                current[state.Recorder.ValueOf(graph, capture.Id)] = EvaluateInProbe(capture.Value, graph, current, run); break;
            case IDeconstructionAssignmentOperation deconstruction:
                DeconstructInProbe(deconstruction.Target, deconstruction.Value, graph, current, opaque, run); break;
            case ISimpleAssignmentOperation assignment:
                AssignInProbe(assignment.Target, ValueOf(assignment, graph, current, run), current, opaque); break;
            case ICompoundAssignmentOperation compound:
                AssignInProbe(compound.Target, ValueOf(compound, graph, current, run), current, opaque); break;
            case IIncrementOrDecrementOperation increment:
                AssignInProbe(increment.Target, ValueOf(increment, graph, current, run), current, opaque); break;
            case IInvocationOperation invocation: ForgetArguments(invocation, current, opaque); break;
        }
    }

    // Assigning through a ref local writes the variable it refers to, which is not followed
    private Constant ValueOf(ISimpleAssignmentOperation assignment, ControlFlowGraph graph, Constant[] current,
        ProbeRun run) =>
        assignment.IsRef || assignment.Target is not ILocalReferenceOperation { Local.IsRef: true }
            ? EvaluateInProbe(assignment.Value, graph, current, run)
            : Constant.Unknown;

    private Constant ValueOf(ICompoundAssignmentOperation compound, ControlFlowGraph graph, Constant[] current,
        ProbeRun run) =>
        compound.IsChecked || compound.OperatorMethod != null
            ? Constant.Unknown
            : Fold(compound.OperatorKind,
                EvaluateInProbe(compound.Target, graph, current, run),
                EvaluateInProbe(compound.Value, graph, current, run),
                compound.Target.Type,
                compound.Type);

    private Constant ValueOf(IIncrementOrDecrementOperation increment, ControlFlowGraph graph, Constant[] current,
        ProbeRun run)
    {
        var before = EvaluateInProbe(increment.Target, graph, current, run);
        return before.IsValue && !increment.IsChecked && increment.OperatorMethod == null
            ? Wrap(before.Value + (increment.Kind == OperationKind.Increment ? 1 : -1), increment.Type)
            : Constant.Unknown;
    }

    // M(ref x), M(out y): no constant after the call
    private void ForgetArguments(IInvocationOperation invocation, Constant[] current, SyntaxNode? opaque)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
            {
                var target = argument.Value is IDeclarationExpressionOperation declared
                    ? declared.Expression
                    : argument.Value;
                AssignInProbe(target, Constant.Unknown, current, opaque);
            }
        }
    }

    private void DeconstructInProbe(IOperation target, IOperation value, ControlFlowGraph graph, Constant[] current,
        SyntaxNode? opaque, ProbeRun run)
    {
        target = target is IDeclarationExpressionOperation declaration ? declaration.Expression : target;
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        if (target is ITupleOperation targets)
        {
            var values = value as ITupleOperation;
            for (var i = 0; i < targets.Elements.Length; i++)
            {
                if (values != null && values.Elements.Length == targets.Elements.Length)
                {
                    DeconstructInProbe(targets.Elements[i], values.Elements[i], graph, current, opaque, run);
                }
                else
                {
                    AssignAllInProbe(targets.Elements[i], current, opaque);
                }
            }

            return;
        }

        AssignInProbe(target, EvaluateInProbe(value, graph, current, run), current, opaque);
    }

    private void AssignAllInProbe(IOperation target, Constant[] current, SyntaxNode? opaque)
    {
        target = target is IDeclarationExpressionOperation declaration ? declaration.Expression : target;
        if (target is ITupleOperation tuple)
        {
            foreach (var element in tuple.Elements)
            {
                AssignAllInProbe(element, current, opaque);
            }

            return;
        }

        AssignInProbe(target, Constant.Unknown, current, opaque);
    }

    // As Assign: a write inside a lambda or local function to a local declared outside it may not happen
    private void AssignInProbe(IOperation target, Constant value, Constant[] current, SyntaxNode? opaque)
    {
        ISymbol? symbol = target switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null
        };

        // A member of a struct local or parameter: the struct is no longer one constant
        if (symbol == null && UniformitySolver.GetStructRoot(target) is { } root)
        {
            symbol = root;
            value = Constant.Unknown;
        }

        if (symbol == null || !state.Recorder.TryGetValue(symbol, out var index))
        {
            return;
        }

        current[index] = opaque == null || UniformitySolver.IsDeclaredIn(symbol, opaque) ? value : current[index].Join(value);
    }

    // The value of operation in the run: the coordinates, compile-time constants and what folds from them
    private Constant EvaluateInProbe(IOperation operation, ControlFlowGraph graph, Constant[] current, ProbeRun run)
    {
        if (operation.ConstantValue is { HasValue: true, Value: var constant })
        {
            return Constant.FromObject(constant);
        }

        return operation switch
        {
            ILocalReferenceOperation local =>
                state.Recorder.TryGetValue(local.Local, out var index) ? current[index].OrUnknown() : Constant.Unknown,
            IParameterReferenceOperation parameter =>
                state.Recorder.TryGetValue(parameter.Parameter, out var index) ? current[index].OrUnknown() : Constant.Unknown,
            IFlowCaptureReferenceOperation capture =>
                state.Recorder.TryGetCapture(graph, capture.Id, out var index) ? current[index].OrUnknown() : Constant.Unknown,
            IPropertyReferenceOperation { Property: var property } => Coordinate(property, run),
            IInvocationOperation invocation when IsLaneId(invocation.TargetMethod) =>
                Constant.Of(run.Probe == Probe.Block ? run.Coordinate : 0),
            IConversionOperation conversion => Convert(conversion, EvaluateInProbe(conversion.Operand, graph, current, run)),
            IParenthesizedOperation parenthesized => EvaluateInProbe(parenthesized.Operand, graph, current, run),
            ISimpleAssignmentOperation assignment => EvaluateInProbe(assignment.Value, graph, current, run),
            IBinaryOperation { IsChecked: false, OperatorMethod: null } binary => Fold(binary.OperatorKind,
                EvaluateInProbe(binary.LeftOperand, graph, current, run),
                EvaluateInProbe(binary.RightOperand, graph, current, run),
                binary.LeftOperand.Type,
                binary.Type),
            IUnaryOperation { IsChecked: false, OperatorMethod: null } unary =>
                FoldUnary(unary.OperatorKind, EvaluateInProbe(unary.Operand, graph, current, run), unary.Type),
            IIsPatternOperation { Pattern: IConstantPatternOperation pattern } isPattern =>
                Fold(BinaryOperatorKind.Equals,
                    EvaluateInProbe(isPattern.Value, graph, current, run),
                    EvaluateInProbe(pattern.Value, graph, current, run),
                    isPattern.Value.Type,
                    isPattern.Type),
            _ => Constant.Unknown
        };
    }

    // ThreadIndex.X / .Y / .Z and BlockIndex.X / .Y / .Z in the run; a coordinate other than X is 0, as in thread 0
    // and thread 1 of a block (block 0 and block 1 of the grid); the block of a block run is not known
    private static Constant Coordinate(IPropertySymbol property, ProbeRun run)
    {
        if (SyncIntrinsicClassifier.IsThreadVariantSource(property))
        {
            return Constant.Of(run.Probe == Probe.Block && property.Name == "X" ? run.Coordinate : 0);
        }

        if (SyncIntrinsicClassifier.IsBlockVariantSource(property))
        {
            return run.Probe == Probe.Grid
                ? Constant.Of(property.Name == "X" ? run.Coordinate : 0)
                : Constant.Unknown;
        }

        return Constant.Unknown;
    }

    private static bool IsLaneId(IMethodSymbol method) =>
        method.Name == "LaneId" && method.Parameters.Length == 0 &&
        SyncIntrinsicClassifier.Classify(method).ResultIsThreadVariant;

    // Integer and Boolean arithmetic as unchecked C# does it: the operands are values of operandType, the result wraps
    // to the width of resultType (a comparison gives a Boolean)
    private static Constant Fold(BinaryOperatorKind kind, Constant left, Constant right, ITypeSymbol? operandType,
        ITypeSymbol? resultType)
    {
        if (!left.IsValue || !right.IsValue || !IsIntegralOrBoolean(operandType) || !IsIntegralOrBoolean(resultType))
        {
            return Constant.Unknown;
        }

        long a = left.Value;
        long b = right.Value;
        var shiftMask = operandType?.SpecialType == SpecialType.System_Int64 ? 63 : 31;

        // [WHY] C# throws for a division by zero and for the smallest value of the type divided by -1 (and long arithmetic
        // here would throw for long.MinValue / -1): no constant, as there is no value
        var divides = b != 0 && !(b == -1 && a == MinimumOf(operandType));
        long? result = kind switch
        {
            BinaryOperatorKind.Add => a + b,
            BinaryOperatorKind.Subtract => a - b,
            BinaryOperatorKind.Multiply => a * b,
            BinaryOperatorKind.Divide when divides => a / b,
            BinaryOperatorKind.Remainder when divides => a % b,
            BinaryOperatorKind.And or BinaryOperatorKind.ConditionalAnd => a & b,
            BinaryOperatorKind.Or or BinaryOperatorKind.ConditionalOr => a | b,
            BinaryOperatorKind.ExclusiveOr => a ^ b,
            BinaryOperatorKind.LeftShift => a << (int)(b & shiftMask),
            BinaryOperatorKind.RightShift => a >> (int)(b & shiftMask),
            BinaryOperatorKind.Equals => a == b ? 1 : 0,
            BinaryOperatorKind.NotEquals => a != b ? 1 : 0,
            BinaryOperatorKind.LessThan => a < b ? 1 : 0,
            BinaryOperatorKind.LessThanOrEqual => a <= b ? 1 : 0,
            BinaryOperatorKind.GreaterThan => a > b ? 1 : 0,
            BinaryOperatorKind.GreaterThanOrEqual => a >= b ? 1 : 0,
            _ => null
        };

        return result is { } value ? Wrap(value, resultType) : Constant.Unknown;
    }

    private static Constant FoldUnary(UnaryOperatorKind kind, Constant operand, ITypeSymbol? type) =>
        !operand.IsValue || !IsIntegralOrBoolean(type)
            ? Constant.Unknown
            : kind switch
            {
                UnaryOperatorKind.Not => Constant.Of(operand.Value == 0 ? 1 : 0),
                UnaryOperatorKind.Minus => Wrap(-operand.Value, type),
                UnaryOperatorKind.Plus => operand,
                UnaryOperatorKind.BitwiseNegation => Wrap(~operand.Value, type),
                _ => Constant.Unknown
            };

    // An unchecked conversion to an integral or Boolean type keeps the low bits of the value, as C# does; any other
    // conversion (to a floating-point type, user-defined, checked) gives no constant
    private static Constant Convert(IConversionOperation conversion, Constant operand) =>
        !operand.IsValue || conversion.IsChecked || conversion.OperatorMethod != null
            ? Constant.Unknown
            : Wrap(operand.Value, conversion.Type);

    // value as a value of type: the low bits for an integral type, unknown for a type that is not integral or Boolean
    private static Constant Wrap(long value, ITypeSymbol? type) =>
        UnderlyingType(type)?.SpecialType switch
        {
            SpecialType.System_Boolean => Constant.Of(value != 0 ? 1 : 0),
            SpecialType.System_SByte => Constant.Of(unchecked((sbyte)value)),
            SpecialType.System_Byte => Constant.Of(unchecked((byte)value)),
            SpecialType.System_Int16 => Constant.Of(unchecked((short)value)),
            SpecialType.System_UInt16 or SpecialType.System_Char => Constant.Of(unchecked((ushort)value)),
            SpecialType.System_Int32 => Constant.Of(unchecked((int)value)),
            SpecialType.System_UInt32 => Constant.Of(unchecked((uint)value)),
            SpecialType.System_Int64 => Constant.Of(value),
            _ => Constant.Unknown
        };

    // The smallest value of a signed integral type, which divided by -1 overflows; long.MinValue for any other type
    private static long MinimumOf(ITypeSymbol? type) => UnderlyingType(type)?.SpecialType switch
    {
        SpecialType.System_SByte => sbyte.MinValue,
        SpecialType.System_Int16 => short.MinValue,
        SpecialType.System_Int32 => int.MinValue,
        _ => long.MinValue
    };

    private static ITypeSymbol? UnderlyingType(ITypeSymbol? type) =>
        type is INamedTypeSymbol { EnumUnderlyingType: { } underlying } ? underlying : type;

    private static bool IsIntegralOrBoolean(ITypeSymbol? type) =>
        UnderlyingType(type)?.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char
            or SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16
            or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64;
}
