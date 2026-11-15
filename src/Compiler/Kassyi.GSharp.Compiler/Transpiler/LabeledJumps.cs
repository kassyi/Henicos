namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Plans and formats the C++ <c>goto</c> targets for C# 15 labeled <c>break</c> and <c>continue</c> statements (#1990).
/// </summary>
internal static class LabeledJumps
{
    /// <summary>
    /// The label identifier of a labeled <c>break</c> or <c>continue</c>, or <see langword="null"/> for an unlabeled one.
    /// </summary>
    public static IdentifierNameSyntax? GetLabelName(StatementSyntax jump) =>
        LabeledJumpSyntax.GetLabelName(jump);

    /// <summary>
    /// The loop or <c>switch</c> that <paramref name="jump"/> names: the nearest ancestor whose immediate parent is a
    /// label of that name (only the immediate label counts); <see langword="null"/> when it is not in the tree.
    /// </summary>
    public static StatementSyntax? FindTarget(StatementSyntax jump) =>
        LabeledJumpSyntax.FindTarget(jump);

    /// <summary>
    /// Decides whether <paramref name="jump"/> is emitted as a plain C++ <c>break;</c>/<c>continue;</c> or as a
    /// <c>goto</c> to the target label's break or continue label.
    /// </summary>
    public static LabeledJumpPlan PlanJump(StatementSyntax jump, SemanticModel model)
    {
        var isBreak = jump is BreakStatementSyntax;
        if (GetLabelName(jump) is not { } name)
        {
            return new LabeledJumpPlan(isBreak, LabeledJumpForm.Plain, null, 0);
        }

        var target = FindTarget(jump);
        if (target != null && target == LabeledJumpSyntax.InnermostTarget(jump))
        {
            return new LabeledJumpPlan(isBreak, LabeledJumpForm.Plain, null, 0);
        }

        var symbol = ((target?.Parent is LabeledStatementSyntax labeled
                          ? model.GetDeclaredSymbol<ILabelSymbol>(labeled)
                          : null) ??
                      model.GetSymbol(name))?.OriginalDefinition as ILabelSymbol;
        var region = LoweringIdentity.GetRegion(name);
        if (region == 0 && target?.Parent is LabeledStatementSyntax targetLabeled)
        {
            region = LoweringIdentity.GetRegion(targetLabeled);
        }

        return new LabeledJumpPlan(isBreak, LabeledJumpForm.Goto, symbol, region);
    }

    /// <summary>
    /// Which labels the statement of <paramref name="labeled"/> needs: whether a <see cref="LabeledJumpForm.Goto"/>-form
    /// <c>break</c> or <c>continue</c> names it (none for an iterator <c>foreach</c>, whose jumps
    /// <see cref="BranchTargetRewriter"/> rewrites).
    /// </summary>
    public static LabeledTargetPlan PlanTarget(LabeledStatementSyntax labeled, SemanticModel model)
    {
        var statement = labeled.Statement;
        if (!LabeledJumpSyntax.IsBreakOrContinueTarget(statement, includesSwitch: true))
        {
            return default;
        }

        if (statement is ForEachStatementSyntax forEach &&
            IteratorTypeResolver.TryResolveIteratorSource(forEach.Expression, model, out _, out _))
        {
            return default;
        }

        var needsBreak = false;
        var needsContinue = false;
        foreach (var node in statement.DescendantNodes(static n => !LabeledJumpSyntax.IsFunction(n)))
        {
            if (node is not (BreakStatementSyntax or ContinueStatementSyntax) ||
                GetLabelName((StatementSyntax)node) is null ||
                FindTarget((StatementSyntax)node) != statement ||
                statement == LabeledJumpSyntax.InnermostTarget((StatementSyntax)node))
            {
                continue;
            }

            if (node is BreakStatementSyntax)
            {
                needsBreak = true;
            }
            else
            {
                needsContinue = true;
            }

            if (needsBreak && needsContinue)
            {
                break;
            }
        }

        return new LabeledTargetPlan(needsBreak, needsContinue);
    }

    /// <summary>
    /// Appends <c>__gsharp_LabeledBreak_&lt;id&gt;_&lt;name&gt;[&lt;LabelSuffix&gt;]</c> or
    /// <c>__gsharp_LabeledContinue_&lt;id&gt;_&lt;name&gt;[&lt;LabelSuffix&gt;]</c> for a planned jump.
    /// </summary>
    public static void AppendJumpTarget(in LabeledJumpPlan plan, StatementSyntax jump, ref TranspileContext context)
    {
        var prefix = plan.IsBreak
            ? GSharpInternalNames.Lowering.LabeledBreakLabelPrefix
            : GSharpInternalNames.Lowering.LabeledContinueLabelPrefix;
        var fallbackName = plan.Label?.Name ?? GetLabelName(jump)?.Identifier.ValueText ?? string.Empty;
        AppendLabel(prefix, plan.Label, fallbackName, plan.Region, ref context);
    }

    /// <summary>
    /// Appends <c>__gsharp_LabeledBreak_&lt;id&gt;_&lt;name&gt;[&lt;LabelSuffix&gt;]</c> for <paramref name="labeled"/>.
    /// </summary>
    public static void AppendBreakLabel(LabeledStatementSyntax labeled, ref TranspileContext context)
    {
        var symbol = context.Query.SemanticModel.GetDeclaredSymbol<ILabelSymbol>(labeled);
        AppendLabel(
            GSharpInternalNames.Lowering.LabeledBreakLabelPrefix,
            symbol,
            labeled.Identifier.ValueText,
            LoweringIdentity.GetRegion(labeled),
            ref context);
    }

    /// <summary>
    /// Appends <c>__gsharp_LabeledContinue_&lt;id&gt;_&lt;name&gt;[&lt;LabelSuffix&gt;]</c> for <paramref name="labeled"/>.
    /// </summary>
    public static void AppendContinueLabel(LabeledStatementSyntax labeled, ref TranspileContext context)
    {
        var symbol = context.Query.SemanticModel.GetDeclaredSymbol<ILabelSymbol>(labeled);
        AppendLabel(
            GSharpInternalNames.Lowering.LabeledContinueLabelPrefix,
            symbol,
            labeled.Identifier.ValueText,
            LoweringIdentity.GetRegion(labeled),
            ref context);
    }

    private static void AppendLabel(
        string prefix,
        ILabelSymbol? symbol,
        string name,
        int region,
        ref TranspileContext context)
    {
        var cppName = CppNameResolver.Resolve(symbol, name, context.Emitter.Dialect, region);
        context.Emitter.Writer.Append(prefix).AppendCppName(cppName);
        if (context.Emitter.LabelSuffix != null)
        {
            context.Emitter.Writer.Append(context.Emitter.LabelSuffix);
        }
    }
}
