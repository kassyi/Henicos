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

        var minFirst = new int[statements.Count];
        for (var i = 0; i < minFirst.Length; i++)
        {
            minFirst[i] = -1;
        }

        var backwardGotos = new List<(int K, int L, GotoStatementSyntax Statement)>();

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

                var l = FindStatementIndexForLabel(statements, targetLabel);
                if (l < 0)
                {
                    continue;
                }

                if (k < l)
                {
                    if (minFirst[l] == -1 || k < minFirst[l])
                    {
                        minFirst[l] = k;
                    }
                }
                else if (k > l)
                {
                    backwardGotos.Add((k, l, gotoStmt));
                }
            }
        }

        var adopted = new List<CandidateRange>();
        for (var l = 0; l < statements.Count; l++)
        {
            var first = minFirst[l];
            if (first < 0)
            {
                continue;
            }

            var hasSplit = false;
            for (var s = first; s < l; s++)
            {
                if (holdsSplitPoint(statements[s]))
                {
                    hasSplit = true;
                    break;
                }
            }

            if (hasSplit)
            {
                adopted.Add(new CandidateRange(l, first, l - 1));
            }
        }

        if (adopted.Count == 0 && backwardGotos.Count == 0)
        {
            return JumpRegionPlan.Empty;
        }

        // Expand crossing ranges, and adopt a range without a split point whose goto comes from before an adopted
        // range and whose label lies in its middle (the lanes that take it must enter the outer range first), until
        // nothing changes
        var changed = true;
        while (changed)
        {
            changed = AdoptRangesIntoMiddle(minFirst, adopted);
            for (var i = 0; i < adopted.Count; i++)
            {
                for (var j = 0; j < adopted.Count; j++)
                {
                    if (i == j)
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
        }

        var rejected = ImmutableArray.CreateBuilder<GotoStatementSyntax>();
        foreach (var (k, l, gotoStmt) in backwardGotos)
        {
            foreach (var range in adopted)
            {
                if (l > range.First && l <= range.Last && k > range.Last)
                {
                    if (!rejected.Contains(gotoStmt))
                    {
                        rejected.Add(gotoStmt);
                    }

                    break;
                }
            }
        }

        if (adopted.Count == 0 && rejected.Count == 0)
        {
            return JumpRegionPlan.Empty;
        }

        adopted.Sort(static (r1, r2) =>
        {
            var cmp = r1.First.CompareTo(r2.First);
            if (cmp != 0)
            {
                return cmp;
            }

            return r2.Last.CompareTo(r1.Last);
        });

        var regions = ImmutableArray.CreateBuilder<JumpRegion>(adopted.Count);
        foreach (var r in adopted)
        {
            regions.Add(new JumpRegion(JumpRegionKind.ForwardRange, r.LabelIndex, r.First, r.Last));
        }

        return new JumpRegionPlan(regions.ToImmutable(), rejected.ToImmutable());
    }

    // A label l whose earliest forward goto k lies before an adopted range [First, Last] with First < l <= Last
    private static bool AdoptRangesIntoMiddle(int[] minFirst, List<CandidateRange> adopted)
    {
        var changed = false;
        for (var l = 0; l < minFirst.Length; l++)
        {
            var k = minFirst[l];
            if (k < 0 ||
                adopted
                    .AsStackEnumerable()
                    .Any(l, static (range, label) => range.LabelIndex == label) ||
                !adopted
                    .AsStackEnumerable()
                    .Any((k, l), static (range, jump) => jump.k < range.First && range.First < jump.l &&
                                                        jump.l <= range.Last))
            {
                continue;
            }

            adopted.Add(new CandidateRange(l, k, l - 1));
            changed = true;
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

    private sealed class CandidateRange(int labelIndex, int first, int last)
    {
        public int LabelIndex { get; } = labelIndex;
        public int First { get; set; } = first;
        public int Last { get; } = last;
    }
}
