#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Runs a <see cref="DifferentialCase"/> on every path of this process and compares each with the oracle (design §4.3).
/// For each seed and launch shape: build the inputs, run the oracle in three thread orders and require the same output,
/// run every path on its own copy of the inputs, and compare every buffer cell by cell.
/// </summary>
public static class DifferentialRunner
{
    private readonly record struct CaseRun(
        DifferentialCase Case,
        IReadOnlyList<DiffPathRunner> Paths,
        List<KnownDivergence> Entries,
        DifferentialReport Report
    )
    {
        public KnownDivergence? FindEntry(string column, DiffPath path, LaunchShape shape)
        {
            string caseName = Case.Name;
            return Entries.FirstOrDefault(k => k.Covers(caseName, column, path, shape));
        }
    }

    private readonly record struct ShapeRun(int Seed, int ShapeIndex, LaunchShape Shape, DiffBuffers Initial);

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
        var effectiveSeeds = seeds ?? testCase.Seeds;
        string? testSeed = effectiveSeeds is null ? DifferentialSeeds.TestSeed : null;
        var report = Evaluate(
            testCase,
            effectiveSeeds ?? DifferentialSeeds.For(testSeed),
            DiffPaths.Available,
            KnownDivergences.All,
            testSeed,
            checkStale: seeds is null
        );
        if (report.HasFailures)
        {
            Assert.Fail(report.ToString());
        }
    }

    /// <summary>
    /// Evaluates a batch of <see cref="DifferentialCase"/> instances, running path preflight checks only once at the start of the batch.
    /// </summary>
    public static IReadOnlyList<DifferentialReport> EvaluateBatch(
        ReadOnlySpan<DifferentialCase> cases,
        IReadOnlyList<int>? seeds = null,
        IReadOnlyList<DiffPathRunner>? paths = null,
        IReadOnlyList<KnownDivergence>? known = null,
        string? testSeed = null,
        bool checkStale = true
    )
    {
        var activePaths = paths ?? DiffPaths.Available;
        var activeKnown = known ?? KnownDivergences.All;
        foreach (var path in activePaths)
        {
            path.Preflight?.Invoke();
        }

        var reports = new List<DifferentialReport>(cases.Length);
        foreach (var testCase in cases)
        {
            var effectiveSeeds = seeds ?? testCase.Seeds ?? DifferentialSeeds.For(testSeed);
            reports.Add(EvaluateCore(testCase, effectiveSeeds, activePaths, activeKnown, testSeed, checkStale, runPreflight: false));
        }

        return reports;
    }

    /// <summary>
    /// Evaluates a batch of <see cref="DifferentialCase"/> instances, running path preflight checks only once at the start of the batch.
    /// </summary>
    public static IReadOnlyList<DifferentialReport> EvaluateBatch(
        IReadOnlyList<DifferentialCase> cases,
        IReadOnlyList<int>? seeds = null,
        IReadOnlyList<DiffPathRunner>? paths = null,
        IReadOnlyList<KnownDivergence>? known = null,
        string? testSeed = null,
        bool checkStale = true
    )
    {
        ArgumentNullException.ThrowIfNull(cases);
        var activePaths = paths ?? DiffPaths.Available;
        var activeKnown = known ?? KnownDivergences.All;
        foreach (var path in activePaths)
        {
            path.Preflight?.Invoke();
        }

        var reports = new List<DifferentialReport>(cases.Count);
        foreach (var testCase in cases)
        {
            var effectiveSeeds = seeds ?? testCase.Seeds ?? DifferentialSeeds.For(testSeed);
            reports.Add(EvaluateCore(testCase, effectiveSeeds, activePaths, activeKnown, testSeed, checkStale, runPreflight: false));
        }

        return reports;
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
    ) => EvaluateCore(testCase, seeds, paths, known, testSeed, checkStale, runPreflight: true);

    private static DifferentialReport EvaluateCore(
        DifferentialCase testCase,
        IReadOnlyList<int> seeds,
        IReadOnlyList<DiffPathRunner> paths,
        IReadOnlyList<KnownDivergence> known,
        string? testSeed,
        bool checkStale,
        bool runPreflight
    )
    {
        ValidateCase(testCase, seeds, paths);

        if (runPreflight)
        {
            foreach (var path in paths)
            {
                path.Preflight?.Invoke();
            }
        }

        var report = new DifferentialReport(testCase, paths, seeds, testSeed);

        if (CudaTrapSubprocessRunner.TryGetActiveTarget(out var childTarget))
        {
            RunChildTarget(testCase, paths, childTarget);
            return report;
        }

        var entries = known.Where(k => k.Case == testCase.Name && k.AppliesToThisHost).ToList();
        var run = new CaseRun(testCase, paths, entries, report);
        int subprocessCount = 0;

        foreach (int seed in seeds)
        {
            var shapesForSeed = testCase.GetShapesForSeed(seed);
            for (int shapeIndex = 0; shapeIndex < shapesForSeed.Count; shapeIndex++)
            {
                var shape = shapesForSeed[shapeIndex];
                report.AddExecutedShape(shape);
                var initial = testCase.Inputs(new DiffRandom(seed), shape);
                initial.Shape = shape;
                var at = new ShapeRun(seed, shapeIndex, shape, initial);

                if (RunOneShape(in run, in at, allowSubprocess: subprocessCount == 0))
                {
                    subprocessCount++;
                }
            }
        }

        if (checkStale)
        {
            AddStaleEntries(in run);
        }

        return report;
    }

    private static void ValidateCase(DifferentialCase testCase, IReadOnlyList<int> seeds, IReadOnlyList<DiffPathRunner> paths)
    {
        bool hasOracle = testCase.Oracle is not null;
        bool hasReference = testCase.Reference is not null;
        bool hasVerifier = testCase.Verify is not null || testCase.VerifyAgainstInitial is not null;
        bool hasExpectedException = testCase.ExpectedException is not null || testCase.ExceptionPolicy is not null;

        if (hasOracle && hasReference)
        {
            throw new DifferentialHarnessException($"{testCase.Name}: set at most one of Oracle and Reference");
        }

        if (!hasOracle && !hasReference && !hasVerifier && !hasExpectedException)
        {
            throw new DifferentialHarnessException($"{testCase.Name}: set Oracle, Reference, Verify/VerifyAgainstInitial, or ExpectedException");
        }

        if (seeds.Count == 0 || paths.Count == 0)
        {
            throw new DifferentialHarnessException($"{testCase.Name}: no seed or no path to run");
        }

        if (testCase.EffectiveShapes.Count == 0)
        {
            throw new DifferentialHarnessException($"{testCase.Name}: no launch shape configured");
        }
    }

    private static void RunChildTarget(
        DifferentialCase testCase,
        IReadOnlyList<DiffPathRunner> paths,
        (string CaseName, int ShapeIndex, int Seed) childTarget
    )
    {
        if (!string.Equals(childTarget.CaseName, testCase.Name, StringComparison.Ordinal))
        {
            return;
        }

        var childShapes = testCase.GetShapesForSeed(childTarget.Seed);
        if ((uint)childTarget.ShapeIndex >= (uint)childShapes.Count)
        {
            throw new DifferentialHarnessException(
                $"{testCase.Name}: invalid child shape index {childTarget.ShapeIndex} (count={childShapes.Count})"
            );
        }

        var childShape = childShapes[childTarget.ShapeIndex];
        var childInitial = testCase.Inputs(new DiffRandom(childTarget.Seed), childShape);
        childInitial.Shape = childShape;
        var childBuffers = childInitial.Clone();
        childBuffers.Shape = childShape;
        var cudaRunner = paths.FirstOrDefault(static p => p.Path == DiffPath.Cuda) ?? paths[0];
        Exception? childThrown = null;
        try
        {
            cudaRunner.Run(() => testCase.Launch(childBuffers, childShape));
        }
        catch (Exception ex)
        {
            childThrown = ex;
        }

        CudaTrapSubprocessRunner.WriteChildResult(childThrown);
    }

    private static bool RunOneShape(in CaseRun run, in ShapeRun at, bool allowSubprocess)
    {
        var testCase = run.Case;
        bool hasOracle = testCase.Oracle is not null;
        bool hasReference = testCase.Reference is not null;

        var activeExPolicy = testCase.ExpectedException?.Invoke(new DiffRandom(at.Seed), at.Shape) ?? testCase.ExceptionPolicy;
        DiffBuffers? expected = null;

        if (activeExPolicy is null)
        {
            if (!hasOracle && !hasReference && at.Initial.All.Any(static b => b.IsOutput && b.Policy.Kind != OutputPolicyKind.Ignore))
            {
                throw new DifferentialHarnessException(
                    $"{testCase.Name}: when neither Oracle nor Reference is set, all output buffers must use OutputPolicy.Ignore"
                );
            }

            if (hasOracle || hasReference)
            {
                expected = RunOracle(testCase, at.Initial, at.Shape, at.Seed, out activeExPolicy);
            }
        }

        if (activeExPolicy is not null)
        {
            return EvaluateExceptionExpectation(in run, in at, activeExPolicy, allowSubprocess);
        }

        var shape = at.Shape;
        var actuals = new List<(DiffPathRunner Path, DiffBuffers Buffers, DiffBuffers? PtrBuffers)>();
        foreach (var path in run.Paths)
        {
            var buffers = at.Initial.Clone();
            buffers.Shape = shape;
            try
            {
                path.Run(() => testCase.Launch(buffers, shape));
                DiffBuffers? ptrBuffers = null;
                if (path.Path == DiffPath.Cuda && testCase.LaunchPtr is { } launchPtr)
                {
                    ptrBuffers = at.Initial.Clone();
                    ptrBuffers.Shape = shape;
                    path.Run(() => launchPtr(ptrBuffers, shape));
                }

                actuals.Add((path, buffers, ptrBuffers));
            }
            catch (Exception ex)
            {
                run.Report.Add(new PathFailure(at.Seed, path.Path, ex, shape));
            }
        }

        if (expected is not null)
        {
            Compare(in run, in at, expected, actuals);
        }

        RunPropertyChecks(in run, in at, actuals);
        return false;
    }

    private static bool EvaluateExceptionExpectation(
        in CaseRun run,
        in ShapeRun at,
        ExceptionPolicy policy,
        bool allowSubprocess
    )
    {
        var testCase = run.Case;
        var cells = new List<PathCell>(run.Paths.Count);
        bool anyDiffers = false;
        bool launchedSubprocess = false;

        foreach (var path in run.Paths)
        {
            if ((policy.Paths & path.Path) == 0)
            {
                cells.Add(new PathCell(path.Path, "skipped (unchecked path)", Differs: false, Known: null));
                continue;
            }

            Exception? thrown = null;
            bool skippedBySampling = false;

            if (path.Path == DiffPath.Cuda && ReferenceEquals(path.Run, DiffPaths.DirectRunner))
            {
                bool shouldRunSubprocess =
                    testCase.CudaTrapSampling == CudaTrapSampling.AllFaults ||
                    CudaTrapSubprocessRunner.IsFullSamplingEnabled ||
                    allowSubprocess;

                if (shouldRunSubprocess)
                {
                    launchedSubprocess = true;
                    thrown = CudaTrapSubprocessRunner.RunInSubprocess(testCase.Name, at.ShapeIndex, at.Seed);
                }
                else
                {
                    skippedBySampling = true;
                }
            }
            else
            {
                var shape = at.Shape;
                var buffers = at.Initial.Clone();
                buffers.Shape = shape;
                try
                {
                    path.Run(() => testCase.Launch(buffers, shape));
                    if (path.Path == DiffPath.Cuda && testCase.LaunchPtr is { } launchPtr)
                    {
                        var ptrBuffers = at.Initial.Clone();
                        ptrBuffers.Shape = shape;
                        path.Run(() => launchPtr(ptrBuffers, shape));
                    }
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }
            }

            if (skippedBySampling)
            {
                cells.Add(new PathCell(path.Path, "sampled on first fault seed", Differs: false, Known: null));
                continue;
            }

            if (thrown is null)
            {
                anyDiffers = true;
                var entry = run.FindEntry(KnownDivergence.ExceptionColumn, path.Path, at.Shape);
                cells.Add(new PathCell(path.Path, "no exception thrown", Differs: true, entry));
            }
            else if (!policy.Matches(thrown, path.Path, out string? mismatchReason))
            {
                anyDiffers = true;
                var actual = ExceptionPolicy.Unwrap(thrown);
                var entry = run.FindEntry(KnownDivergence.ExceptionColumn, path.Path, at.Shape);
                cells.Add(new PathCell(path.Path, $"{actual.GetType().Name} ({mismatchReason})", Differs: true, entry));
            }
            else
            {
                var actual = ExceptionPolicy.Unwrap(thrown);
                cells.Add(new PathCell(path.Path, actual.GetType().Name, Differs: false, Known: null));
            }
        }

        if (anyDiffers)
        {
            run.Report.Add(new CellMismatch(
                at.Seed,
                new CellLocation(KnownDivergence.ExceptionColumn, 0, KnownDivergence.ExceptionColumn),
                FormatInputs(at.Initial, at.Shape.Rows, 0),
                policy.ToString(),
                cells,
                at.Shape
            ));
        }

        return launchedSubprocess;
    }

    private static DiffBuffers? RunOracle(
        DifferentialCase testCase,
        DiffBuffers initial,
        LaunchShape shape,
        int seed,
        out ExceptionPolicy? matchedExceptionPolicy
    )
    {
        matchedExceptionPolicy = null;
        var expected = initial.Clone();
        expected.Shape = shape;
        if (testCase.Reference is { } reference)
        {
            Guard(testCase, seed, "reference model", () => reference(expected, shape));
            return expected;
        }

        var oracle = testCase.Oracle ?? throw new DifferentialHarnessException($"{testCase.Name}: no oracle");
        var orders = testCase.OracleOrders ?? [ThreadOrder.Forward, ThreadOrder.Reverse, ThreadOrder.Permutation(seed)];
        var firstOrder = orders.Count > 0 ? orders[0] : ThreadOrder.Forward;

        if (testCase.OracleExceptions == OracleExceptionMode.MatchOracle)
        {
            Exception? firstEx = TryRunOrder(shape, firstOrder, () => oracle(expected, shape));
            for (int i = 1; i < orders.Count; i++)
            {
                var order = orders[i];
                var other = initial.Clone();
                other.Shape = shape;
                Exception? otherEx = TryRunOrder(shape, order, () => oracle(other, shape));
                if ((firstEx is null) != (otherEx is null))
                {
                    throw new DifferentialHarnessException(
                        $"{testCase.Name}: oracle exception depends on thread order for seed {seed}: " +
                        $"{firstOrder} {(firstEx is null ? "succeeded" : $"threw {firstEx.GetType().Name}")}, " +
                        $"but {order} {(otherEx is null ? "succeeded" : $"threw {otherEx.GetType().Name}")}"
                    );
                }

                if (firstEx is not null && otherEx is not null)
                {
                    if (firstEx.GetType() != otherEx.GetType())
                    {
                        throw new DifferentialHarnessException(
                            $"{testCase.Name}: oracle threw {firstEx.GetType().Name} in {firstOrder} order " +
                            $"but {otherEx.GetType().Name} in {order} order for seed {seed}"
                        );
                    }
                }
                else
                {
                    CheckOrderIndependence(testCase, seed, expected, other, order);
                }
            }

            if (firstEx is not null)
            {
                matchedExceptionPolicy = new ExceptionPolicy(firstEx.GetType(), testCase.ExceptionPaths);
                return null;
            }

            return expected;
        }

        Guard(testCase, seed, $"oracle ({firstOrder})", () => SequentialOracle.Run(shape, firstOrder, () => oracle(expected, shape)));

        for (int i = 1; i < orders.Count; i++)
        {
            var order = orders[i];
            var other = initial.Clone();
            other.Shape = shape;
            Guard(testCase, seed, $"oracle ({order})", () => SequentialOracle.Run(shape, order, () => oracle(other, shape)));
            CheckOrderIndependence(testCase, seed, expected, other, order);
        }

        return expected;
    }

    private static Exception? TryRunOrder(LaunchShape shape, ThreadOrder order, Action run)
    {
        try
        {
            SequentialOracle.Run(shape, order, run);
            return null;
        }
        catch (Exception ex)
        {
            return ExceptionPolicy.Unwrap(ex);
        }
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
            if (first.ColumnPolicies is null && first.Policy.Kind is OutputPolicyKind.Ignore or OutputPolicyKind.Multiset)
            {
                continue;
            }

            var second = other.All[b];
            for (int i = 0; i < first.Length; i++)
            {
                if (first.PolicyOf(i).Kind is OutputPolicyKind.Ignore or OutputPolicyKind.Multiset)
                {
                    continue;
                }

                // [WHY] a computed floating-point cell (Bitwise) promises only that it is a NaN, not which one: the
                // optimized JIT code may quiet a signaling NaN that the unoptimized code returns as it is. A moved value
                // (Exact) still has to keep every bit
                bool sameValue = first.SameBits(second, i) ||
                    (first.PolicyOf(i).Kind == OutputPolicyKind.Bitwise && first.CellsMatch(second, i));
                if (!sameValue)
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
        in CaseRun run,
        in ShapeRun at,
        DiffBuffers expected,
        List<(DiffPathRunner Path, DiffBuffers Buffers, DiffBuffers? PtrBuffers)> actuals
    )
    {
        for (int b = 0; b < expected.All.Count; b++)
        {
            var rawOracle = at.Initial.All[b].IsReadOnly ? at.Initial.All[b] : expected.All[b];
            bool isMultiset = rawOracle.Policy.Kind == OutputPolicyKind.Multiset ||
                (rawOracle.ColumnPolicies is not null && rawOracle.ColumnPolicies.Any(static p => p.Kind == OutputPolicyKind.Multiset));
            var oracle = isMultiset ? rawOracle.CreateSortedCopy() : rawOracle;
            var actualBuffers = new List<(DiffPathRunner Path, DiffBuffer Primary, DiffBuffer? Ptr)>(actuals.Count);
            foreach (var (path, buffers, ptrBuffers) in actuals)
            {
                var primary = isMultiset ? buffers.All[b].CreateSortedCopy() : buffers.All[b];
                var ptr = ptrBuffers is not null
                    ? (isMultiset ? ptrBuffers.All[b].CreateSortedCopy() : ptrBuffers.All[b])
                    : null;
                actualBuffers.Add((path, primary, ptr));
            }

            for (int i = 0; i < oracle.Length; i++)
            {
                if (actualBuffers.All(a => oracle.CellsMatch(a.Primary, i) && (a.Ptr is null || oracle.CellsMatch(a.Ptr, i))))
                {
                    continue;
                }

                var cells = new List<PathCell>(actualBuffers.Count);
                string column = oracle.ColumnNameOf(i);
                foreach (var (path, primary, ptr) in actualBuffers)
                {
                    bool primaryDiffers = !oracle.CellsMatch(primary, i);
                    bool ptrDiffers = ptr is not null && !oracle.CellsMatch(ptr, i);
                    bool differs = primaryDiffers || ptrDiffers;
                    var displayedBuffer = !primaryDiffers && ptrDiffers && ptr is not null ? ptr : primary;
                    var entry = differs ? run.FindEntry(column, path.Path, at.Shape) : null;
                    cells.Add(new PathCell(path.Path, displayedBuffer.FormatCell(i), differs, entry));
                }

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
    }

    private static void RunPropertyChecks(
        in CaseRun run,
        in ShapeRun at,
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
            if (testCase.Verify is { } verify)
            {
                var res = verify(buffers, at.Shape);
                if (!res.Success)
                {
                    run.Report.Add(new PropertyFailure(at.Seed, path.Path, res.FailureMessage ?? "Verify failed.", at.Shape));
                }

                if (ptrBuffers is not null)
                {
                    var ptrRes = verify(ptrBuffers, at.Shape);
                    if (!ptrRes.Success)
                    {
                        run.Report.Add(new PropertyFailure(at.Seed, path.Path, ptrRes.FailureMessage ?? "Verify (LaunchPtr) failed.", at.Shape));
                    }
                }
            }

            if (testCase.VerifyAgainstInitial is { } verifyAgainstInitial)
            {
                var res = verifyAgainstInitial(at.Initial, buffers, at.Shape);
                if (!res.Success)
                {
                    run.Report.Add(new PropertyFailure(at.Seed, path.Path, res.FailureMessage ?? "VerifyAgainstInitial failed.", at.Shape));
                }

                if (ptrBuffers is not null)
                {
                    var ptrRes = verifyAgainstInitial(at.Initial, ptrBuffers, at.Shape);
                    if (!ptrRes.Success)
                    {
                        run.Report.Add(new PropertyFailure(at.Seed, path.Path, ptrRes.FailureMessage ?? "VerifyAgainstInitial (LaunchPtr) failed.", at.Shape));
                    }
                }
            }
        }
    }

    // The inputs with one value per row, as name=value
    private static string FormatInputs(DiffBuffers initial, int rows, int row) =>
        string.Join("  ", initial.All
            .Where(b => !b.IsOutput && b.Length == rows && row < rows)
            .Select(b => $"{b.Name}={b.FormatCell(row)}")
        );

    private static void AddStaleEntries(in CaseRun run)
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
                if ((entry.Paths & path.Path) == 0 ||
                    report.Failures.Any(f => f.Path == path.Path && (f.Shape is null || entry.ShapePredicate is null || entry.ShapePredicate(f.Shape))))
                {
                    continue;
                }

                bool seen = report.Mismatches.Any(m =>
                    m.ColumnName == entry.Column &&
                    (m.Shape is null || entry.ShapePredicate is null || entry.ShapePredicate(m.Shape)) &&
                    m.Paths.Any(p => p.Path == path.Path && p.Known == entry));
                if (!seen)
                {
                    report.Add(new StaleDivergence(entry, path.Path));
                }
            }
        }
    }
}
