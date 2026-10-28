using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

internal sealed partial class UniformityAnalysis
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
    private bool IsCertain(int branch, SyncScope scope) =>
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
        var count = _nodes.Count;
        var values = _symbols.Count + _captures.Count;
        var entry = new Constant[count][];
        var reached = new bool[count];
        entry[0] = new Constant[values];
        reached[0] = true;
        var state = new Constant[values];
        bool changed;
        do
        {
            changed = false;
            for (var node = 0; node < count; node++)
            {
                if (!reached[node])
                {
                    continue;
                }

                var condition = RunProbeNode(node, entry[node], state, run);
                foreach (var successor in FeasibleSuccessors(node, condition))
                {
                    if (!reached[successor])
                    {
                        reached[successor] = true;
                        entry[successor] = new Constant[values];
                        changed = true;
                    }

                    var target = entry[successor];
                    for (var value = 0; value < values; value++)
                    {
                        var merged = target[value].Join(state[value]);
                        if (!merged.Equals(target[value]))
                        {
                            target[value] = merged;
                            changed = true;
                        }
                    }
                }
            }
        } while (changed);

        var conditions = new Constant[count];
        var siteArguments = run.Probe == Probe.Block ? new Dictionary<SyntaxNode, Constant[]>() : null;
        for (var node = 0; node < count; node++)
        {
            if (reached[node])
            {
                conditions[node] = RunProbeNode(node, entry[node], state, run, siteArguments);
            }
        }

        if (siteArguments != null)
        {
            _probeSiteArguments[run.Coordinate] = siteArguments;
        }

        return conditions;
    }

    // The successors a run takes from node: one side of a branch whose condition is a constant, all of them otherwise
    private ImmutableArray<int> FeasibleSuccessors(int node, Constant condition)
    {
        var flowNode = _nodes[node];
        if (!condition.IsValue || flowNode.WhenTrue < 0)
        {
            return _successors[node];
        }

        return ImmutableArray.Create(condition.Value != 0 ? flowNode.WhenTrue : flowNode.WhenFalse);
    }

    // Runs the events of node from its entry state into state; returns the value of its condition (Bottom without one)
    private Constant RunProbeNode(int node, Constant[] entry, Constant[] state, ProbeRun run,
        Dictionary<SyntaxNode, Constant[]>? siteArguments = null)
    {
        entry.AsSpan().CopyTo(state);
        var flowNode = _nodes[node];
        foreach (var flowEvent in flowNode.Events)
        {
            switch (flowEvent.Kind)
            {
                case FlowEventKind.Define:
                    state[flowEvent.Value] = DefineInProbe(flowEvent.Value, run);
                    break;
                case FlowEventKind.Write when flowEvent is { Operation: { } operation, Graph: { } graph }:
                    WriteInProbe(operation, graph, state, flowEvent.Opaque, run);
                    break;
                case FlowEventKind.Site when siteArguments != null &&
                                             flowEvent is
                                             {
                                                 Operation: IInvocationOperation call,
                                                 Graph: { } callGraph
                                             }:
                    RecordSiteArguments(call, callGraph, state, run, siteArguments);
                    break;
            }
        }

        return flowNode is { Condition: { } condition, Graph: { } conditionGraph }
            ? EvaluateInProbe(condition, conditionGraph, state, run)
            : default;
    }

    private void RecordSiteArguments(IInvocationOperation call, ControlFlowGraph graph, Constant[] state, ProbeRun run,
        Dictionary<SyntaxNode, Constant[]> siteArguments)
    {
        var values = new Constant[Math.Min(call.TargetMethod.Parameters.Length, 64)];
        foreach (var argument in call.Arguments)
        {
            if (argument.Parameter is { } parameter && parameter.Ordinal < values.Length)
            {
                values[parameter.Ordinal] = EvaluateInProbe(argument.Value, graph, state, run);
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
    private ulong GetIdentityMask(SyntaxNode call)
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
                mask |= ParameterBit(i);
            }
        }

        return mask;
    }

    // A parameter that receives the thread index itself is the coordinate of the run; any other is unknown
    private Constant DefineInProbe(int value, ProbeRun run) =>
        _parameters.TryGetValue(value, out var parameter) && (_identityMask & ParameterBit(parameter.Ordinal)) != 0
            ? Constant.Of(run.Probe == Probe.Block ? run.Coordinate : 0)
            : Constant.Unknown;

    private void WriteInProbe(IOperation operation, ControlFlowGraph graph, Constant[] state, SyntaxNode? opaque,
        ProbeRun run)
    {
        switch (operation)
        {
            case IFlowCaptureOperation capture:
                state[ValueOf(graph, capture.Id)] = EvaluateInProbe(capture.Value, graph, state, run);
                break;
            case IDeconstructionAssignmentOperation deconstruction:
                DeconstructInProbe(deconstruction.Target, deconstruction.Value, graph, state, opaque, run);
                break;
            case ISimpleAssignmentOperation assignment:
                var rebinds = assignment.IsRef ||
                              assignment.Target is not ILocalReferenceOperation { Local.IsRef: true };
                AssignInProbe(assignment.Target,
                    rebinds ? EvaluateInProbe(assignment.Value, graph, state, run) : Constant.Unknown, state, opaque);
                break;
            case ICompoundAssignmentOperation compound:
                AssignInProbe(compound.Target,
                    compound.IsChecked || compound.OperatorMethod != null
                        ? Constant.Unknown
                        : Fold(compound.OperatorKind,
                            EvaluateInProbe(compound.Target, graph, state, run),
                            EvaluateInProbe(compound.Value, graph, state, run),
                            compound.Target.Type,
                            compound.Type),
                    state, opaque);
                break;
            case IIncrementOrDecrementOperation increment:
                var current = EvaluateInProbe(increment.Target, graph, state, run);
                AssignInProbe(increment.Target,
                    current.IsValue && !increment.IsChecked && increment.OperatorMethod == null
                        ? Wrap(current.Value + (increment.Kind == OperationKind.Increment ? 1 : -1), increment.Type)
                        : Constant.Unknown,
                    state, opaque);
                break;
            case IInvocationOperation invocation:
                foreach (var argument in invocation.Arguments)
                {
                    if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
                    {
                        var target = argument.Value is IDeclarationExpressionOperation declared
                            ? declared.Expression
                            : argument.Value;
                        AssignInProbe(target, Constant.Unknown, state, opaque);
                    }
                }

                break;
        }
    }

    private void DeconstructInProbe(IOperation target, IOperation value, ControlFlowGraph graph, Constant[] state,
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
                    DeconstructInProbe(targets.Elements[i], values.Elements[i], graph, state, opaque, run);
                }
                else
                {
                    AssignAllInProbe(targets.Elements[i], state, opaque);
                }
            }

            return;
        }

        AssignInProbe(target, EvaluateInProbe(value, graph, state, run), state, opaque);
    }

    private void AssignAllInProbe(IOperation target, Constant[] state, SyntaxNode? opaque)
    {
        target = target is IDeclarationExpressionOperation declaration ? declaration.Expression : target;
        if (target is ITupleOperation tuple)
        {
            foreach (var element in tuple.Elements)
            {
                AssignAllInProbe(element, state, opaque);
            }

            return;
        }

        AssignInProbe(target, Constant.Unknown, state, opaque);
    }

    // As Assign: a write inside a lambda or local function to a local declared outside it may not happen
    private void AssignInProbe(IOperation target, Constant value, Constant[] state, SyntaxNode? opaque)
    {
        ISymbol? symbol = target switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null
        };

        // A member of a struct local or parameter: the struct is no longer one constant
        if (symbol == null && GetStructRoot(target) is { } root)
        {
            symbol = root;
            value = Constant.Unknown;
        }

        if (symbol == null || !_symbols.TryGetValue(symbol, out var index))
        {
            return;
        }

        state[index] = opaque == null || IsDeclaredIn(symbol, opaque) ? value : state[index].Join(value);
    }

    // The value of operation in the run: the coordinates, compile-time constants and what folds from them
    private Constant EvaluateInProbe(IOperation operation, ControlFlowGraph graph, Constant[] state, ProbeRun run)
    {
        if (operation.ConstantValue is { HasValue: true, Value: var constant })
        {
            return Constant.FromObject(constant);
        }

        return operation switch
        {
            ILocalReferenceOperation local =>
                _symbols.TryGetValue(local.Local, out var index) ? state[index].OrUnknown() : Constant.Unknown,
            IParameterReferenceOperation parameter =>
                _symbols.TryGetValue(parameter.Parameter, out var index) ? state[index].OrUnknown() : Constant.Unknown,
            IFlowCaptureReferenceOperation capture =>
                _captures.TryGetValue((graph, capture.Id), out var index) ? state[index].OrUnknown() : Constant.Unknown,
            IPropertyReferenceOperation { Property: var property } => Coordinate(property, run),
            IInvocationOperation invocation when IsLaneId(invocation.TargetMethod) =>
                Constant.Of(run.Probe == Probe.Block ? run.Coordinate : 0),
            IConversionOperation conversion => Convert(conversion, EvaluateInProbe(conversion.Operand, graph, state, run)),
            IParenthesizedOperation parenthesized => EvaluateInProbe(parenthesized.Operand, graph, state, run),
            ISimpleAssignmentOperation assignment => EvaluateInProbe(assignment.Value, graph, state, run),
            IBinaryOperation { IsChecked: false, OperatorMethod: null } binary => Fold(binary.OperatorKind,
                EvaluateInProbe(binary.LeftOperand, graph, state, run),
                EvaluateInProbe(binary.RightOperand, graph, state, run),
                binary.LeftOperand.Type,
                binary.Type),
            IUnaryOperation { IsChecked: false, OperatorMethod: null } unary =>
                FoldUnary(unary.OperatorKind, EvaluateInProbe(unary.Operand, graph, state, run), unary.Type),
            IIsPatternOperation { Pattern: IConstantPatternOperation pattern } isPattern =>
                Fold(BinaryOperatorKind.Equals,
                    EvaluateInProbe(isPattern.Value, graph, state, run),
                    EvaluateInProbe(pattern.Value, graph, state, run),
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

        if (SyncIntrinsicClassifier.IsGridVariantSource(property))
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

    /// <summary>Which coordinate a probe run fixes.</summary>
    private enum Probe
    {
        // ThreadIndex.X and Gpu.LaneId()
        Block,

        // BlockIndex.X
        Grid
    }

    private readonly record struct ProbeRun(Probe Probe, int Coordinate);

    /// <summary>A value of the constant propagation: not reached yet (Bottom), one constant, or unknown.</summary>
    private readonly struct Constant : IEquatable<Constant>
    {
        private readonly byte _kind;

        private Constant(byte kind, long value)
        {
            _kind = kind;
            Value = value;
        }

        public static Constant Unknown => new(2, 0);

        public long Value { get; }

        public bool IsValue => _kind == 1;

        public static Constant Of(long value) => new(1, value);

        public static Constant FromObject(object? value) => value switch
        {
            bool flag => Of(flag ? 1 : 0),
            int number => Of(number),
            uint number => Of(number),
            long number => Of(number),
            short number => Of(number),
            ushort number => Of(number),
            byte number => Of(number),
            sbyte number => Of(number),
            char character => Of(character),
            _ => Unknown
        };

        // A read of a value that no reached path assigned is not a constant
        public Constant OrUnknown() => _kind == 0 ? Unknown : this;

        public Constant Join(Constant other) =>
            _kind == 0 ? other :
            other._kind == 0 ? this :
            _kind == 1 && other._kind == 1 && Value == other.Value ? this : Unknown;

        public bool Equals(Constant other) => _kind == other._kind && Value == other.Value;

        public override bool Equals(object? obj) => obj is Constant other && Equals(other);

        public override int GetHashCode() => (_kind * 397) ^ Value.GetHashCode();
    }
}
