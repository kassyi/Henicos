namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles the switch statement, its sections and the jump statements (break, continue, goto, labels) to CUDA C++.</summary>
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

    /// <summary>
    /// Transpiles a break statement.
    /// </summary>
    public static void TranspileBreakStatement(BreakStatementSyntax node, ref TranspileContext context) =>
        context.Emitter.Writer.Append("break;");

    /// <summary>
    /// Transpiles a continue statement.
    /// </summary>
    public static void TranspileContinueStatement(ContinueStatementSyntax node, ref TranspileContext context) =>
        context.Emitter.Writer.Append("continue;");

    public static void TranspileGotoStatement(GotoStatementSyntax node, ref TranspileContext context)
    {
        if (node.IsKind(SyntaxKind.GotoCaseStatement))
        {
            context.Emitter.Writer.Append("goto ");
            if (node.Expression is { } value)
            {
                SwitchStateLabel.Append(value, ref context);
            }

            context.Emitter.Writer.AppendLine(";");
            return;
        }
        else if (node.IsKind(SyntaxKind.GotoDefaultStatement))
        {
            context.Emitter.Writer.Append("goto ");
            context.Emitter.Writer.AppendLine(
                $"{GSharpInternalNames.Lowering.SwitchStatePrefix}{context.Emitter.CurrentSwitchId}_state_default;"
            );
            return;
        }

        context.Emitter.Writer.Append("goto ");
        if (node.Expression is IdentifierNameSyntax identifier &&
            !IsSyntheticLabel(identifier.Identifier.ValueText))
        {
            CppNameResolver.AppendReferencedName(identifier, identifier.Identifier, ref context);
            if (context.Emitter.LabelSuffix != null)
            {
                context.Emitter.Writer.Append(context.Emitter.LabelSuffix);
            }
        }
        else
        {
            context.Visitor.Visit(node.Expression);
        }

        context.Emitter.Writer.AppendLine(";");
    }

    public static void TranspileLabeledStatement(LabeledStatementSyntax node, ref TranspileContext context)
    {
        CppNameResolver.AppendDeclaredName(node, node.Identifier, ref context);
        if (context.Emitter.LabelSuffix != null && !IsSyntheticLabel(node.Identifier.ValueText))
        {
            context.Emitter.Writer.Append(context.Emitter.LabelSuffix);
        }

        if (node.Statement is EmptyStatementSyntax)
        {
            context.Emitter.Writer.AppendLine(":;");
            return;
        }

        context.Emitter.Writer.AppendLine(":");
        context.Visitor.Visit(node.Statement);
    }

    // Labels a compiler pass names uniquely by itself: written as they are, without the label suffix of an inlined copy
    private static bool IsSyntheticLabel(string name) =>
        name.StartsWith(GSharpInternalNames.Iterator.EndLabelPrefix, StringComparison.Ordinal) ||
        name.StartsWith(GSharpInternalNames.Iterator.NextLabelPrefix, StringComparison.Ordinal) ||
        name.StartsWith(GSharpInternalNames.Lowering.SyncGridNextLabelPrefix, StringComparison.Ordinal);
}
