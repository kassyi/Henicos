#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

// The evaluation of a seed whose oracle throws, or that expects an exception: every checked path must throw the same
// exception and leave the writes the oracle made: every thread runs to its end or to its own exception, as on the GPU
public static partial class DifferentialRunner
{
    // Runs the oracle in every order of the case, each on its own copy of the inputs, and requires the same outcome in
    // all of them: the same exception type, or none, and the same outputs (an output that depends on the order is a
    // race, also when a thread throws). Returns the outputs of the first order, and in thrown its exception or null
    private static DiffBuffers RunOracleOrders(
        DifferentialCase testCase,
        Action<DiffBuffers, LaunchShape> oracle,
        DiffBuffers initial,
        LaunchShape shape,
        int seed,
        out Exception? thrown
    )
    {
        var orders = testCase.OracleOrders ?? [ThreadOrder.Forward, ThreadOrder.Reverse, ThreadOrder.Permutation(seed)];
        var firstOrder = orders.Count > 0 ? orders[0] : ThreadOrder.Forward;
        var expected = initial.Clone();
        expected.Shape = shape;
        var firstThrown = TryRunOrder(shape, firstOrder, () => oracle(expected, shape));
        for (int i = 1; i < orders.Count; i++)
        {
            var order = orders[i];
            var other = initial.Clone();
            other.Shape = shape;
            var otherThrown = TryRunOrder(shape, order, () => oracle(other, shape));
            if (firstThrown?.GetType() != otherThrown?.GetType())
            {
                throw new DifferentialHarnessException(
                    $"{testCase.Name}: oracle exception depends on thread order for seed {seed}: " +
                    $"{firstOrder} {Outcome(firstThrown)}, but {order} {Outcome(otherThrown)}"
                );
            }

            CheckOrderIndependence(testCase, seed, expected, other, order);
        }

        thrown = firstThrown;
        return expected;
    }

    private static string Outcome(Exception? thrown) => thrown is null ? "succeeded" : $"threw {thrown.GetType().Name}";

    // The outputs of the oracle for a seed whose exception the case states (ExpectedException or ExceptionPolicy): the
    // oracle must throw that exception as well, and the paths must leave its writes
    private static DiffBuffers RunOracleExpecting(DifferentialCase testCase, in ShapeRun at, ExceptionPolicy policy)
    {
        var oracle = testCase.EffectiveOracle ?? throw new DifferentialHarnessException($"{testCase.Name}: no oracle");
        var expected = RunOracleOrders(testCase, oracle, at.Initial, at.Shape, at.Seed, out var thrown);
        if (thrown?.GetType() != policy.ExceptionType)
        {
            throw new DifferentialHarnessException(
                $"{testCase.Name}: the case expects {policy} for seed {at.Seed}, but the oracle {Outcome(thrown)}"
            );
        }

        return expected;
    }

    // afterException: the outputs of the oracle at its exception, which the paths that throw must match, or null when
    // the case has no oracle
    private static bool EvaluateExceptionExpectation(
        in CaseRun run,
        in ShapeRun at,
        ExceptionPolicy policy,
        bool allowSubprocess,
        DiffBuffers? afterException
    )
    {
        var cells = new List<PathCell>(run.Paths.Count);
        var thrownOutputs = new List<(DiffPathRunner Path, DiffBuffers Buffers, DiffBuffers? PtrBuffers)>();
        bool anyDiffers = false;
        bool launchedSubprocess = false;

        foreach (var path in run.Paths)
        {
            var cell = EvaluatePathException(in run, in at, policy, allowSubprocess, path, ref launchedSubprocess,
                out var matchedBuffers);
            cells.Add(cell);
            anyDiffers |= cell.Differs;
            if (matchedBuffers is not null)
            {
                thrownOutputs.Add((path, matchedBuffers, null));
            }
        }

        if (afterException is not null)
        {
            // A path that did not throw, or was not run, is reported by its exception cell above
            Compare(in run, in at, afterException, thrownOutputs, absentPathsMatch: true);
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

    // The exception cell of one path; matchedBuffers: the buffers of an in-process launch that threw the expected
    // exception, for the comparison after the exception, or null
    private static PathCell EvaluatePathException(
        in CaseRun run,
        in ShapeRun at,
        ExceptionPolicy policy,
        bool allowSubprocess,
        DiffPathRunner path,
        ref bool launchedSubprocess,
        out DiffBuffers? matchedBuffers
    )
    {
        matchedBuffers = null;
        if (run.Case.SkippedPaths is not null && run.Case.SkippedPaths.TryGetValue(path.Path, out string? skipReason))
        {
            return new PathCell(path.Path, $"skipped ({skipReason})", Differs: false, Known: null);
        }

        if ((policy.Paths & path.Path) == 0)
        {
            return new PathCell(path.Path, "skipped (unchecked path)", Differs: false, Known: null);
        }

        Exception? thrown;
        DiffBuffers? ranBuffers = null;

        // [WHY] A failing CUDA thread records its error and exits, so a fault seed runs in this process like on the
        // other paths. A CUDA exception cell that is a known divergence is the exception: CUDA does not check what C#
        // checks there (a release build has no bounds or null check), so the launch may read or write an illegal
        // address, a real GPU fault that leaves the context of the process unusable
        if (path.Path == DiffPath.Cuda && ReferenceEquals(path.Run, DiffPaths.DirectRunner) &&
            run.FindEntry(KnownDivergence.ExceptionColumn, path.Path, at.Shape) is not null)
        {
            if (RunCudaFaultSeed(in run, in at, allowSubprocess, ref launchedSubprocess, out thrown) is { } skipped)
            {
                return skipped;
            }
        }
        else
        {
            thrown = LaunchExpectingException(run.Case, path, at, out ranBuffers);
        }

        if (thrown is null)
        {
            var entry = run.FindEntry(KnownDivergence.ExceptionColumn, path.Path, at.Shape);
            return new PathCell(path.Path, "no exception thrown", Differs: true, entry);
        }

        var actual = ExceptionPolicy.Unwrap(thrown);
        if (!policy.Matches(thrown, path.Path, out string? mismatchReason))
        {
            var entry = run.FindEntry(KnownDivergence.ExceptionColumn, path.Path, at.Shape);
            return new PathCell(path.Path, $"{actual.GetType().Name} ({mismatchReason})", Differs: true, entry);
        }

        matchedBuffers = ranBuffers;
        return new PathCell(path.Path, actual.GetType().Name, Differs: false, Known: null);
    }

    // A CUDA fault seed that is a known divergence, run in a child process (null, and what it threw), or the cell of a
    // skipped one
    private static PathCell? RunCudaFaultSeed(
        in CaseRun run,
        in ShapeRun at,
        bool allowSubprocess,
        ref bool launchedSubprocess,
        out Exception? thrown
    )
    {
        thrown = null;
        var testCase = run.Case;
        if (!CudaTrapGate.IsEnabled)
        {
            // The CUDA cell is compared only by `just test-gpu-trap` (#1457), and the report lists the seeds left out
            run.Report.AddSkippedCudaTrap(at.Seed, at.Shape);
            return new PathCell(DiffPath.Cuda, $"skipped (CUDA fault seed {CudaTrapGate.SkipReason})", Differs: false,
                Known: null);
        }

        bool shouldRunSubprocess =
            testCase.CudaTrapSampling == CudaTrapSampling.AllFaults ||
            CudaTrapSubprocessRunner.IsFullSamplingEnabled ||
            allowSubprocess;
        if (!shouldRunSubprocess)
        {
            return new PathCell(DiffPath.Cuda, "sampled on first fault seed", Differs: false, Known: null);
        }

        launchedSubprocess = true;
        var (shapeIndex, seed) = (at.ShapeIndex, at.Seed);
        thrown = CudaTrapGate.RunPaced(() => CudaTrapSubprocessRunner.RunInSubprocess(testCase.Name, shapeIndex, seed));
        return null;
    }

    // Launches the case on path in this process: the exception it throws, or null, and the buffers it wrote
    private static Exception? LaunchExpectingException(
        DifferentialCase testCase,
        DiffPathRunner path,
        in ShapeRun at,
        out DiffBuffers buffers
    )
    {
        var shape = at.Shape;
        var initial = at.Initial;
        var launched = initial.Clone();
        launched.Shape = shape;
        buffers = launched;
        try
        {
            path.Run(() => testCase.Launch(launched, shape));
            if (path.Path == DiffPath.Cuda && testCase.LaunchPtr is { } launchPtr)
            {
                var ptrBuffers = initial.Clone();
                ptrBuffers.Shape = shape;
                path.Run(() => launchPtr(ptrBuffers, shape));
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
