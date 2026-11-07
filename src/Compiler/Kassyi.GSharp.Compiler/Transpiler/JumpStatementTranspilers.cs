namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles the jump statements (break, continue, goto) and labels to CUDA C++.</summary>
internal static class JumpStatementTranspilers
{
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

    /// <summary>
    /// Transpiles a goto statement: <c>goto case</c>, <c>goto default</c> or a goto to a label.
    /// </summary>
    public static void TranspileGotoStatement(GotoStatementSyntax node, ref TranspileContext context)
    {
        context.Emitter.Writer.Append("goto ");
        switch (node.Kind())
        {
            case SyntaxKind.GotoCaseStatement:
                WriteGotoCaseTarget(node, ref context);
                break;
            case SyntaxKind.GotoDefaultStatement:
                WriteGotoDefaultTarget(ref context);
                break;
            default:
                WriteGotoLabelTarget(node, ref context);
                break;
        }

        context.Emitter.Writer.AppendLine(";");
    }

    private static void WriteGotoCaseTarget(GotoStatementSyntax node, ref TranspileContext context)
    {
        if (node.Expression is { } value)
        {
            SwitchStateLabel.Append(value, ref context);
        }
    }

    private static void WriteGotoDefaultTarget(ref TranspileContext context) =>
        context.Emitter.Writer.Append(
            $"{GSharpInternalNames.Lowering.SwitchStatePrefix}{context.Emitter.CurrentSwitchId}_state_default"
        );

    private static void WriteGotoLabelTarget(GotoStatementSyntax node, ref TranspileContext context)
    {
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
    }

    /// <summary>
    /// Transpiles a labeled statement.
    /// </summary>
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
