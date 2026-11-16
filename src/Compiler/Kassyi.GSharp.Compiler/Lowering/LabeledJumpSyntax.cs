namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Syntax-only lookup of the target of a <c>break</c> or <c>continue</c> statement, with or without a label (#1990).
/// </summary>
internal static class LabeledJumpSyntax
{
    /// <summary>
    /// The label identifier of a labeled <c>break</c> or <c>continue</c>, or <see langword="null"/> for an unlabeled
    /// one.
    /// </summary>
    public static IdentifierNameSyntax? GetLabelName(StatementSyntax jump) =>
        // BreakStatementSyntax.Name and ContinueStatementSyntax.Name are [Experimental("RSEXPERIMENTAL006")] in
        // Microsoft.CodeAnalysis.CSharp 5.9.0 (dotnet/roslyn#83266); isolate the warning suppression here.
#pragma warning disable RSEXPERIMENTAL006
        jump switch
        {
            BreakStatementSyntax { Name: { } name } => name,
            ContinueStatementSyntax { Name: { } name } => name,
            _ => null,
        };
#pragma warning restore RSEXPERIMENTAL006

    /// <summary>
    /// Returns <paramref name="jump"/> with its target label identifier replaced by <paramref name="name"/>.
    /// </summary>
    public static StatementSyntax WithLabelName(StatementSyntax jump, IdentifierNameSyntax? name) =>
        // BreakStatementSyntax.WithName and ContinueStatementSyntax.WithName are [Experimental("RSEXPERIMENTAL006")] in
        // Microsoft.CodeAnalysis.CSharp 5.9.0 (dotnet/roslyn#83266); isolate the warning suppression here.
#pragma warning disable RSEXPERIMENTAL006
        jump switch
        {
            BreakStatementSyntax breakStmt => breakStmt.WithName(name),
            ContinueStatementSyntax continueStmt => continueStmt.WithName(name),
            _ => jump,
        };
#pragma warning restore RSEXPERIMENTAL006

    /// <summary>
    /// The loop or <c>switch</c> statement that <paramref name="jump"/> names: the nearest enclosing loop (or
    /// <c>switch</c> for <c>break</c>) in the same function whose immediate parent is a
    /// <see cref="LabeledStatementSyntax"/> with that identifier (only the immediate label counts);
    /// <see langword="null"/>
    /// when <paramref name="jump"/> is unlabeled or its target is not in the tree.
    /// </summary>
    public static StatementSyntax? FindTarget(StatementSyntax jump)
    {
        if (GetLabelName(jump) is not { } labelName)
        {
            return null;
        }

        var targetText = labelName.Identifier.ValueText;
        var includesSwitch = jump is BreakStatementSyntax;
        for (var current = jump.Parent; current != null && !IsFunction(current); current = current.Parent)
        {
            if (current is StatementSyntax statement &&
                IsBreakOrContinueTarget(statement, includesSwitch) &&
                statement.Parent is LabeledStatementSyntax labeled &&
                string.Equals(labeled.Identifier.ValueText, targetText, StringComparison.Ordinal))
            {
                return statement;
            }
        }

        return null;
    }

    /// <summary>
    /// The innermost enclosing loop (or <c>switch</c> for <c>break</c>) in the same function around
    /// <paramref name="jump"/>, ignoring any label on <paramref name="jump"/>.
    /// </summary>
    public static StatementSyntax? InnermostTarget(StatementSyntax jump)
    {
        var includesSwitch = jump is BreakStatementSyntax;
        for (var current = jump.Parent; current != null && !IsFunction(current); current = current.Parent)
        {
            if (current is StatementSyntax stmt && IsBreakOrContinueTarget(stmt, includesSwitch))
            {
                return stmt;
            }
        }

        return null;
    }

    /// <summary>
    /// The loop or <c>switch</c> statement that <paramref name="jump"/> leaves or continues: <see cref="FindTarget"/>
    /// when <paramref name="jump"/> is labeled, or <see cref="InnermostTarget"/> when <paramref name="jump"/> is
    /// unlabeled.
    /// </summary>
    public static StatementSyntax? FindEnclosingTarget(StatementSyntax jump) =>
        GetLabelName(jump) != null ? FindTarget(jump) : InnermostTarget(jump);

    internal static bool IsBreakOrContinueTarget(StatementSyntax statement, bool includesSwitch) =>
        statement is ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax ||
        (includesSwitch && statement is SwitchStatementSyntax);

    internal static bool IsFunction(SyntaxNode node) =>
        node is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax
            or AccessorDeclarationSyntax;
}
