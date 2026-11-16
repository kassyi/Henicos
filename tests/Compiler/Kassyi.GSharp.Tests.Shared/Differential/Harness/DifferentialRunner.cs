#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Runs a <see cref="DifferentialCase"/> on every path of this process and compares each with the oracle.
/// For each seed and launch shape: build the inputs, run the oracle in three thread orders and require the same output,
/// run every path on its own copy of the inputs, and compare every buffer cell by cell.
/// </summary>
public static partial class DifferentialRunner
{
    static partial void EnsureEnvironmentReady();

    internal readonly record struct CaseRun(
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

    internal readonly record struct ShapeRun(int Seed, int ShapeIndex, LaunchShape Shape, DiffBuffers Initial);

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
        EnsureEnvironmentReady();
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
    /// Evaluates a batch of <see cref="DifferentialCase"/> instances, running path preflight checks only once at the
    /// start of the batch.
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
            reports.Add(
                EvaluateCore(
                    testCase,
                    effectiveSeeds,
                    activePaths,
                    activeKnown,
                    testSeed,
                    checkStale,
                    runPreflight: false
                )
            );
        }

        return reports;
    }

    /// <summary>
    /// Evaluates a batch of <see cref="DifferentialCase"/> instances, running path preflight checks only once at the
    /// start of the batch.
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
            reports.Add(
                EvaluateCore(
                    testCase,
                    effectiveSeeds,
                    activePaths,
                    activeKnown,
                    testSeed,
                    checkStale,
                    runPreflight: false
                )
            );
        }

        return reports;
    }

    /// <summary>Runs the case and returns the report without failing the test.</summary>
    /// <param name="testCase">The case.</param>
    /// <param name="seeds">The seeds.</param>
    /// <param name="paths">The paths to compare with the oracle.</param>
    /// <param name="known">The known divergences; entries of other cases are ignored.</param>
    /// <param name="testSeed">The <c>TEST_SEED</c> the seeds came from, for the report.</param>
    /// <param name="checkStale">
    /// Whether to report known divergences that no longer reproduce; false when <paramref name="seeds"/> are only some
    /// of the seeds that run by default.
    /// </param>
    /// <exception cref="DifferentialHarnessException">
    /// The case is malformed, or the oracle threw or depends on the thread order.
    /// </exception>
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
            DifferentialOutputVerifier.AddStaleEntries(in run);
        }

        return report;
    }

    private static void ValidateCase(
        DifferentialCase testCase,
        IReadOnlyList<int> seeds,
        IReadOnlyList<DiffPathRunner> paths
    )
    {
        bool hasOracle = testCase.EffectiveOracle is not null;
        bool hasReference = testCase.Reference is not null;
        bool hasVerifier = testCase.Verify is not null || testCase.VerifyAgainstInitial is not null;
        bool hasExpectedException = testCase.ExpectedException is not null || testCase.ExceptionPolicy is not null;

        if (hasOracle && hasReference)
        {
            throw new DifferentialHarnessException($"{testCase.Name}: set at most one of Oracle and Reference");
        }

        if (testCase.Oracle is not null && testCase.OracleIsLaunch)
        {
            throw new DifferentialHarnessException($"{testCase.Name}: set at most one of Oracle and OracleIsLaunch");
        }

        if (!hasOracle && !hasReference && !hasVerifier && !hasExpectedException)
        {
            throw new DifferentialHarnessException(
                $"{testCase.Name}: set Oracle, Reference, Verify/VerifyAgainstInitial, or ExpectedException"
            );
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
        bool hasOracle = testCase.EffectiveOracle is not null;
        bool hasReference = testCase.Reference is not null;

        var activeExPolicy =
            testCase.ExpectedException?.Invoke(new DiffRandom(at.Seed), at.Shape) ?? testCase.ExceptionPolicy;
        DiffBuffers? expected = null;
        DiffBuffers? afterException = null;

        if (activeExPolicy is not null && hasOracle)
        {
            afterException = DifferentialOracleRunner.RunOracleExpecting(testCase, in at, activeExPolicy);
        }
        else if (activeExPolicy is null)
        {
            if (!hasOracle && !hasReference &&
                at.Initial.All.Any(static b => b.IsOutput && b.Policy.Kind != OutputPolicyKind.Ignore))
            {
                throw new DifferentialHarnessException(
                    $"{testCase.Name}: when neither Oracle nor Reference is set, "
                        + "all output buffers must use OutputPolicy.Ignore"
                );
            }

            if (hasOracle || hasReference)
            {
                expected = DifferentialOracleRunner.RunOracle(
                    testCase,
                    at.Initial,
                    at.Shape,
                    at.Seed,
                    out activeExPolicy,
                    out afterException
                );
            }
        }

        if (activeExPolicy is not null)
        {
            return DifferentialExceptionEvaluator.EvaluateExceptionExpectation(
                in run,
                in at,
                activeExPolicy,
                allowSubprocess,
                afterException
            );
        }

        var shape = at.Shape;
        var actuals = new List<(DiffPathRunner Path, DiffBuffers Buffers, DiffBuffers? PtrBuffers)>();
        foreach (var path in run.Paths)
        {
            if (run.Case.SkippedPaths is not null && run.Case.SkippedPaths.TryGetValue(path.Path, out string? _))
            {
                continue;
            }

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
                var entry = run.FindEntry(KnownDivergence.ExceptionColumn, path.Path, shape);
                run.Report.Add(new PathFailure(at.Seed, path.Path, ex, shape, entry));
            }
        }

        if (expected is not null)
        {
            DifferentialOutputVerifier.Compare(in run, in at, expected, actuals);
        }

        DifferentialOutputVerifier.RunPropertyChecks(in run, in at, actuals);
        return false;
    }
}
