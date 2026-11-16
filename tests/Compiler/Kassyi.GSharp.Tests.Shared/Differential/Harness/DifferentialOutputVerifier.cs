#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Compares output buffers against the oracle, evaluates property checks, and records stale known divergences.
/// </summary>
internal static class DifferentialOutputVerifier
{
    // absentPathsMatch: a path without buffers among actuals is left out of the comparison instead of differing in
    // every cell (the comparison after an exception, whose exception cells report the paths that did not throw)
    public static void Compare(
        in DifferentialRunner.CaseRun run,
        in DifferentialRunner.ShapeRun at,
        DiffBuffers expected,
        List<(DiffPathRunner Path, DiffBuffers Buffers, DiffBuffers? PtrBuffers)> actuals,
        bool absentPathsMatch = false
    )
    {
        for (int b = 0; b < expected.All.Count; b++)
        {
            var rawOracle = at.Initial.All[b].IsReadOnly ? at.Initial.All[b] : expected.All[b];
            bool isMultiset =
                rawOracle.Policy.Kind == OutputPolicyKind.Multiset ||
                (rawOracle.ColumnPolicies is not null &&
                 rawOracle.ColumnPolicies.Any(static p => p.Kind == OutputPolicyKind.Multiset));
            var oracle = isMultiset ? rawOracle.CreateSortedCopy() : rawOracle;
            var actualBuffers = PrepareActualBuffers(actuals, b, isMultiset);
            CompareBufferCells(in run, in at, oracle, actualBuffers, absentPathsMatch);
        }
    }

    private static List<(DiffPathRunner Path, DiffBuffer Primary, DiffBuffer? Ptr)> PrepareActualBuffers(
        List<(DiffPathRunner Path, DiffBuffers Buffers, DiffBuffers? PtrBuffers)> actuals,
        int bufferIndex,
        bool isMultiset
    )
    {
        var actualBuffers = new List<(DiffPathRunner Path, DiffBuffer Primary, DiffBuffer? Ptr)>(actuals.Count);
        foreach (var (path, buffers, ptrBuffers) in actuals)
        {
            var primary = isMultiset
                ? buffers.All[bufferIndex].CreateSortedCopy()
                : buffers.All[bufferIndex];
            var ptr = ptrBuffers is not null
                ? (isMultiset ? ptrBuffers.All[bufferIndex].CreateSortedCopy() : ptrBuffers.All[bufferIndex])
                : null;
            actualBuffers.Add((path, primary, ptr));
        }

        return actualBuffers;
    }

    private static void CompareBufferCells(
        in DifferentialRunner.CaseRun run,
        in DifferentialRunner.ShapeRun at,
        DiffBuffer oracle,
        List<(DiffPathRunner Path, DiffBuffer Primary, DiffBuffer? Ptr)> actualBuffers,
        bool absentPathsMatch
    )
    {
        var skippedPaths = run.Case.SkippedPaths;
        for (int i = 0; i < oracle.Length; i++)
        {
            if (run.Paths.All(p =>
                    (skippedPaths is not null && skippedPaths.ContainsKey(p.Path)) ||
                    PathMatches(oracle, actualBuffers, p.Path, i, absentPathsMatch)))
            {
                continue;
            }

            var cells = BuildMismatchCells(in run, in at, oracle, actualBuffers, i);
            run.Report.Add(new CellMismatch(
                at.Seed,
                oracle.LocationOf(i),
                FormatInputs(at.Initial, at.Shape.Rows, oracle.RowOf(i)),
                oracle.FormatCell(i),
                cells,
                at.Shape
            ));
        }
    }

    private static List<PathCell> BuildMismatchCells(
        in DifferentialRunner.CaseRun run,
        in DifferentialRunner.ShapeRun at,
        DiffBuffer oracle,
        List<(DiffPathRunner Path, DiffBuffer Primary, DiffBuffer? Ptr)> actualBuffers,
        int cellIndex
    )
    {
        var cells = new List<PathCell>(run.Paths.Count);
        string column = oracle.ColumnNameOf(cellIndex);
        foreach (var path in run.Paths)
        {
            if (run.Case.SkippedPaths is not null &&
                run.Case.SkippedPaths.TryGetValue(path.Path, out string? skipReason))
            {
                cells.Add(new PathCell(path.Path, $"skipped ({skipReason})", Differs: false, Known: null));
                continue;
            }

            var match = actualBuffers.FirstOrDefault(a => a.Path.Path == path.Path);
            if (match.Primary is null)
            {
                continue;
            }

            bool primaryDiffers = !oracle.CellsMatch(match.Primary, cellIndex);
            bool ptrDiffers = match.Ptr is not null && !oracle.CellsMatch(match.Ptr, cellIndex);
            bool differs = primaryDiffers || ptrDiffers;
            var displayedBuffer =
                !primaryDiffers && ptrDiffers && match.Ptr is not null ? match.Ptr : match.Primary;
            var entry = differs ? run.FindEntry(column, path.Path, at.Shape) : null;
            cells.Add(new PathCell(path.Path, displayedBuffer.FormatCell(cellIndex), differs, entry));
        }

        return cells;
    }

    public static void RunPropertyChecks(
        in DifferentialRunner.CaseRun run,
        in DifferentialRunner.ShapeRun at,
        List<(DiffPathRunner Path, DiffBuffers Buffers, DiffBuffers? PtrBuffers)> actuals
    )
    {
        var testCase = run.Case;
        if (testCase.Verify is null && testCase.VerifyAgainstInitial is null)
        {
            return;
        }

        foreach (var (path, buffers, ptrBuffers) in actuals)
        {
            RunPathPropertyChecks(in run, in at, path.Path, buffers, ptrBuffers);
        }
    }

    private static void RunPathPropertyChecks(
        in DifferentialRunner.CaseRun run,
        in DifferentialRunner.ShapeRun at,
        DiffPath path,
        DiffBuffers buffers,
        DiffBuffers? ptrBuffers
    )
    {
        var testCase = run.Case;
        if (testCase.Verify is { } verify)
        {
            var res = verify(buffers, at.Shape);
            if (!res.Success)
            {
                run.Report.Add(new PropertyFailure(at.Seed, path, res.FailureMessage ?? "Verify failed.", at.Shape));
            }

            if (ptrBuffers is not null)
            {
                var ptrRes = verify(ptrBuffers, at.Shape);
                if (!ptrRes.Success)
                {
                    run.Report.Add(new PropertyFailure(
                        at.Seed,
                        path,
                        ptrRes.FailureMessage ?? "Verify (LaunchPtr) failed.",
                        at.Shape
                    ));
                }
            }
        }

        if (testCase.VerifyAgainstInitial is { } verifyAgainstInitial)
        {
            var res = verifyAgainstInitial(at.Initial, buffers, at.Shape);
            if (!res.Success)
            {
                run.Report.Add(new PropertyFailure(
                    at.Seed,
                    path,
                    res.FailureMessage ?? "VerifyAgainstInitial failed.",
                    at.Shape
                ));
            }

            if (ptrBuffers is not null)
            {
                var ptrRes = verifyAgainstInitial(at.Initial, ptrBuffers, at.Shape);
                if (!ptrRes.Success)
                {
                    run.Report.Add(new PropertyFailure(
                        at.Seed,
                        path,
                        ptrRes.FailureMessage ?? "VerifyAgainstInitial (LaunchPtr) failed.",
                        at.Shape
                    ));
                }
            }
        }
    }

    // The inputs with one value per row, as name=value
    public static string FormatInputs(DiffBuffers initial, int rows, int row) =>
        string.Join(
            "  ",
            initial.All
                .Where(b => !b.IsOutput && b.Length == rows && row < rows)
                .Select(b => $"{b.Name}={b.FormatCell(row)}")
        );

    public static void AddStaleEntries(in DifferentialRunner.CaseRun run)
    {
        var report = run.Report;
        foreach (var entry in run.Entries)
        {
            if (entry.ShapePredicate is not null && !report.ExecutedShapes.Any(s => entry.ShapePredicate(s)))
            {
                continue;
            }

            foreach (var path in run.Paths)
            {
                CheckStaleEntryForPath(in run, entry, path.Path);
            }
        }
    }

    private static void CheckStaleEntryForPath(
        in DifferentialRunner.CaseRun run,
        KnownDivergence entry,
        DiffPath path
    )
    {
        var report = run.Report;
        if (run.Case.SkippedPaths is not null && run.Case.SkippedPaths.ContainsKey(path))
        {
            return;
        }

        if ((entry.Paths & path) == 0)
        {
            return;
        }

        // The CUDA cells of the fault seeds did not run, so an entry for them cannot be seen
        if (entry.Column == KnownDivergence.ExceptionColumn &&
            path == DiffPath.Cuda &&
            report.SkippedCudaTraps.Count > 0)
        {
            return;
        }

        if (entry.Column != KnownDivergence.ExceptionColumn &&
            report.Failures.Any(f =>
                f.Path == path &&
                (f.Shape is null || entry.ShapePredicate is null || entry.ShapePredicate(f.Shape))))
        {
            return;
        }

        bool seen =
            report.Mismatches.Any(m =>
                m.ColumnName == entry.Column &&
                (m.Shape is null || entry.ShapePredicate is null || entry.ShapePredicate(m.Shape)) &&
                m.Paths.Any(p => p.Path == path && p.Known == entry)) ||
            (entry.Column == KnownDivergence.ExceptionColumn &&
             report.Failures.Any(f =>
                 f.Path == path &&
                 f.Known == entry &&
                 (f.Shape is null || entry.ShapePredicate is null || entry.ShapePredicate(f.Shape))));
        if (!seen)
        {
            report.Add(new StaleDivergence(entry, path));
        }
    }

    // Whether the buffers of path match the oracle at cell i; a path without buffers matches when absentPathsMatch
    private static bool PathMatches(
        DiffBuffer oracle,
        List<(DiffPathRunner Path, DiffBuffer Primary, DiffBuffer? Ptr)> actualBuffers,
        DiffPath path,
        int i,
        bool absentPathsMatch
    )
    {
        var match = actualBuffers.FirstOrDefault(a => a.Path.Path == path);
        if (match.Primary is null)
        {
            return absentPathsMatch;
        }

        return oracle.CellsMatch(match.Primary, i) && (match.Ptr is null || oracle.CellsMatch(match.Ptr, i));
    }
}
