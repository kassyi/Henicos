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

internal sealed record RejectedJump(
    GotoStatementSyntax Statement,
    string TargetLabelName,
    string RegionLabelName);

internal sealed record JumpRegionPlan(
    ImmutableArray<JumpRegion> Regions,
    ImmutableArray<GotoStatementSyntax> Rejected,
    ImmutableArray<RejectedJump> RejectedDetails = default)
{
    public static readonly JumpRegionPlan Empty =
        new(
            ImmutableArray<JumpRegion>.Empty,
            ImmutableArray<GotoStatementSyntax>.Empty,
            ImmutableArray<RejectedJump>.Empty);
}

internal static class JumpRegionPlanner
{
    public static JumpRegionPlan Plan(
        SyntaxList<StatementSyntax> statements,
        Func<StatementSyntax, bool> holdsSplitPoint,
        bool splitStackedLabels = false,
        Func<GotoStatementSyntax, bool>? isRemovedGoto = null,
        bool groupDetachedLabels = false)
    {
        if (statements.Count == 0 || !HasAnyLabel(statements))
        {
            return JumpRegionPlan.Empty;
        }

        var slots = BuildSlots(statements, holdsSplitPoint, splitStackedLabels, groupDetachedLabels);
        var minFirst = new int[slots.Length];
        var maxLast = new int[slots.Length];
        var labelNames = new string?[slots.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            minFirst[i] = -1;
            maxLast[i] = -1;
        }

        var allGotos = new List<(int K, int L, GotoStatementSyntax Statement, string LabelName)>();

        for (var k = 0; k < slots.Length; k++)
        {
            if (slots[k].Body is not { } bodyStatement)
            {
                continue;
            }

            var gotos = new List<GotoStatementSyntax>();
            CollectGotos(bodyStatement, gotos, isRemovedGoto);

            foreach (var gotoStmt in gotos)
            {
                if (gotoStmt.Expression is not IdentifierNameSyntax { Identifier.ValueText: var labelName })
                {
                    continue;
                }

                if (labelName.StartsWith(
                    GSharpInternalNames.Lowering.SyncGridNextLabelPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var targetLabel = ReconvergenceSyntax.FindLabel(gotoStmt, labelName);
                if (targetLabel == null)
                {
                    continue;
                }

                var l = FindSlotIndexForLabel(slots, targetLabel);
                if (l < 0)
                {
                    continue;
                }

                labelNames[l] ??= targetLabel.Identifier.ValueText;

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

                allGotos.Add((k, l, gotoStmt, labelName));
            }
        }

        var adopted = new List<CandidateRange>();
        for (var l = 0; l < slots.Length; l++)
        {
            var first = minFirst[l];
            if (first >= 0 && HasSplitPointInRange(slots, first, l - 1))
            {
                adopted.Add(
                    new CandidateRange(
                        JumpRegionKind.ForwardRange,
                        l,
                        first,
                        l - 1,
                        labelNames[l] ?? GetFirstLabelName(slots[l])));
            }

            var last = maxLast[l];
            if (last >= 0 && HasSplitPointInRange(slots, l, last))
            {
                var regionLabel = labelNames[l] ?? GetFirstLabelName(slots[l]);
                adopted.Add(new CandidateRange(JumpRegionKind.GotoLoop, l, l, last, regionLabel));

                // If a GotoLoop has forward gotos to its start label, adopt a ForwardRange so those gotos land at a
                // join point before enter(GotoLoop); L: ;
                if (first >= 0 && !HasForwardRangeAtLabel(adopted, l))
                {
                    adopted.Add(
                        new CandidateRange(JumpRegionKind.ForwardRange, l, first, l - 1, regionLabel));
                }
            }
        }

        if (adopted.Count == 0)
        {
            return JumpRegionPlan.Empty;
        }

        // Expand crossing ForwardRanges (including ForwardRanges that exit a GotoLoop), and adopt ForwardRanges
        // without split points whose goto starts before an adopted ForwardRange and targets its middle, until
        // fixed point
        var changed = true;
        while (changed)
        {
            changed = AdoptRangesIntoMiddle(minFirst, slots, labelNames, adopted);
            for (var i = 0; i < adopted.Count; i++)
            {
                for (var j = 0; j < adopted.Count; j++)
                {
                    if (i == j || adopted[j].Kind != JumpRegionKind.ForwardRange)
                    {
                        continue;
                    }

                    var a1 = adopted[i].First;
                    var a2 = adopted[j].First;
                    var l2 = adopted[j].LabelIndex;

                    if (adopted[i].Kind == JumpRegionKind.ForwardRange)
                    {
                        var l1 = adopted[i].LabelIndex;
                        if (a1 < a2 && a2 < l1 && l1 < l2)
                        {
                            adopted[j].First = a1;
                            changed = true;
                        }
                    }
                    else
                    {
                        var end1 = adopted[i].Last;
                        if (a1 < a2 && a2 <= end1 && end1 < l2)
                        {
                            adopted[j].First = a1;
                            changed = true;
                        }
                    }
                }
            }
        }

        var rejected = ImmutableArray.CreateBuilder<GotoStatementSyntax>();
        var rejectedDetails = ImmutableArray.CreateBuilder<RejectedJump>();
        foreach (var (k, l, gotoStmt, targetName) in allGotos)
        {
            foreach (var range in adopted)
            {
                if (l > range.First && l <= range.Last && (k < range.First || k > range.Last))
                {
                    if (!rejected.Contains(gotoStmt))
                    {
                        rejected.Add(gotoStmt);
                        rejectedDetails.Add(new RejectedJump(gotoStmt, targetName, range.LabelName));
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

        return new JumpRegionPlan(regions.ToImmutable(), rejected.ToImmutable(), rejectedDetails.ToImmutable());
    }

    internal static ImmutableArray<RejectedJump> FindRejectedCrossSectionGotos(
        SwitchStatementSyntax switchStatement,
        JumpRegionPlan[] sectionPlans,
        bool splitStackedLabels = false,
        Func<StatementSyntax, bool>? holdsSplitPoint = null,
        Func<GotoStatementSyntax, bool>? isRemovedGoto = null,
        bool groupDetachedLabels = false)
    {
        var rejected = ImmutableArray.CreateBuilder<RejectedJump>();
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

                if (isRemovedGoto != null && isRemovedGoto(gotoStmt))
                {
                    continue;
                }

                if (labelName.StartsWith(
                    GSharpInternalNames.Lowering.SyncGridNextLabelPrefix, StringComparison.Ordinal))
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
                    var slots = BuildSlots(
                        targetSection.Statements, holdsSplitPoint, splitStackedLabels, groupDetachedLabels);
                    var l = FindSlotIndexForLabel(slots, targetLabel);
                    if (l < 0)
                    {
                        continue;
                    }

                    var targetRegions = sectionPlans[t].Regions;
                    foreach (var range in targetRegions)
                    {
                        var opensBeforeLabels = OpensBeforeFirstLabels(targetRegions, range);
                        var entersMiddle = opensBeforeLabels
                            ? l >= range.First && l <= range.Last
                            : l > range.First && l <= range.Last;
                        if (entersMiddle)
                        {
                            rejected.Add(
                                new RejectedJump(
                                    gotoStmt,
                                    labelName,
                                    GetFirstLabelName(slots[range.LabelIndex])));
                            break;
                        }
                    }

                    break;
                }
            }
        }

        return rejected.ToImmutable();
    }

    internal static bool OpensBeforeFirstLabels(ImmutableArray<JumpRegion> regions, JumpRegion region)
    {
        if (region.Kind == JumpRegionKind.GotoLoop)
        {
            return true;
        }

        return regions
            .AsStackEnumerable()
            .Any(region, static (candidate, target) =>
                candidate.Kind == JumpRegionKind.GotoLoop &&
                candidate.First == target.First &&
                candidate.Last <= target.Last);
    }

    // A label l whose earliest forward goto k lies before an adopted ForwardRange [First, Last] with First < l <= Last,
    // or starts inside an adopted GotoLoop [First, Last] and exits after it (First < k <= Last < l)
    private static bool AdoptRangesIntoMiddle(
        int[] minFirst,
        StatementSlot[] slots,
        string?[] labelNames,
        List<CandidateRange> adopted)
    {
        var changed = false;
        for (var l = 0; l < minFirst.Length; l++)
        {
            var k = minFirst[l];
            if (k < 0 || HasForwardRangeAtLabel(adopted, l))
            {
                continue;
            }

            var entersForwardRangeMiddle = adopted
                .AsStackEnumerable()
                .Any((k, l), static (range, jump) =>
                    range.Kind == JumpRegionKind.ForwardRange &&
                    jump.k < range.First &&
                    range.First < jump.l &&
                    jump.l <= range.Last);
            var exitsGotoLoop = adopted
                .AsStackEnumerable()
                .Any((k, l), static (range, jump) =>
                    range.Kind == JumpRegionKind.GotoLoop &&
                    range.First < jump.k &&
                    jump.k <= range.Last &&
                    range.Last < jump.l);
            if (!entersForwardRangeMiddle && !exitsGotoLoop)
            {
                continue;
            }

            adopted.Add(
                new CandidateRange(
                    JumpRegionKind.ForwardRange,
                    l,
                    k,
                    l - 1,
                    labelNames[l] ?? GetFirstLabelName(slots[l])));
            changed = true;
        }

        return changed;
    }

    private static bool HasForwardRangeAtLabel(List<CandidateRange> adopted, int labelIndex) =>
        adopted
            .AsStackEnumerable()
            .Any(labelIndex, static (range, label) =>
                range.Kind == JumpRegionKind.ForwardRange && range.LabelIndex == label);

    private static bool HasSplitPointInRange(StatementSlot[] slots, int first, int last)
    {
        for (var s = first; s <= last; s++)
        {
            if (slots[s].HasSplit)
            {
                return true;
            }
        }

        return false;
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

    internal static bool IsDetachedLabelStatement(StatementSyntax statement) =>
        statement is LabeledStatementSyntax { Statement: EmptyStatementSyntax empty } labeled &&
        !LoweringIdentity.IsLoweringLabel(labeled) &&
        !empty.HasAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind);

    private static StatementSlot[] BuildSlots(
        SyntaxList<StatementSyntax> statements,
        Func<StatementSyntax, bool>? holdsSplitPoint,
        bool splitStackedLabels,
        bool groupDetachedLabels)
    {
        if (!splitStackedLabels || holdsSplitPoint == null)
        {
            var direct = new StatementSlot[statements.Count];
            for (var i = 0; i < statements.Count; i++)
            {
                var stmt = statements[i];
                var headIndex = groupDetachedLabels &&
                                i > 0 &&
                                IsDetachedLabelStatement(stmt) &&
                                IsDetachedLabelStatement(statements[i - 1])
                    ? direct[i - 1].HeadIndex
                    : i;
                direct[i] = new StatementSlot(
                    stmt, null, null, holdsSplitPoint != null && holdsSplitPoint(stmt), headIndex);
            }

            return direct;
        }

        var builder = ImmutableArray.CreateBuilder<StatementSlot>(statements.Count);
        for (var i = 0; i < statements.Count; i++)
        {
            var cur = statements[i];
            if (cur is LabeledStatementSyntax head && holdsSplitPoint(cur))
            {
                while (cur is LabeledStatementSyntax labeled)
                {
                    cur = labeled.Statement;
                }

                var headSlotIndex = builder.Count;
                builder.Add(new StatementSlot(null, head, cur, HasSplit: false, headSlotIndex));
                builder.Add(new StatementSlot(cur, null, null, holdsSplitPoint(cur), builder.Count));
            }
            else
            {
                builder.Add(new StatementSlot(cur, null, null, holdsSplitPoint(cur), builder.Count));
            }
        }

        return builder.ToArray();
    }

    private static void CollectGotos(
        StatementSyntax statement,
        List<GotoStatementSyntax> gotos,
        Func<GotoStatementSyntax, bool>? isRemovedGoto)
    {
        if (statement is LocalFunctionStatementSyntax)
        {
            return;
        }

        if (statement is GotoStatementSyntax directGoto && directGoto.IsKind(SyntaxKind.GotoStatement))
        {
            if (isRemovedGoto == null || !isRemovedGoto(directGoto))
            {
                gotos.Add(directGoto);
            }

            return;
        }

        foreach (var node in statement.DescendantNodes(static n =>
                     n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
        {
            if (node is GotoStatementSyntax gotoStmt &&
                gotoStmt.IsKind(SyntaxKind.GotoStatement) &&
                (isRemovedGoto == null || !isRemovedGoto(gotoStmt)))
            {
                gotos.Add(gotoStmt);
            }
        }
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

    private static int FindSlotIndexForLabel(StatementSlot[] slots, LabeledStatementSyntax targetLabel)
    {
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].SplitHead is { } splitHead)
            {
                for (var cur = (StatementSyntax)splitHead;
                     cur is LabeledStatementSyntax labeled && cur != slots[i].StopAt;
                     cur = labeled.Statement)
                {
                    if (labeled == targetLabel)
                    {
                        return slots[i].HeadIndex;
                    }
                }

                continue;
            }

            for (var cur = slots[i].Body; cur is LabeledStatementSyntax labeled; cur = labeled.Statement)
            {
                if (labeled == targetLabel)
                {
                    return slots[i].HeadIndex;
                }
            }
        }

        return -1;
    }

    private static string GetFirstLabelName(StatementSlot slot) =>
        slot.SplitHead?.Identifier.ValueText ??
        (slot.Body as LabeledStatementSyntax)?.Identifier.ValueText ??
        string.Empty;

    private readonly record struct StatementSlot(
        StatementSyntax? Body,
        LabeledStatementSyntax? SplitHead,
        StatementSyntax? StopAt,
        bool HasSplit,
        int HeadIndex);

    private sealed class CandidateRange(
        JumpRegionKind kind,
        int labelIndex,
        int first,
        int last,
        string labelName)
    {
        public JumpRegionKind Kind { get; } = kind;
        public int LabelIndex { get; } = labelIndex;
        public int First { get; set; } = first;
        public int Last { get; } = last;
        public string LabelName { get; } = labelName;
    }
}
