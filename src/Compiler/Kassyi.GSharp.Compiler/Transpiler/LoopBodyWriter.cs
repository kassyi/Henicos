namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Emits the body of a loop statement (<c>for</c>, <c>while</c>, <c>do-while</c>, range <c>foreach</c>, constant-string
/// <c>foreach</c>) and places any C# 15 labeled-<c>continue</c> target label at the end of the loop body before the
/// closing brace (#1990).
/// </summary>
internal static class LoopBodyWriter
{
    /// <summary>
    /// Writes <paramref name="body"/> as the body of <paramref name="loop"/>. When <paramref name="loop"/> has no
    /// enclosing label that needs a continue target, delegates directly to
    /// <see cref="RoslynToCudaVisitor.VisitStatementOrBlock"/>; otherwise wraps the body in its own block followed by
    /// <c>__gsharp_LabeledContinue_&lt;id&gt;_&lt;name&gt;:;</c>.
    /// </summary>
    public static void Write(StatementSyntax loop, StatementSyntax body, ref TranspileContext context)
    {
        var hasCounter = context.Emitter.Dialect.IsCuda &&
                         context.Emitter.ActiveMaskIterations is { } plan &&
                         plan.TryGetLoopCounter(loop, out _);
        if (loop.Parent is not LabeledStatementSyntax labeled ||
            !LabeledJumps.PlanTarget(labeled, context.Query.SemanticModel).NeedsContinueLabel)
        {
            if (!hasCounter)
            {
                context.Visitor.VisitStatementOrBlock(body);
                return;
            }

            using (context.Emitter.Writer.Scope())
            {
                ActiveMaskIterationEmitter.EmitLoopIncrement(loop, ref context);
                WriteStatements(body, ref context);
            }

            return;
        }

        using (context.Emitter.Writer.Scope())
        {
            ActiveMaskIterationEmitter.EmitLoopIncrement(loop, ref context);
            WriteInnerBodyAndContinueLabel(labeled, body, ref context);
        }
    }

    /// <summary>
    /// Writes <paramref name="body"/> inside an already-open loop scope (converted-element range <c>foreach</c> and
    /// constant-string <c>foreach</c>).
    /// </summary>
    public static void WriteInOpenScope(
        StatementSyntax loop,
        StatementSyntax body,
        bool visitBlockDirectlyWhenUnlabeled,
        ref TranspileContext context)
    {
        ActiveMaskIterationEmitter.EmitLoopIncrement(loop, ref context);
        if (loop.Parent is not LabeledStatementSyntax labeled ||
            !LabeledJumps.PlanTarget(labeled, context.Query.SemanticModel).NeedsContinueLabel)
        {
            if (visitBlockDirectlyWhenUnlabeled)
            {
                WriteStatements(body, ref context);
            }
            else
            {
                context.Visitor.VisitStatementOrBlock(body);
            }

            return;
        }

        WriteInnerBodyAndContinueLabel(labeled, body, ref context);
    }

    private static void WriteInnerBodyAndContinueLabel(
        LabeledStatementSyntax labeled,
        StatementSyntax body,
        ref TranspileContext context)
    {
        using (context.Emitter.Writer.Scope())
        {
            WriteStatements(body, ref context);
        }

        LabeledJumps.AppendContinueLabel(labeled, ref context);
        context.Emitter.Writer.AppendLine(":;");
    }

    private static void WriteStatements(StatementSyntax body, ref TranspileContext context)
    {
        if (body is BlockSyntax block)
        {
            context.Visitor.VisitBlock(block);
        }
        else
        {
            ActiveMaskIterationEmitter.EmitResets(body, ref context);
            context.Visitor.Visit(body);
            context.Emitter.Writer.AppendLine();
        }
    }
}
