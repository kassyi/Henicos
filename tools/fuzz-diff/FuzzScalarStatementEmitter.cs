namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Emitter for scalar random and core statements inside generated fuzz kernels.
/// </summary>
internal static class FuzzScalarStatementEmitter
{
    private sealed record StatementKind(
        int Below,
        Func<KernelGenerationState, ScalarLocalVariable, bool> CanEmit,
        Action<KernelGenerationState, ScalarLocalVariable> Emit);

    private static readonly Func<KernelGenerationState, ScalarLocalVariable, bool> s_always = static (_, _) => true;

    private static bool IsArithmeticTarget(KernelGenerationState state, ScalarLocalVariable target) =>
        !target.Type.IsEnum && target.Type.Kind != FuzzScalarKind.Boolean;

    private static readonly StatementKind[] s_statementKinds =
    [
        new(25, s_always, EmitAssignmentStatement),
        new(42, IsArithmeticTarget, EmitCompoundAssignmentStatement),
        new(
            52,
            s_always,
            static (state, _) => FuzzCompositeExpressionEmitter.EmitTupleDeconstructionStatement(state)),
        new(
            62,
            s_always,
            static (state, _) => FuzzCompositeExpressionEmitter.EmitNullableStatement(state)),
        new(
            70,
            s_always,
            static (state, _) => FuzzCompositeExpressionEmitter.EmitSpanStatement(state)),
        new(
            78,
            s_always,
            static (state, _) => FuzzCompositeExpressionEmitter.EmitRefTernaryStatement(state)),
        new(
            86,
            static (state, _) => state.Swarm.DeviceCallWeight > 1 && !state.InSwitchSection,
            static (state, _) => FuzzDeviceFunctionEmitter.EmitRandomDeviceFunctionCall(state)),
        new(
            92,
            static (state, _) => state.ControlFlowDepth < 2
                && (state.Swarm.ControlFlowWeight > 1 || state.Swarm.LoopWeight > 1),
            static (state, _) => EmitControlFlowStatement(state)),
        new(96, IsArithmeticTarget, EmitIncrementStatement),
        new(
            int.MaxValue,
            static (state, _) => state.ControlFlowDepth == 0,
            static (state, _) => EmitNewLocalStatement(state)),
    ];

    /// <summary>
    /// Emits <paramref name="count"/> generated statements into <paramref name="writer"/> at the next control-flow
    /// depth (up to depth <c>2</c>), preserving loop-variable immutability and scoping invariants.
    /// </summary>
    internal static void EmitNestedStatements(
        KernelGenerationState state,
        SourceWriter writer,
        int count,
        bool inSwitchSection = false)
    {
        if (state.ControlFlowDepth >= 2 || count <= 0)
        {
            return;
        }

        var nestedBuffer = new List<string>(count * 2);
        var savedBuffer = state.SwapStatementsBuffer(nestedBuffer);
        bool savedInSwitch = state.InSwitchSection;
        state.ControlFlowDepth++;
        state.InSwitchSection = savedInSwitch || inSwitchSection;
        try
        {
            for (int i = 0; i < count; i++)
            {
                EmitRandomStatement(state);
            }
        }
        finally
        {
            state.InSwitchSection = savedInSwitch;
            state.ControlFlowDepth--;
            state.SwapStatementsBuffer(savedBuffer);
        }

        foreach (string stmt in nestedBuffer)
        {
            writer.AppendLine(stmt);
        }
    }

    /// <summary>Emits core deterministic scalar statements into <paramref name="state"/>.</summary>
    internal static void EmitCoreScalarStatements(KernelGenerationState state)
    {
        EmitShiftStatements(state);
        EmitInterferenceStatements(state);
        EmitNarrowStatements(state);
        EmitDivisionStatements(state);
        EmitEnumStatements(state);
    }

    private static void EmitShiftStatements(KernelGenerationState state)
    {
        var rng = state.Rng;
        var int32Local = state.FindLocal(FuzzScalarKind.Int32);
        var uint32Local = state.FindLocal(FuzzScalarKind.UInt32);
        var int64Local = state.FindLocal(FuzzScalarKind.Int64);
        var uint64Local = state.FindLocal(FuzzScalarKind.UInt64);

        string shiftOp32 = rng.NextBelow(3) switch { 0 => "<<", 1 => ">>", _ => ">>>" };
        string shiftOp64 = rng.NextBelow(3) switch { 0 => "<<", 1 => ">>", _ => ">>>" };
        string dynamicShiftCount = rng.NextBool() ? "inC[gid]" : $"(inC[gid] + {rng.NextRange(32, 95)})";
        state.Statements.Add(
            $"{int32Local.Name} = ({int32Local.Name} {shiftOp32} {dynamicShiftCount}) ^ " +
            $"(int)({uint32Local.Name} >>> inC[gid]);");
        state.Statements.Add(
            $"{int64Local.Name} = ({int64Local.Name} {shiftOp64} {dynamicShiftCount}) + " +
            $"(long)({uint64Local.Name} << inC[gid]);");
    }

    private static void EmitInterferenceStatements(KernelGenerationState state)
    {
        var rng = state.Rng;
        var (interfTarget, interfAux) = state.PickDistinctPair(FuzzScalarKind.Int32, FuzzScalarKind.Int32);
        string stepDelta = FuzzScalarExpressionEmitter.FormatLiteral(interfTarget.Type, rng.NextRange(1, 7));
        string binOp = rng.NextBelow(3) switch { 0 => "+", 1 => "-", _ => "^" };
        string three = FuzzScalarExpressionEmitter.FormatLiteral(interfTarget.Type, 3);
        state.Statements.Add(
            rng.NextBool()
                ? $"{interfAux.Name} = ((({interfTarget.Name} += {stepDelta}) {binOp} " +
                  $"({interfTarget.Name} * {three})));"
                : $"{interfAux.Name} = ((({interfTarget.Name} * {three}) {binOp} " +
                  $"({interfTarget.Name} += {stepDelta})));");
    }

    private static void EmitNarrowStatements(KernelGenerationState state)
    {
        var rng = state.Rng;
        var int32Local = state.FindLocal(FuzzScalarKind.Int32);
        FuzzScalarKind narrowKind = rng.NextBelow(4) switch
        {
            0 => FuzzScalarKind.SByte,
            1 => FuzzScalarKind.Byte,
            2 => FuzzScalarKind.Int16,
            _ => FuzzScalarKind.UInt16,
        };
        var narrowLocal = state.FindLocal(narrowKind);
        string narrowType = narrowLocal.Type.Keyword;
        string narrowRhs = $"(({narrowType})(({int32Local.Name} += 5) + 129))";
        string narrowCompoundOp = rng.NextBelow(4) switch { 0 => "+=", 1 => "-=", 2 => "*=", _ => "^=" };
        state.Statements.Add($"{narrowLocal.Name} {narrowCompoundOp} {narrowRhs};");
        state.Statements.Add($"{narrowLocal.Name}++;");
        state.Statements.Add($"--{narrowLocal.Name};");

        if (!state.IssueFilter.AvoidNarrowIncDecValue)
        {
            state.Statements.Add(
                $"{int32Local.Name} = ({int32Local.Name} ^ (++{narrowLocal.Name}) ^ ({narrowLocal.Name}--));");
        }
    }

    private static void EmitDivisionStatements(KernelGenerationState state)
    {
        var rng = state.Rng;
        var int32Local = state.FindLocal(FuzzScalarKind.Int32);
        var uint32Local = state.FindLocal(FuzzScalarKind.UInt32);

        string signedDivExpr = FuzzScalarExpressionEmitter.BuildGuardedDivisionOrModulo(
            FuzzScalarKind.Int32, "inA[gid]", "inB[gid]", isModulo: rng.NextBool(), leftIsSimpleLocal: true);
        state.Statements.Add($"{int32Local.Name} = ({int32Local.Name} + {signedDivExpr});");

        string unsignedDivExpr = FuzzScalarExpressionEmitter.BuildGuardedDivisionOrModulo(
            FuzzScalarKind.UInt32,
            uint32Local.Name,
            "(uint)inB[gid]",
            isModulo: rng.NextBool(),
            leftIsSimpleLocal: true);
        state.Statements.Add($"{uint32Local.Name} = ({uint32Local.Name} ^ {unsignedDivExpr});");

        if (!state.IssueFilter.AvoidMsvcNegatedConstantDivision)
        {
            state.Statements.Add($"{int32Local.Name} = ({int32Local.Name} ^ ((-{int32Local.Name}) / 3));");
        }
    }

    private static void EmitEnumStatements(KernelGenerationState state)
    {
        var enumByteLocal = state.FindLocal(FuzzScalarKind.EnumByte);
        var enumShortLocal = state.FindLocal(FuzzScalarKind.EnumShort);
        var enumIntLocal = state.FindLocal(FuzzScalarKind.EnumInt);
        state.Statements.Add($"{enumByteLocal.Name} = ~{enumByteLocal.Name} | FuzzEnumByte.Bit;");
        state.Statements.Add(
            $"{enumShortLocal.Name} = ({enumShortLocal.Name} ^ FuzzEnumShort.Mask) & ~FuzzEnumShort.One;");
        state.Statements.Add(
            $"{enumIntLocal.Name} = ({enumByteLocal.Name} < FuzzEnumByte.High) " +
            $"? ({enumIntLocal.Name} | FuzzEnumInt.Alt) : ~{enumIntLocal.Name};");
    }

    /// <summary>Emits a single random statement into <paramref name="state"/>.</summary>
    internal static void EmitRandomStatement(KernelGenerationState state)
    {
        var rng = state.Rng;
        var target = rng.Choose(state.Locals);
        if (state.ProtectedLoopVariables.Contains(target.Name))
        {
            target = state.FindLocal(FuzzScalarKind.Int32);
        }

        int choice = rng.NextBelow(100);

        foreach (var kind in s_statementKinds)
        {
            if (choice < kind.Below && kind.CanEmit(state, target))
            {
                kind.Emit(state, target);
                return;
            }
        }

        string fallbackRhs = FuzzScalarExpressionEmitter.GenerateLeafExpression(state, target.Type);
        state.Statements.Add($"{target.Name} = {fallbackRhs};");
    }

    private static void EmitAssignmentStatement(KernelGenerationState state, ScalarLocalVariable target)
    {
        string rhs = FuzzScalarExpressionEmitter.GenerateExpression(
            state, target.Type, depth: 0, maxDepth: 2 + (state.Size / 3));
        if (string.Equals(rhs, target.Name, StringComparison.Ordinal))
        {
            rhs = FuzzScalarExpressionEmitter.GenerateLeafExpression(state, target.Type);
        }

        state.Statements.Add($"{target.Name} = {rhs};");
    }

    private static void EmitControlFlowStatement(KernelGenerationState state)
    {
        var rng = state.Rng;
        int cfKind = state.InSwitchSection ? rng.NextBelow(2) : rng.NextBelow(3);
        if (cfKind == 0)
        {
            FuzzControlFlowEmitter.EmitIfElseStatement(state);
        }
        else if (cfKind == 1)
        {
            FuzzControlFlowEmitter.EmitBoundedLoopStatement(state);
        }
        else
        {
            FuzzControlFlowEmitter.EmitSwitchStatementWithAcyclicGoto(state);
        }
    }

    private static void EmitIncrementStatement(KernelGenerationState state, ScalarLocalVariable target)
    {
        var rng = state.Rng;
        bool canUseIncDecValue = !target.Type.IsNarrowIntegral || !state.IssueFilter.AvoidNarrowIncDecValue;
        if (canUseIncDecValue)
        {
            string stepOp = rng.NextBool() ? "++" : "--";
            string stepExpr = rng.NextBool() ? $"{stepOp}{target.Name}" : $"{target.Name}{stepOp}";
            var receiver = state.FindLocal(target.Type.Kind);
            state.Statements.Add($"{receiver.Name} = ({target.Type.Keyword})({stepExpr});");
        }
        else
        {
            state.Statements.Add($"{target.Name}++;");
        }
    }

    private static void EmitNewLocalStatement(KernelGenerationState state)
    {
        var newType = state.Rng.Choose(FuzzTypeInfo.All);
        string newName = $"v{state.Locals.Count}";
        string init = FuzzScalarExpressionEmitter.GenerateExpression(state, newType, depth: 0, maxDepth: 2);
        state.Statements.Add($"{newType.Keyword} {newName} = {init};");
        state.Locals.Add(new ScalarLocalVariable(newName, newType));
    }

    private static void EmitCompoundAssignmentStatement(KernelGenerationState state, ScalarLocalVariable target)
    {
        var rng = state.Rng;
        string[] ops = ["+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>=", ">>>="];
        string op = ops[rng.NextBelow(ops.Length)];

        if (op is "<<=" or ">>=" or ">>>=")
        {
            string countExpr = FuzzScalarExpressionEmitter.GenerateShiftCountExpression(state, depth: 1, maxDepth: 2);
            state.Statements.Add($"{target.Name} {op} {countExpr};");
            return;
        }

        if (op is "/=" or "%=")
        {
            string rawDivisor = FuzzScalarExpressionEmitter.GenerateLeafExpression(state, target.Type);
            string guardedDivisor = FuzzScalarExpressionEmitter.WrapDivisorForCompoundAssignment(
                target.Name, target.Type, rawDivisor);
            state.Statements.Add($"{target.Name} {op} {guardedDivisor};");
            return;
        }

        string rhs = FuzzScalarExpressionEmitter.GenerateExpression(state, target.Type, depth: 1, maxDepth: 2);
        string typedRhs = FuzzScalarExpressionEmitter.EnsureType(rhs, target.Type);
        state.Statements.Add($"{target.Name} {op} {typedRhs};");
    }
}
