using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Lowering;

internal enum JumpRegionKind
{
    ForwardRange,
    GotoLoop
}

internal sealed record JumpRegion(
    JumpRegionKind Kind,
    int LabelIndex,
    int First,
    int Last);

internal sealed record JumpRegionPlan(
    ImmutableArray<JumpRegion> Regions,
    ImmutableArray<GotoStatementSyntax> Rejected)
{
    public static readonly JumpRegionPlan Empty =
        new(ImmutableArray<JumpRegion>.Empty, ImmutableArray<GotoStatementSyntax>.Empty);
}

internal readonly record struct CrossSectionRejectedGoto(
    GotoStatementSyntax Goto,
    string TargetLabelName,
    string RegionLabelName);

internal static class JumpRegionPlanner
{
    public static JumpRegionPlan Plan(
        SyntaxList<StatementSyntax> statements,
        Func<StatementSyntax, bool> holdsSplitPoint)
    {
        if (statements.Count == 0 || !HasAnyLabel(statements))
        {
            return JumpRegionPlan.Empty;
        }

        var canonicalIndices = BuildCanonicalLabelIndices(statements);
        var minFirst = new int[statements.Count];
        var maxLast = new int[statements.Count];
        for (var i = 0; i < statements.Count; i++)
        {
            minFirst[i] = -1;
            maxLast[i] = -1;
        }

        var allGotos = new List<(int K, int L, GotoStatementSyntax Statement)>();

        for (var k = 0; k < statements.Count; k++)
        {
            var gotos = new List<GotoStatementSyntax>();
            CollectGotos(statements[k], gotos);

            foreach (var gotoStmt in gotos)
            {
                if (gotoStmt.Expression is not IdentifierNameSyntax { Identifier.ValueText: var labelName })
                {
                    continue;
                }

                if (labelName.StartsWith(GSharpInternalNames.Lowering.SyncGridNextLabelPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var targetLabel = ReconvergenceSyntax.FindLabel(gotoStmt, labelName);
                if (targetLabel == null)
                {
                    continue;
                }

                var rawL = FindStatementIndexForLabel(statements, targetLabel);
                if (rawL < 0)
                {
                    continue;
                }

                var l = canonicalIndices[rawL];

                allGotos.Add((k, l, gotoStmt));
                if (k < l)
                {
                    if (minFirst[l] == -1 || k < minFirst[l])
                    {
                        minFirst[l] = k;
                    }
                }
                else
                {
                    if (k > maxLast[l])
                    {
                        maxLast[l] = k;
                    }
                }
            }
        }

        var adopted = new List<CandidateRange>();
        for (var l = 0; l < statements.Count; l++)
        {
            var first = minFirst[l];
            if (first >= 0 && HoldsSplitPoint(statements, first, l - 1, holdsSplitPoint))
            {
                adopted.Add(new CandidateRange(JumpRegionKind.ForwardRange, l, first, l - 1));
            }

            var last = maxLast[l];
            if (last >= 0 && HoldsSplitPoint(statements, l, last, holdsSplitPoint))
            {
                adopted.Add(new CandidateRange(JumpRegionKind.GotoLoop, l, l, last));

                // A label with a GotoLoop and a forward goto must still close the forward range in front of the loop
                if (first >= 0 && !HasForwardRangeForLabel(adopted, l))
                {
                    adopted.Add(new CandidateRange(JumpRegionKind.ForwardRange, l, first, l - 1));
                }
            }
        }

        if (adopted.Count == 0)
        {
            return JumpRegionPlan.Empty;
        }

        // Expand crossing ranges, expand forward ranges that exit a GotoLoop, and adopt a forward range without a split
        // point whose goto comes from before an adopted forward range and whose label lies in its middle, until
        // nothing changes
        var changed = true;
        while (changed)
        {
            changed = AdoptRangesIntoMiddle(minFirst, adopted);
            changed |= AdoptRangesExitingLoop(minFirst, adopted);
            for (var i = 0; i < adopted.Count; i++)
            {
                if (adopted[i].Kind == JumpRegionKind.ForwardRange)
                {
                    for (var j = 0; j < adopted.Count; j++)
                    {
                        if (i == j || adopted[j].Kind != JumpRegionKind.ForwardRange)
                        {
                            continue;
                        }

                        var a1 = adopted[i].First;
                        var l1 = adopted[i].LabelIndex;
                        var a2 = adopted[j].First;
                        var l2 = adopted[j].LabelIndex;

                        if (a1 < a2 && a2 < l1 && l1 < l2)
                        {
                            adopted[j].First = a1;
                            changed = true;
                        }
                    }
                }
                else
                {
                    var l = adopted[i].First;
                    var j = adopted[i].Last;
                    for (var f = 0; f < adopted.Count; f++)
                    {
                        if (adopted[f].Kind != JumpRegionKind.ForwardRange)
                        {
                            continue;
                        }

                        var a = adopted[f].First;
                        var m = adopted[f].LabelIndex;
                        if (l < a && a <= j && j < m)
                        {
                            adopted[f].First = l;
                            changed = true;
                        }
                    }
                }
            }
        }

        var rejected = ImmutableArray.CreateBuilder<GotoStatementSyntax>();
        foreach (var (k, l, gotoStmt) in allGotos)
        {
            foreach (var range in adopted)
            {
                if (l > range.First && l <= range.Last && (k < range.First || k > range.Last))
                {
                    if (!rejected.Contains(gotoStmt))
                    {
                        rejected.Add(gotoStmt);
                    }

                    break;
                }
            }
        }

        adopted.Sort(static (r1, r2) =>
        {
            var cmp = r1.First.CompareTo(r2.First);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = r2.Last.CompareTo(r1.Last);
            if (cmp != 0)
            {
                return cmp;
            }

            return r1.Kind.CompareTo(r2.Kind);
        });

        var regions = ImmutableArray.CreateBuilder<JumpRegion>(adopted.Count);
        foreach (var r in adopted)
        {
            regions.Add(new JumpRegion(r.Kind, r.LabelIndex, r.First, r.Last));
        }

        return new JumpRegionPlan(regions.ToImmutable(), rejected.ToImmutable());
    }

    private static bool HoldsSplitPoint(
        SyntaxList<StatementSyntax> statements,
        int start,
        int end,
        Func<StatementSyntax, bool> holdsSplitPoint)
    {
        for (var s = start; s <= end; s++)
        {
            if (holdsSplitPoint(statements[s]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasForwardRangeForLabel(List<CandidateRange> adopted, int labelIndex) =>
        adopted
            .AsStackEnumerable()
            .Any(labelIndex, static (range, label) =>
                range.Kind == JumpRegionKind.ForwardRange && range.LabelIndex == label
            );

    // A label l whose earliest forward goto k lies before an adopted ForwardRange [First, Last] with First < l <= Last
    private static bool AdoptRangesIntoMiddle(int[] minFirst, List<CandidateRange> adopted)
    {
        var changed = false;
        for (var l = 0; l < minFirst.Length; l++)
        {
            var k = minFirst[l];
            if (k < 0 || HasForwardRangeForLabel(adopted, l))
            {
                continue;
            }

            var entersForwardRange = adopted
                .AsStackEnumerable()
                .Any((k, l), static (range, jump) =>
                    range.Kind == JumpRegionKind.ForwardRange &&
                    jump.k < range.First &&
                    range.First < jump.l &&
                    jump.l <= range.Last
                );
            if (!entersForwardRange)
            {
                continue;
            }

            adopted.Add(new CandidateRange(JumpRegionKind.ForwardRange, l, k, l - 1));
            changed = true;
        }

        return changed;
    }

    // A label m whose earliest forward goto a starts inside an adopted GotoLoop [First, Last] and lands after it
    // (First <= a <= Last < m): the lanes that jump skip the split points of the loop, so the range must wrap it
    private static bool AdoptRangesExitingLoop(int[] minFirst, List<CandidateRange> adopted)
    {
        var changed = false;
        for (var m = 0; m < minFirst.Length; m++)
        {
            var a = minFirst[m];
            if (a < 0 || HasForwardRangeForLabel(adopted, m))
            {
                continue;
            }

            foreach (var range in adopted)
            {
                if (range.Kind == JumpRegionKind.GotoLoop &&
                    range.First <= a &&
                    a <= range.Last &&
                    range.Last < m)
                {
                    adopted.Add(new CandidateRange(JumpRegionKind.ForwardRange, m, range.First, m - 1));
                    changed = true;
                    break;
                }
            }
        }

        return changed;
    }

    private static bool HasAnyLabel(SyntaxList<StatementSyntax> statements)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is LabeledStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectGotos(StatementSyntax statement, List<GotoStatementSyntax> gotos)
    {
        if (statement is GotoStatementSyntax directGoto && directGoto.IsKind(SyntaxKind.GotoStatement))
        {
            gotos.Add(directGoto);
            return;
        }

        foreach (var node in statement.DescendantNodes(static n =>
                     n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
        {
            if (node is GotoStatementSyntax gotoStmt && gotoStmt.IsKind(SyntaxKind.GotoStatement))
            {
                gotos.Add(gotoStmt);
            }
        }
    }

    internal static ImmutableArray<CrossSectionRejectedGoto> FindCrossSectionRejectedGotos(
        SwitchStatementSyntax switchStatement,
        JumpRegionPlan[] sectionPlans,
        Func<GotoStatementSyntax, bool>? isIncluded = null)
    {
        var rejected = ImmutableArray.CreateBuilder<CrossSectionRejectedGoto>();
        for (var s = 0; s < switchStatement.Sections.Count; s++)
        {
            var section = switchStatement.Sections[s];
            foreach (var node in section.DescendantNodes(static n =>
                         n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
            {
                if (node is not GotoStatementSyntax gotoStmt ||
                    !gotoStmt.IsKind(SyntaxKind.GotoStatement) ||
                    gotoStmt.Expression is not IdentifierNameSyntax { Identifier.ValueText: var labelName })
                {
                    continue;
                }

                if (isIncluded != null && !isIncluded(gotoStmt))
                {
                    continue;
                }

                if (ReconvergenceSyntax.FindLabel(gotoStmt, labelName) is not { } targetLabel)
                {
                    continue;
                }

                for (var t = 0; t < switchStatement.Sections.Count; t++)
                {
                    if (t == s)
                    {
                        continue;
                    }

                    var targetSection = switchStatement.Sections[t];
                    var l = FindCanonicalStatementIndexForLabel(targetSection.Statements, targetLabel);
                    if (l < 0)
                    {
                        continue;
                    }

                    foreach (var range in sectionPlans[t].Regions)
                    {
                        // GotoLoop opens before the first statement's labels, so entering at First skips its enter;
                        // ForwardRange opens after them unless a GotoLoop also starts there (caught by its own entry)
                        var enters = range.Kind == JumpRegionKind.GotoLoop
                            ? l >= range.First && l <= range.Last
                            : l > range.First && l <= range.Last;
                        if (enters)
                        {
                            var rangeLabel = FindRegionLabelName(targetSection.Statements, range);
                            rejected.Add(new CrossSectionRejectedGoto(gotoStmt, labelName, rangeLabel));
                            break;
                        }
                    }

                    break;
                }
            }
        }

        return rejected.Count == 0 ? ImmutableArray<CrossSectionRejectedGoto>.Empty : rejected.ToImmutable();
    }

    internal static bool IsEmptyLabeledStatement(StatementSyntax statement)
    {
        if (statement is not LabeledStatementSyntax)
        {
            return false;
        }

        while (statement is LabeledStatementSyntax labeled)
        {
            statement = labeled.Statement;
        }

        return statement is EmptyStatementSyntax;
    }

    internal static int[] BuildCanonicalLabelIndices(SyntaxList<StatementSyntax> statements)
    {
        var canonical = new int[statements.Count];
        for (var i = 0; i < statements.Count; i++)
        {
            canonical[i] = i > 0 &&
                           statements[i] is LabeledStatementSyntax &&
                           IsEmptyLabeledStatement(statements[i - 1])
                ? canonical[i - 1]
                : i;
        }

        return canonical;
    }

    internal static int FindCanonicalStatementIndexForLabel(
        SyntaxList<StatementSyntax> statements,
        LabeledStatementSyntax targetLabel,
        int[]? canonicalIndices = null)
    {
        var rawIndex = FindStatementIndexForLabel(statements, targetLabel);
        if (rawIndex < 0)
        {
            return -1;
        }

        if (canonicalIndices != null)
        {
            return canonicalIndices[rawIndex];
        }

        var index = rawIndex;
        while (index > 0 &&
               statements[index] is LabeledStatementSyntax &&
               IsEmptyLabeledStatement(statements[index - 1]))
        {
            index--;
        }

        return index;
    }

    internal static string FindRegionLabelName(SyntaxList<StatementSyntax> statements, JumpRegion range)
    {
        if (range.LabelIndex < 0 || range.LabelIndex >= statements.Count)
        {
            return string.Empty;
        }

        var groupLabels = new List<LabeledStatementSyntax>();
        for (var i = range.LabelIndex; i < statements.Count; i++)
        {
            for (var cur = statements[i]; cur is LabeledStatementSyntax labeled; cur = labeled.Statement)
            {
                groupLabels.Add(labeled);
            }

            if (!IsEmptyLabeledStatement(statements[i]))
            {
                break;
            }
        }

        if (groupLabels.Count == 0)
        {
            return string.Empty;
        }

        if (groupLabels.Count > 1)
        {
            var start = range.First;
            var end = range.Kind == JumpRegionKind.GotoLoop ? range.Last : range.LabelIndex - 1;
            var gotos = new List<GotoStatementSyntax>();
            for (var k = start; k <= end; k++)
            {
                gotos.Clear();
                CollectGotos(statements[k], gotos);
                foreach (var gotoStmt in gotos)
                {
                    if (gotoStmt.Expression is IdentifierNameSyntax { Identifier.ValueText: var name } &&
                        ReconvergenceSyntax.FindLabel(gotoStmt, name) is { } targetLabel &&
                        groupLabels.Contains(targetLabel))
                    {
                        return targetLabel.Identifier.ValueText;
                    }
                }
            }
        }

        return groupLabels[0].Identifier.ValueText;
    }

    internal static int FindStatementIndexForLabel(
        SyntaxList<StatementSyntax> statements,
        LabeledStatementSyntax targetLabel)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            for (var cur = statements[i]; cur is LabeledStatementSyntax labeled; cur = labeled.Statement)
            {
                if (labeled == targetLabel)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private sealed class CandidateRange(JumpRegionKind kind, int labelIndex, int first, int last)
    {
        public JumpRegionKind Kind { get; } = kind;
        public int LabelIndex { get; } = labelIndex;
        public int First { get; set; } = first;
        public int Last { get; } = last;
    }
}
