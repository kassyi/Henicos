#nullable enable
using System;
using System.Collections.Generic;

namespace Kassyi.GSharp.Tests.Shared.Differential;

// The evaluation of a seed whose oracle throws, or that expects an exception: every checked path must throw the same
// exception and leave the writes the oracle made: every thread runs to its end or to its own exception, as on the GPU
internal static class DifferentialExceptionEvaluator
{
    // afterException: the outputs of the oracle at its exception, which the paths that throw must match, or null when
    // the case has no oracle
    public static bool EvaluateExceptionExpectation(
        in DifferentialRunner.CaseRun run,
        in DifferentialRunner.ShapeRun at,
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
            var cell = EvaluatePathException(
                in run,
                in at,
                policy,
                allowSubprocess,
                path,
                ref launchedSubprocess,
                out var matchedBuffers
            );
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
            DifferentialOutputVerifier.Compare(in run, in at, afterException, thrownOutputs, absentPathsMatch: true);
        }

        if (anyDiffers)
        {
            run.Report.Add(new CellMismatch(
                at.Seed,
                new CellLocation(KnownDivergence.ExceptionColumn, 0, KnownDivergence.ExceptionColumn),
                DifferentialOutputVerifier.FormatInputs(at.Initial, at.Shape.Rows, 0),
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
        in DifferentialRunner.CaseRun run,
        in DifferentialRunner.ShapeRun at,
        ExceptionPolicy policy,
        bool allowSubprocess,
        DiffPathRunner path,
        ref bool launchedSubprocess,
        out DiffBuffers? matchedBuffers
    )
    {
        matchedBuffers = null;
        if (run.Case.SkippedPaths is not null &&
            run.Case.SkippedPaths.TryGetValue(path.Path, out string? skipReason))
        {
            return new PathCell(path.Path, $"skipped ({skipReason})", Differs: false, Known: null);
        }

        if ((policy.Paths & path.Path) == 0)
        {
            return new PathCell(path.Path, "skipped (unchecked path)", Differs: false, Known: null);
        }

        Exception? thrown;
        DiffBuffers? ranBuffers = null;
        DiffBuffers? ptrBuffers = null;
        long stackLaunches = 0;

        // [WHY] A failing CUDA thread records its error and exits, so a fault seed runs in this process like on the
        // other paths. A CUDA exception cell that is a known divergence is the exception: CUDA does not check what C#
        // checks there (a release build has no bounds or null check), so the launch may read or write an illegal
        // address, a real GPU fault that leaves the context of the process unusable
        if (path.Path == DiffPath.Cuda &&
            ReferenceEquals(path.Run, DiffPaths.DirectRunner) &&
            run.FindEntry(KnownDivergence.ExceptionColumn, path.Path, at.Shape) is not null)
        {
            if (RunCudaFaultSeed(in run, in at, allowSubprocess, ref launchedSubprocess, out thrown) is { } skipped)
            {
                return skipped;
            }
        }
        else
        {
            thrown = LaunchExpectingException(run.Case, path, at, run.RecordRun, out ranBuffers, out ptrBuffers, out stackLaunches);
        }

        if (run.RecordRun && DiffRecordWriter.Active is { } writer)
        {
            writer.Write(run.Case.Name, at.Seed, at.ShapeIndex, path.Path, at.Shape, ranBuffers, ptrBuffers, thrown, stackLaunches);
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
        in DifferentialRunner.CaseRun run,
        in DifferentialRunner.ShapeRun at,
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
            return new PathCell(
                DiffPath.Cuda,
                $"skipped (CUDA fault seed {CudaTrapGate.SkipReason})",
                Differs: false,
                Known: null
            );
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
        thrown = CudaTrapGate.RunPaced(
            () => CudaTrapSubprocessRunner.RunInSubprocess(testCase.Name, shapeIndex, seed)
        );
        return null;
    }

    // Launches the case on path in this process: the exception it throws, or null, and the buffers it wrote
    private static Exception? LaunchExpectingException(
        DifferentialCase testCase,
        DiffPathRunner path,
        in DifferentialRunner.ShapeRun at,
        bool recordRun,
        out DiffBuffers buffers,
        out DiffBuffers? ptrBuffers,
        out long stackLaunches
    )
    {
        var shape = at.Shape;
        var initial = at.Initial;
        var launched = initial.Clone();
        launched.Shape = shape;
        buffers = launched;
        ptrBuffers = null;
        DiffBuffers? ptrRan = null;
        long launches = 0;
        var writer = recordRun ? DiffRecordWriter.Active : null;
        try
        {
            if (writer is null)
            {
                path.Run(() => testCase.Launch(launched, shape));
            }
            else
            {
                path.Run(() =>
                {
                    long before = DiffRecordWriter.ReadLaunchCount(path.Path);
                    try
                    {
                        testCase.Launch(launched, shape);
                    }
                    finally
                    {
                        launches = DiffRecordWriter.ReadLaunchCount(path.Path) - before;
                    }
                });
            }

            if (path.Path == DiffPath.Cuda && testCase.LaunchPtr is { } launchPtr)
            {
                ptrRan = initial.Clone();
                ptrRan.Shape = shape;
                path.Run(() => launchPtr(ptrRan, shape));
            }

            ptrBuffers = ptrRan;
            stackLaunches = launches;
            return null;
        }
        catch (Exception ex)
        {
            ptrBuffers = ptrRan;
            stackLaunches = launches;
            return ex;
        }
    }
}
