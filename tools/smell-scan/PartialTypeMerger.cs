using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Adds up the measurements of one unit that is spread over several files, such as the parts of a partial type. Parts
/// with the same rule, metric, project and group name are one unit. The unit is reported at its largest part, with the
/// value of all parts, and the other parts are its related locations.
/// </summary>
internal static class PartialTypeMerger
{
    /// <summary>Merges the candidates that carry a group name; the others are not looked at.</summary>
    public static List<MergedUnit> Merge(IReadOnlyList<Candidate> candidates, ProjectLocator locator, RuleScope scope)
    {
        var groups = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var candidate in candidates)
        {
            var measurement = candidate.Measurement;
            if (measurement.Group is null)
            {
                continue;
            }

            var key = candidate.Rule.Id + "\0" + measurement.Metric + "\0" + locator.Find(measurement.Path) + "\0" + measurement.Group;
            if (!groups.TryGetValue(key, out var parts))
            {
                parts = [];
                groups.Add(key, parts);
                order.Add(key);
            }

            parts.Add(candidate);
        }

        var units = new List<MergedUnit>(order.Count);
        foreach (var key in order)
        {
            units.Add(MergeGroup(groups[key], scope));
        }

        return units;
    }

    private static MergedUnit MergeGroup(List<Candidate> parts, RuleScope scope)
    {
        var largest = parts[0];
        foreach (var part in parts)
        {
            if (IsLarger(part.Measurement, largest.Measurement))
            {
                largest = part;
            }
        }

        var total = 0;
        var physical = 0;
        var related = new List<RelatedLocation>();
        foreach (var part in parts)
        {
            total += part.Measurement.Value;
            physical += part.Measurement.PhysicalLines;
            if (!ReferenceEquals(part, largest))
            {
                related.Add(new RelatedLocation(
                    part.Measurement.Path,
                    part.Measurement.Range,
                    "Another part of " + largest.Measurement.Symbol));
            }
        }

        related.Sort(CompareLocations);
        var merged = largest.Measurement with { Value = total, PhysicalLines = physical };
        return new MergedUnit(
            largest.Rule,
            merged,
            scope.ParametersFor(largest.Rule, merged.Path),
            [.. related]);
    }

    // The larger part wins; equal parts are told apart by their place so the choice does not depend on the order of files.
    private static bool IsLarger(Measurement candidate, Measurement current)
    {
        if (candidate.Value != current.Value)
        {
            return candidate.Value > current.Value;
        }

        var byPath = string.CompareOrdinal(candidate.Path, current.Path);
        return byPath != 0 ? byPath < 0 : candidate.Range.StartLine < current.Range.StartLine;
    }

    private static int CompareLocations(RelatedLocation x, RelatedLocation y)
    {
        var byPath = string.CompareOrdinal(x.Path, y.Path);
        if (byPath != 0)
        {
            return byPath;
        }

        var byLine = x.Range.StartLine.CompareTo(y.Range.StartLine);
        return byLine != 0 ? byLine : x.Range.StartColumn.CompareTo(y.Range.StartColumn);
    }
}
