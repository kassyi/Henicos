using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Emits structured control-flow statements: <c>if</c> / <c>else</c>, <c>switch</c> statements with acyclic forward
/// <c>goto case</c> / <c>goto default</c>, bounded <c>for</c> / <c>while</c> / <c>do-while</c> loops (<c>K &lt;= 5</c>),
/// and <c>Gpu.ForUnroll(0, K, __u =&gt; ...)</c> blocks.
/// </summary>
public static class FuzzControlFlowEmitter
{
    /// <summary>Emits mandatory control-flow statements in every kernel so all control-flow constructs are exercised.</summary>
    public static void EmitCoreControlFlowStatements(KernelGenerationState state)
    {
        EmitIfElseStatement(state);
        EmitSwitchStatementWithAcyclicGoto(state);
        EmitBoundedLoopStatement(state);
        EmitForUnrollStatement(state);
    }

    /// <summary>Emits an <c>if</c> / <c>else</c> branch statement.</summary>
    public static void EmitIfElseStatement(KernelGenerationState state)
    {
        var i32 = state.FindLocal(FuzzScalarKind.Int32);
        var u32 = state.FindLocal(FuzzScalarKind.UInt32);
        string cond = FuzzScalarExpressionEmitter.GenerateBooleanExpression(state, depth: 1, maxDepth: 2);
        string thenExpr = FuzzScalarExpressionEmitter.GenerateExpression(state, i32.Type, depth: 1, maxDepth: 2);
        string elseExpr = FuzzScalarExpressionEmitter.GenerateExpression(state, u32.Type, depth: 1, maxDepth: 2);

        using var writer = new SourceWriter();
        using (writer.Scope($"if ({cond})"))
        {
            writer.AppendLine($"{i32.Name} = ({i32.Name} ^ {FuzzScalarExpressionEmitter.EnsureType(thenExpr, i32.Type)});");
            if (state.ControlFlowDepth < 2)
            {
                ScalarFuzzGenerator.EmitNestedStatements(state, writer, count: 1);
            }
        }

        using (writer.Scope("else"))
        {
            writer.AppendLine($"{u32.Name} = ({u32.Name} + {FuzzScalarExpressionEmitter.EnsureType(elseExpr, u32.Type)});");
            if (state.ControlFlowDepth < 2)
            {
                ScalarFuzzGenerator.EmitNestedStatements(state, writer, count: 1);
            }
        }

        state.Statements.Add(writer.ToString().AsSpan().TrimEnd().ToString());
    }

    /// <summary>
    /// Emits a <c>switch</c> statement with strictly acyclic forward <c>goto case</c> and <c>goto default</c> transitions.
    /// Case <c>0</c> jumps forward to Case <c>1</c>, and Case <c>2</c> jumps forward to <c>default</c>.
    /// </summary>
    public static void EmitSwitchStatementWithAcyclicGoto(KernelGenerationState state)
    {
        var rng = state.Rng;
        var i32 = state.FindLocal(FuzzScalarKind.Int32);
        var i64 = state.FindLocal(FuzzScalarKind.Int64);
        int c0 = rng.NextRange(3, 19);
        int c1 = rng.NextRange(21, 53);
        int c2 = rng.NextRange(55, 97);

        using var writer = new SourceWriter();
        using (writer.Scope($"switch (({i32.Name} ^ inA[gid]) & 3)"))
        {
            writer.AppendLine("case 0:");
            writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{i32.Name} += {c0};"));
            if (state.ControlFlowDepth < 2)
            {
                ScalarFuzzGenerator.EmitNestedStatements(state, writer, count: 1, inSwitchSection: true);
            }

            writer.AppendLine("goto case 1;");
            writer.AppendLine("case 1:");
            writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{i32.Name} ^= {c1};"));
            if (state.ControlFlowDepth < 2)
            {
                ScalarFuzzGenerator.EmitNestedStatements(state, writer, count: 1, inSwitchSection: true);
            }

            writer.AppendLine("break;");
            writer.AppendLine("case 2:");
            writer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{i64.Name} += {c2}L;"));
            if (state.ControlFlowDepth < 2)
            {
                ScalarFuzzGenerator.EmitNestedStatements(state, writer, count: 1, inSwitchSection: true);
            }

            writer.AppendLine("goto default;");
            writer.AppendLine("default:");
            writer.AppendLine($"{i64.Name} ^= {i32.Name};");
            if (state.ControlFlowDepth < 2)
            {
                ScalarFuzzGenerator.EmitNestedStatements(state, writer, count: 1, inSwitchSection: true);
            }

            writer.AppendLine("break;");
        }

        state.Statements.Add(writer.ToString().AsSpan().TrimEnd().ToString());
    }

    /// <summary>
    /// Emits a bounded <c>for</c>, <c>while</c>, or <c>do-while</c> loop with compile-time bound <c>K &lt;= 5</c>
    /// and a dedicated monotone counter that is never modified negatively inside the body.
    /// </summary>
    public static void EmitBoundedLoopStatement(KernelGenerationState state)
    {
        var rng = state.Rng;
        var i32 = state.FindLocal(FuzzScalarKind.Int32);
        var u32 = state.FindLocal(FuzzScalarKind.UInt32);
        int bound = rng.NextRange(2, 5);
        int loopId = ++state.TempCounter;
        string loopVar = string.Create(CultureInfo.InvariantCulture, $"loopIdx{loopId}");
        int kind = state.InSwitchSection ? 0 : rng.NextBelow(3);

        state.ProtectedLoopVariables.Add(loopVar);
        try
        {
            using var writer = new SourceWriter();
            if (kind == 0)
            {
                using (writer.Scope(string.Create(CultureInfo.InvariantCulture, $"for (int {loopVar} = 0; {loopVar} < {bound}; {loopVar}++)")))
                {
                    using (writer.Scope($"if ((({i32.Name} + {loopVar}) & 7) == 5)"))
                    {
                        writer.AppendLine("continue;");
                    }

                    writer.AppendLine($"{i32.Name} = (({i32.Name} << 1) ^ ({loopVar} + inB[gid]));");
                    if (state.ControlFlowDepth < 2)
                    {
                        ScalarFuzzGenerator.EmitNestedStatements(state, writer, count: 1);
                    }

                    using (writer.Scope($"if (({u32.Name} & 15U) == 14U)"))
                    {
                        writer.AppendLine("break;");
                    }
                }
            }
            else if (kind == 1)
            {
                writer.AppendLine($"int {loopVar} = 0;");
                using (writer.Scope(string.Create(CultureInfo.InvariantCulture, $"while ({loopVar} < {bound})")))
                {
                    writer.AppendLine($"{loopVar}++;");
                    writer.AppendLine($"{i32.Name} = ({i32.Name} + ({loopVar} ^ inC[gid]));");
                    if (state.ControlFlowDepth < 2)
                    {
                        ScalarFuzzGenerator.EmitNestedStatements(state, writer, count: 1);
                    }
                }
            }
            else
            {
                writer.AppendLine($"int {loopVar} = 0;");
                using (writer.Scope("do", string.Create(CultureInfo.InvariantCulture, $"}} while ({loopVar} < {bound});")))
                {
                    writer.AppendLine($"{loopVar}++;");
                    writer.AppendLine($"{u32.Name} = ({u32.Name} ^ (uint)({i32.Name} + {loopVar}));");
                    if (state.ControlFlowDepth < 2)
                    {
                        ScalarFuzzGenerator.EmitNestedStatements(state, writer, count: 1);
                    }
                }
            }

            state.Statements.Add(writer.ToString().AsSpan().TrimEnd().ToString());
        }
        finally
        {
            state.ProtectedLoopVariables.Remove(loopVar);
        }
    }

    /// <summary>
    /// Emits a <c>Gpu.ForUnroll(0, K, __u =&gt; { ... })</c> statement with <c>K &lt;= 5</c>.
    /// </summary>
    public static void EmitForUnrollStatement(KernelGenerationState state)
    {
        var rng = state.Rng;
        var i32 = state.FindLocal(FuzzScalarKind.Int32);
        var i64 = state.FindLocal(FuzzScalarKind.Int64);
        int count = rng.NextRange(2, 4);
        int unrollId = ++state.TempCounter;
        string idxVar = string.Create(CultureInfo.InvariantCulture, $"unrollIdx{unrollId}");

        using var writer = new SourceWriter();
        using (writer.Scope(string.Create(CultureInfo.InvariantCulture, $"Gpu.ForUnroll(0, {count}, {idxVar} =>"), "});"))
        {
            writer.AppendLine($"{i32.Name} = ({i32.Name} + ({idxVar} * 13) ^ sL1.A);");
            writer.AppendLine($"{i64.Name} = ({i64.Name} ^ ((long){idxVar} + sL2.C));");
        }

        state.Statements.Add(writer.ToString().AsSpan().TrimEnd().ToString());
    }
}
