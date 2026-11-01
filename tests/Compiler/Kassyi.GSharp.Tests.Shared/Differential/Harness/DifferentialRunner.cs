#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Runs a <see cref="DifferentialCase"/> on every path of this process and compares each with the oracle (design §4.3).
/// For each seed: build the inputs, run the oracle in three thread orders and require the same output, run every path on
/// its own copy of the inputs, and compare every buffer cell by cell. All seeds run before the test fails, so one failure
/// shows every mismatching cell.
/// </summary>
public static class DifferentialRunner
{
    /// <summary>
    /// Runs the case on <see cref="DiffPaths.Available"/> with <paramref name="seeds"/> (by default
    /// <see cref="DifferentialSeeds.For"/> of <c>TEST_SEED</c>) and <see cref="KnownDivergences.All"/>, and fails the
    /// test with the report when a cell differs, a path throws, or a known divergence no longer reproduces. Skips the
    /// test on Tests.Cuda without a GPU.
    /// </summary>
    /// <param name="testCase">The case.</param>
    /// <param name="seeds">The seeds to run instead of the default ones, e.g. the seeds a failure printed.</param>
    public static void Run(DifferentialCase testCase, IReadOnlyList<int>? seeds = null)
    {
#if GSHARP_CUDA
        CudaCapability.EnsureGpuAvailable();
#endif
        string? testSeed = seeds is null ? DifferentialSeeds.TestSeed : null;
        // Explicit seeds reproduce a failure: the entries' triggering inputs may be in the seeds left out, so none is stale
        var report = Evaluate(testCase, seeds ?? DifferentialSeeds.For(testSeed), DiffPaths.Available, KnownDivergences.All, testSeed, checkStale: seeds is null);
        if (report.HasFailures)
        {
            Assert.Fail(report.ToString());
        }
    }

    /// <summary>Runs the case and returns the report without failing the test.</summary>
    /// <param name="testCase">The case.</param>
    /// <param name="seeds">The seeds.</param>
    /// <param name="paths">The paths to compare with the oracle.</param>
    /// <param name="known">The known divergences; entries of other cases are ignored.</param>
    /// <param name="testSeed">The <c>TEST_SEED</c> the seeds came from, for the report.</param>
    /// <param name="checkStale">Whether to report known divergences that no longer reproduce; false when <paramref name="seeds"/> are only some of the seeds that run by default.</param>
    /// <exception cref="DifferentialHarnessException">The case is malformed, or the oracle threw or depends on the thread order.</exception>
    public static DifferentialReport Evaluate(
        DifferentialCase testCase,
        IReadOnlyList<int> seeds,
        IReadOnlyList<DiffPathRunner> paths,
        IReadOnlyList<KnownDivergence> known,
        string? testSeed = null,
        bool checkStale = true
    )
    {
        if ((testCase.Oracle is null) == (testCase.Reference is null))
        {
            throw new DifferentialHarnessException($"{testCase.Name}: set exactly one of Oracle and Reference");
        }

        if (seeds.Count == 0 || paths.Count == 0)
        {
            throw new DifferentialHarnessException($"{testCase.Name}: no seed or no path to run");
        }

        // Each path is the one it names (a native path without its library would run managed), before anything runs
        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
        }

        var entries = known.Where(k => k.Case == testCase.Name && k.AppliesToThisHost).ToList();
        var report = new DifferentialReport(testCase, paths, seeds, testSeed);
        foreach (int seed in seeds)
        {
            var initial = testCase.Inputs(new DiffRandom(seed), testCase.Shape.Rows);
            var expected = RunOracle(testCase, initial, seed);
            var actuals = new List<(DiffPathRunner Path, DiffBuffers Buffers)>();
            foreach (var path in paths)
            {
                var buffers = initial.Clone();
                try
                {
                    path.Run(() => testCase.Launch(buffers, testCase.Shape));
                    actuals.Add((path, buffers));
                }
                catch (Exception ex)
                {
                    // A throwing path is a divergence (#728 and #711 threw GpuOnlyException), not a harness fault
                    report.Add(new PathFailure(seed, path.Path, ex));
                }
            }

            Compare(testCase, seed, initial, expected, actuals, entries, report);
        }

        if (checkStale)
        {
            AddStaleEntries(paths, entries, report);
        }

        return report;
    }

    private static DiffBuffers RunOracle(DifferentialCase testCase, DiffBuffers initial, int seed)
    {
        var expected = initial.Clone();
        if (testCase.Reference is { } reference)
        {
            Guard(testCase, seed, "reference model", () => reference(expected));
            return expected;
        }

        // Checked by the caller: exactly one of Oracle and Reference is set
        var oracle = testCase.Oracle ?? throw new DifferentialHarnessException($"{testCase.Name}: no oracle");
        Guard(testCase, seed, "oracle (forward)", () => SequentialOracle.Run(testCase.Shape, ThreadOrder.Forward, () => oracle(expected)));

        // The same output in the reverse and a permuted order: otherwise the kernel races (design §2.2)
        foreach (var order in new[] { ThreadOrder.Reverse, ThreadOrder.Permutation(seed) })
        {
            var other = initial.Clone();
            Guard(testCase, seed, $"oracle ({order})", () => SequentialOracle.Run(testCase.Shape, order, () => oracle(other)));
            CheckOrderIndependence(testCase, seed, expected, other, order);
        }

        return expected;
    }

    private static void Guard(DifferentialCase testCase, int seed, string what, Action run)
    {
        try
        {
            run();
        }
        catch (Exception ex)
        {
            throw new DifferentialHarnessException(
                $"{testCase.Name}: the {what} threw for seed {seed}; keep inputs that C# rejects out of the generator (design §2.5)",
                ex
            );
        }
    }

    private static void CheckOrderIndependence(DifferentialCase testCase, int seed, DiffBuffers forward, DiffBuffers other, ThreadOrder order)
    {
        for (int b = 0; b < forward.All.Count; b++)
        {
            var first = forward.All[b];
            if (first.Policy.Kind == OutputPolicyKind.Ignore)
            {
                continue;
            }

            var second = other.All[b];
            for (int i = 0; i < first.Length; i++)
            {
                if (!first.SameBits(second, i))
                {
                    throw new DifferentialHarnessException(
                        $"{testCase.Name}: kernel output depends on thread order, so the kernel races (undefined on the GPU) " +
                        $"or reads a non-commutative atomic: seed {seed}, {first.Name}[{i}] column \"{first.ColumnNameOf(i)}\" " +
                        $"is {first.FormatCell(i)} in forward order and {second.FormatCell(i)} in {order} order"
                    );
                }
            }
        }
    }

    private static void Compare(
        DifferentialCase testCase,
        int seed,
        DiffBuffers initial,
        DiffBuffers expected,
        List<(DiffPathRunner Path, DiffBuffers Buffers)> actuals,
        List<KnownDivergence> entries,
        DifferentialReport report
    )
    {
        for (int b = 0; b < expected.All.Count; b++)
        {
            var oracle = expected.All[b];
            for (int i = 0; i < oracle.Length; i++)
            {
                if (actuals.All(a => oracle.CellsMatch(a.Buffers.All[b], i)))
                {
                    continue;
                }

                var cells = new List<PathCell>(actuals.Count);
                string column = oracle.ColumnNameOf(i);
                foreach (var (path, buffers) in actuals)
                {
                    var actual = buffers.All[b];
                    bool differs = !oracle.CellsMatch(actual, i);
                    var entry = differs ? entries.FirstOrDefault(k => k.Covers(testCase.Name, column, path.Path)) : null;
                    cells.Add(new PathCell(path.Path, actual.FormatCell(i), differs, entry));
                }

                report.Add(new CellMismatch(
                    seed,
                    oracle.Name,
                    i,
                    oracle.RowOf(i),
                    oracle.ColumnOf(i),
                    column,
                    FormatInputs(initial, testCase.Shape.Rows, oracle.RowOf(i)),
                    oracle.FormatCell(i),
                    cells
                ));
            }
        }
    }

    // The inputs with one value per row, as name=value
    private static string FormatInputs(DiffBuffers initial, int rows, int row) =>
        string.Join("  ", initial.All
            .Where(b => !b.IsOutput && b.Length == rows && row < rows)
            .Select(b => $"{b.Name}={b.FormatCell(row)}")
        );

    // An entry must still differ on each listed path that ran, for at least one seed. A path that threw on a seed did not
    // complete, so it says nothing about the entry
    private static void AddStaleEntries(IReadOnlyList<DiffPathRunner> paths, List<KnownDivergence> entries, DifferentialReport report)
    {
        foreach (var entry in entries)
        {
            foreach (var path in paths)
            {
                if ((entry.Paths & path.Path) == 0 || report.Failures.Any(f => f.Path == path.Path))
                {
                    continue;
                }

                bool seen = report.Mismatches.Any(m => m.ColumnName == entry.Column && m.Paths.Any(p => p.Path == path.Path && p.Known == entry));
                if (!seen)
                {
                    report.Add(new StaleDivergence(entry, path.Path));
                }
            }
        }
    }
}
