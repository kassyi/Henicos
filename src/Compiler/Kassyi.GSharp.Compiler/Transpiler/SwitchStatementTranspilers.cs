namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles the switch statement and its sections to CUDA C++.</summary>
internal static class SwitchStatementTranspilers
{
    /// <summary>
    /// Transpiles a switch statement and its case sections into C++ switch-case syntax.
    /// </summary>
    /// <remarks>
    /// A local that one section declares and another uses is declared in a block around the switch (#795):
    /// <c>{ &lt;declarations&gt; switch (...) { ... } }</c>.
    /// </remarks>
    public static void TranspileSwitchStatement(SwitchStatementSyntax node, ref TranspileContext context)
    {
        if (!SwitchSectionLocals.ReportAndFindSharedLocals(node, ref context))
        {
            TranspileSwitchBlock(node, ref context);
            return;
        }

        using (context.Emitter.Writer.Scope())
        {
            SwitchSectionLocals.WriteDeclarations(node, ref context);
            TranspileSwitchBlock(node, ref context);
        }
    }

    private static void TranspileSwitchBlock(SwitchStatementSyntax node, ref TranspileContext context)
    {
        StatementPrelude.Emit(node.Expression, ref context);
        var switchId = ++context.Emitter.SwitchIdCounter;
        context.Emitter.ActiveSwitchIds.Push(switchId);

        context.Emitter.Writer.Append("switch");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Expression);
        }

        context.Emitter.Writer.AppendLine();
        using (context.Emitter.Writer.Scope())
        {
            foreach (var section in node.Sections)
            {
                context.Visitor.Visit(section);
            }
        }

        context.Emitter.ActiveSwitchIds.Pop();
    }

    /// <summary>
    /// Transpiles the labels and statements within a switch case section.
    /// </summary>
    public static void TranspileSwitchSection(SwitchSectionSyntax node, ref TranspileContext context)
    {
        foreach (var label in node.Labels)
        {
            context.Visitor.Visit(label);
            context.Emitter.Writer.AppendLine();
        }

        using (context.Emitter.Writer.Scope())
        {
            JumpedOverDeclarations.WriteDeclarations(node.Statements, ref context);
            foreach (var stmt in node.Statements)
            {
                if (stmt is BlockSyntax nested &&
                    nested.HasAnnotations(LoweringAnnotations.ScopedBlockAnnotationKind))
                {
                    context.Visitor.VisitStatementOrBlock(nested);
                }
                else
                {
                    context.Visitor.Visit(stmt);
                }

                context.Emitter.Writer.AppendLine();
            }
        }
    }
}
