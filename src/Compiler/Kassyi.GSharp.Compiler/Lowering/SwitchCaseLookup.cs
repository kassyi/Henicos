using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Matches a <c>goto case</c> or <c>goto default</c> statement to its target section or lowered section label in a
/// switch whose labels are constant cases and <c>default</c>.
/// </summary>
internal static class SwitchCaseLookup
{
    /// <summary>
    /// Collects the case targets and the default section index of <paramref name="switchStatement"/>.
    /// </summary>
    internal static ImmutableArray<SwitchCaseTarget> CollectTargets(
        SwitchStatementSyntax switchStatement,
        SemanticModel? semanticModel,
        out int defaultSectionIndex)
    {
        defaultSectionIndex = -1;
        var targets = ImmutableArray.CreateBuilder<SwitchCaseTarget>();
        for (var i = 0; i < switchStatement.Sections.Count; i++)
        {
            foreach (var label in switchStatement.Sections[i].Labels)
            {
                if (label is CaseSwitchLabelSyntax caseLabel)
                {
                    targets.Add(
                        new SwitchCaseTarget(
                            semanticModel?.GetDeclaredSymbol<ILabelSymbol>(caseLabel),
                            caseLabel.Value,
                            i,
                            string.Empty
                        )
                    );
                }
                else if (label is DefaultSwitchLabelSyntax)
                {
                    defaultSectionIndex = i;
                }
            }
        }

        return targets.ToImmutable();
    }

    /// <summary>
    /// Returns a delegate mapping <c>goto case</c> / <c>goto default</c> of <paramref name="switchStatement"/> to its
    /// section index.
    /// </summary>
    internal static Func<GotoStatementSyntax, int> CreateSectionLookup(
        SwitchStatementSyntax switchStatement,
        SemanticModel? semanticModel)
    {
        var targets = CollectTargets(switchStatement, semanticModel, out var defaultSectionIndex);
        return jump => FindSection(jump, defaultSectionIndex, targets, semanticModel);
    }

    /// <summary>
    /// Finds the zero-based section index targeted by <paramref name="node"/>, or <c>-1</c> if unresolved.
    /// </summary>
    internal static int FindSection(
        GotoStatementSyntax node,
        int defaultSectionIndex,
        ImmutableArray<SwitchCaseTarget> caseTargets,
        SemanticModel? semanticModel) =>
        node.IsKind(SyntaxKind.GotoDefaultStatement)
            ? defaultSectionIndex
            : FindCaseTarget(node, caseTargets, semanticModel)?.SectionIndex ?? -1;

    /// <summary>
    /// Finds the lowered section label targeted by <paramref name="node"/>, or <see langword="null"/> if unresolved.
    /// </summary>
    internal static string? FindTargetLabel(
        GotoStatementSyntax node,
        string? defaultLabel,
        ImmutableArray<SwitchCaseTarget> caseTargets,
        SemanticModel? semanticModel) =>
        node.IsKind(SyntaxKind.GotoDefaultStatement)
            ? defaultLabel
            : FindCaseTarget(node, caseTargets, semanticModel)?.Label;

    // [WHY] C# binds goto case to the case label whose constant equals its value converted to the governing type
    // (goto case 1.5 reaches case 1 of an int switch, goto case 0.1 reaches case 0.1f of a float switch); the label
    // symbol that Roslyn binds (IBranchOperation.Target) is that case label's declared symbol, so no conversion is
    // repeated here. Without a semantic model, or when the goto does not resolve, equivalent case expressions match.
    private static SwitchCaseTarget? FindCaseTarget(
        GotoStatementSyntax node,
        ImmutableArray<SwitchCaseTarget> caseTargets,
        SemanticModel? semanticModel)
    {
        if (semanticModel?.GetSafeOperation(node) is IBranchOperation { Target: { } target } &&
            caseTargets
                .AsStackEnumerable()
                .TryFirst(target, static (c, t) => c.Symbol.IsEqualTo(t), out var bound))
        {
            return bound;
        }

        return node.Expression is { } value &&
               caseTargets
                   .AsStackEnumerable()
                   .TryFirst(value, static (c, v) => SyntaxFactory.AreEquivalent(c.Value, v), out var equivalent)
            ? equivalent
            : null;
    }
}
