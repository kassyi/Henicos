namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Emits CUDA per-iteration counter declarations, resets, and increments planned by
/// <see cref="ActiveMaskIterationPlan"/>.
/// </summary>
internal static class ActiveMaskIterationEmitter
{
    public static void EmitDeclarations(ActiveMaskIterationPlan plan, ref TranspileContext context)
    {
        for (var i = 0; i < plan.CounterCount; i++)
        {
            context.Emitter.Writer.AppendLine(
                $"unsigned long long {GSharpInternalNames.Lowering.IterationCounterPrefix}{i} = 0ull;");
        }
    }

    public static void EmitResets(StatementSyntax statement, ref TranspileContext context)
    {
        if (!context.Emitter.Dialect.IsCuda ||
            context.Emitter.ActiveMaskIterations is not { } plan ||
            !plan.TryGetResetCounters(statement, out var counters))
        {
            return;
        }

        foreach (var counter in counters)
        {
            context.Emitter.Writer.AppendLine(
                $"{GSharpInternalNames.Lowering.IterationCounterPrefix}{counter}=0ull;");
        }
    }

    public static void EmitPostLabelResets(LabeledStatementSyntax label, ref TranspileContext context)
    {
        if (!context.Emitter.Dialect.IsCuda ||
            context.Emitter.ActiveMaskIterations is not { } plan ||
            !plan.TryGetPostLabelResetCounters(label, out var counters))
        {
            return;
        }

        foreach (var counter in counters)
        {
            context.Emitter.Writer.AppendLine(
                $"{GSharpInternalNames.Lowering.IterationCounterPrefix}{counter}=0ull;");
        }
    }

    public static void EmitSwitchResets(SwitchStatementSyntax switchStatement, ref TranspileContext context)
    {
        if (!context.Emitter.Dialect.IsCuda ||
            context.Emitter.ActiveMaskIterations is not { } plan ||
            !plan.TryGetSwitchResets(switchStatement, out var counters))
        {
            return;
        }

        foreach (var counter in counters)
        {
            context.Emitter.Writer.AppendLine(
                $"{GSharpInternalNames.Lowering.IterationCounterPrefix}{counter}=0ull;");
        }
    }

    public static void EmitLoopIncrement(StatementSyntax loop, ref TranspileContext context)
    {
        if (!context.Emitter.Dialect.IsCuda ||
            context.Emitter.ActiveMaskIterations is not { } plan ||
            !plan.TryGetLoopCounter(loop, out var counter))
        {
            return;
        }

        context.Emitter.Writer.AppendLine(
            $"++{GSharpInternalNames.Lowering.IterationCounterPrefix}{counter};");
    }

    public static void EmitLabelIncrement(LabeledStatementSyntax label, ref TranspileContext context)
    {
        if (!context.Emitter.Dialect.IsCuda ||
            context.Emitter.ActiveMaskIterations is not { } plan ||
            !plan.TryGetLabelCounters(label, out var counters))
        {
            return;
        }

        foreach (var counter in counters)
        {
            context.Emitter.Writer.AppendLine(
                $"++{GSharpInternalNames.Lowering.IterationCounterPrefix}{counter};");
        }
    }

    public static void EmitSwitchSectionIncrement(SwitchSectionSyntax section, ref TranspileContext context)
    {
        if (!context.Emitter.Dialect.IsCuda ||
            context.Emitter.ActiveMaskIterations is not { } plan ||
            !plan.TryGetSwitchSectionCounter(section, out var counter))
        {
            return;
        }

        context.Emitter.Writer.AppendLine(
            $"++{GSharpInternalNames.Lowering.IterationCounterPrefix}{counter};");
    }
}
