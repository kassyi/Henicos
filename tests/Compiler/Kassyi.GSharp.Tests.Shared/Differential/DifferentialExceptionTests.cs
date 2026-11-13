#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.DifferentialHarnessFakes;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Tests of the exception side of the differential harness: <see cref="ExceptionPolicy"/>, the CUDA fault seeds,
/// skipped paths and the child process protocol of <see cref="CudaTrapSubprocessRunner"/>. Each check names, above it,
/// the one-line change of the harness (mutant) that makes it fail; the shared fakes are
/// <see cref="DifferentialHarnessFakes"/>.
/// </summary>
public class DifferentialExceptionTests
{
    // ---- Exception policies and CUDA traps ----

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void ExceptionPolicy_MatchOracle_PassesOnMatchingThrow()
    {
        // When OracleExceptions is MatchOracle (or ExpectedException is set), a seed that throws the same exception type
        // on the oracle and on every target path passes without recording PathFailure or UnexpectedMismatches
        // mutant: DifferentialRunner.cs EvaluateCore ignores activeExPolicy and adds thrown exception to PathFailure
        var matchOracleCase = new DifferentialCase("HarnessTests.BoundsFaultKernel")
        {
            Shape = LaunchShapes.Linear(4, blockX: 4),
            OracleExceptions = OracleExceptionMode.MatchOracle,
            ExceptionPaths = DiffPath.Managed | DiffPath.Native,
            Inputs = static (rng, shape) =>
            {
                var indices = new int[shape.Rows];
                for (int i = 0; i < shape.Rows; i++)
                {
                    indices[i] = i;
                }

                if (!rng.IsBoundarySeed)
                {
                    indices[0] = -1;
                }

                return new DiffBuffers()
                    .In("idx", indices)
                    .Out<int>("r", shape.Rows, OutputPolicy.Exact);
            },
            Launch = static (b, shape) => DoubleIndexOnHost(b, shape, ThreadStop.FailingThread),
            Oracle = static (b, s) => HarnessProbeKernels.LaunchDoubleIndexKernel(
                b.Get<int>("idx"),
                b.Get<int>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
        };

        DiffPathRunner[] bothCpuPaths =
        [
            new(DiffPath.Managed, DiffPaths.RunDirect),
            new(DiffPath.Native, DiffPaths.RunDirect),
        ];

        // Seed 0 succeeds and compares cells; Seed 1 throws IndexOutOfRangeException on Oracle, Managed, and Native
        var passingReport = DifferentialRunner.Evaluate(matchOracleCase, [0, 1], bothCpuPaths, NoEntries);
        Assert.False(passingReport.HasFailures, passingReport.ToString());
        Assert.Empty(passingReport.Failures);
        Assert.Empty(passingReport.UnexpectedMismatches);

        // If Native fails to throw on seed 1, an unexpected mismatch on "$exception" is reported unless covered by KnownDivergence
        var silentNativeCase = new DifferentialCase("HarnessTests.SilentNativeFaultKernel")
        {
            Shape = LaunchShapes.SingleThread(),
            ExpectedException = static (_, _) => ExceptionPolicy.For<IndexOutOfRangeException>(
                DiffPath.Managed | DiffPath.Native,
                memberName: "SilentNativeFaultKernel"
            ),
            Inputs = static (_, _) => new DiffBuffers().Out<int>("r", 1, OutputPolicy.Exact),
            Launch = static (_, _) => { },
        };

        DiffPathRunner[] managedThrowsNativeSilent =
        [
            new(DiffPath.Managed, static _ =>
            {
                var ex = new IndexOutOfRangeException("Index was outside the bounds of the array.");
                ex.Data["GSharp.MemberName"] = "SilentNativeFaultKernel";
                throw ex;
            }),
            new(DiffPath.Native, DiffPaths.RunDirect),
        ];

        var missingThrowReport = DifferentialRunner.Evaluate(silentNativeCase, [1], managedThrowsNativeSilent, NoEntries);
        Assert.True(missingThrowReport.HasFailures);
        Assert.Single(missingThrowReport.UnexpectedMismatches);
        Assert.Equal(KnownDivergence.ExceptionColumn, missingThrowReport.UnexpectedMismatches[0].ColumnName);

        KnownDivergence[] exceptionEntry =
        [
            new("HarnessTests.SilentNativeFaultKernel", KnownDivergence.ExceptionColumn, DiffPath.Native, Issue: 718),
        ];
        var excusedReport = DifferentialRunner.Evaluate(silentNativeCase, [1], managedThrowsNativeSilent, exceptionEntry);
        Assert.False(excusedReport.HasFailures, excusedReport.ToString());
    }

    // A seed whose oracle throws compares the writes too: thread 0 of four reads idx[-1], and threads 1 to 3 write
    // r[t] = t. A path that stops every thread at the first failure, or lets the failing thread write on, differs in r
    [Theory]
    [InlineData(nameof(ThreadStop.FailingThread), false, false)]
    [InlineData(nameof(ThreadStop.WholeLaunch), false, true)]
    [InlineData(nameof(ThreadStop.Never), false, true)]
    [InlineData(nameof(ThreadStop.FailingThread), true, false)]
    [InlineData(nameof(ThreadStop.WholeLaunch), true, true)]
    [InlineData(nameof(ThreadStop.Never), true, true)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void ExceptionSeed_PathStopsOtherThanTheFailingThread_ReportsTheWrites(
        string stop,
        bool explicitPolicy,
        bool differs
    )
    {
        var threadStop = Enum.Parse<ThreadStop>(stop);
        var testCase = new DifferentialCase("HarnessTests.ThreadStopKernel")
        {
            Shape = LaunchShapes.Linear(4, blockX: 4),
            Seeds = [1],
            Inputs = static (_, shape) => new DiffBuffers()
                .In("idx", new[] { -1, 1, 2, 3 })
                .Out<int>("r", shape.Rows, OutputPolicy.Exact),
            Launch = (b, shape) => DoubleIndexOnHost(b, shape, threadStop),
            Oracle = static (b, s) => HarnessProbeKernels.LaunchDoubleIndexKernel(
                b.Get<int>("idx"),
                b.Get<int>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
        };
        // mutant: DifferentialRunner.RunOneShape skips RunOracleExpecting for an explicit policy (no comparison)
        testCase = explicitPolicy
            ? testCase with
            {
                ExceptionPolicy = ExceptionPolicy.For<IndexOutOfRangeException>(DiffPath.Managed | DiffPath.Native),
            }
            : testCase with
            {
                OracleExceptions = OracleExceptionMode.MatchOracle,
                ExceptionPaths = DiffPath.Managed | DiffPath.Native,
            };
        DiffPathRunner[] paths =
        [
            new(DiffPath.Managed, DiffPaths.RunDirect),
            new(DiffPath.Native, DiffPaths.RunDirect),
        ];

        var report = DifferentialRunner.Evaluate(testCase, [1], paths, NoEntries);

        // mutant: DifferentialRunner.RunOracle sets afterException = null for MatchOracle (the writes are not compared)
        Assert.Equal(differs, report.HasFailures);
        Assert.DoesNotContain(report.Mismatches, static m => m.ColumnName == KnownDivergence.ExceptionColumn);
        Assert.Equal(differs, report.Mismatches.Any(static m => m.ColumnName == "r"));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void CudaFaultSeeds_DirectRunner_RunsInProcess()
    {
        // A failing CUDA thread exits instead of trapping, so a fault seed on the CUDA direct runner whose CUDA cell is
        // not a known divergence runs in this process like on every other path, and its exception is matched against
        // the policy (CudaFaultSeeds_TrapRunsDisabled_IsSkippedWithReason covers a known divergence)
        // mutant: DifferentialRunner.cs EvaluateExceptionExpectation drops the known-divergence condition of the CUDA gate
        int launches = 0;
        var faultCase = new DifferentialCase("HarnessTests.CudaFaultSeedKernel")
        {
            Shape = LaunchShapes.SingleThread(),
            ExpectedException = static (_, _) => ExceptionPolicy.For<IndexOutOfRangeException>(
                DiffPath.Managed | DiffPath.Cuda,
                memberName: "CudaFaultSeedKernel"
            ),
            Inputs = static (_, _) => new DiffBuffers().Out<int>("r", 1, OutputPolicy.Exact),
            Launch = (_, _) =>
            {
                launches++;
                var ex = new IndexOutOfRangeException("Index was outside the bounds of the array.");
                ex.Data["GSharp.MemberName"] = "CudaFaultSeedKernel";
                throw ex;
            },
        };
        DiffPathRunner[] paths =
        [
            new(DiffPath.Managed, DiffPaths.RunDirect),
            new(DiffPath.Cuda, DiffPaths.DirectRunner),
        ];

        var report = DifferentialRunner.Evaluate(faultCase, [1, 2], paths, NoEntries);

        Assert.False(report.HasFailures, report.ToString());
        Assert.Equal(4, launches);

        // A launch that does not throw is a mismatch of its cell
        var silentCase = faultCase with { Launch = static (_, _) => { } };
        var silentReport = DifferentialRunner.Evaluate(silentCase, [1], paths, NoEntries);
        Assert.True(silentReport.HasFailures, silentReport.ToString());
        Assert.Contains("no exception thrown", silentReport.ToString());
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void CudaFaultSeeds_TrapRunsDisabled_IsSkippedWithReason()
    {
        // A fault seed on the CUDA direct runner would start a child process that raises a real GPU fault (#1457); by
        // default the cell is skipped, the report lists the seed with the reason, and a known CUDA exception entry is
        // not reported stale
        // mutant: DifferentialRunner.cs EvaluateExceptionExpectation drops the `!CudaTrapGate.IsEnabled` gate
        // mutant: DifferentialRunner.cs AddStaleEntries drops the skip of CUDA exception entries
        Skip.If(CudaTrapGate.IsEnabled, $"{CudaTrapGate.EnableVariable}=1 runs the fault seeds.");
        var faultCase = new DifferentialCase("HarnessTests.CudaFaultSeedKernel")
        {
            Shape = LaunchShapes.SingleThread(),
            ExpectedException = static (_, _) => ExceptionPolicy.For<IndexOutOfRangeException>(
                DiffPath.Managed | DiffPath.Cuda,
                memberName: "CudaFaultSeedKernel"
            ),
            Inputs = static (_, _) => new DiffBuffers().Out<int>("r", 1, OutputPolicy.Exact),
            Launch = static (_, _) =>
            {
                var ex = new IndexOutOfRangeException("Index was outside the bounds of the array.");
                ex.Data["GSharp.MemberName"] = "CudaFaultSeedKernel";
                throw ex;
            },
        };
        DiffPathRunner[] paths =
        [
            new(DiffPath.Managed, DiffPaths.RunDirect),
            new(DiffPath.Cuda, DiffPaths.DirectRunner),
        ];
        KnownDivergence[] cudaEntry =
        [
            new("HarnessTests.CudaFaultSeedKernel", KnownDivergence.ExceptionColumn, DiffPath.Cuda, Issue: 718),
        ];

        var report = DifferentialRunner.Evaluate(faultCase, [1, 2], paths, cudaEntry);

        Assert.False(report.HasFailures, report.ToString());
        Assert.Equal([1, 2], report.SkippedCudaTraps.Select(static t => t.Seed));
        Assert.Contains("CUDA fault seeds not run: 2", report.ToString());
        Assert.Contains("just test-gpu-trap", report.ToString());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void DifferentialCase_SkippedPaths_AreRecordedInReport()
    {
        // When a path is in SkippedPaths, it is reported as skipped with the given reason string and not executed
        bool nativeRan = false;
        var skippedNativeCase = new DifferentialCase("HarnessTests.SkippedNativeKernel")
        {
            Shape = LaunchShapes.SingleThread(),
            SkippedPaths = new Dictionary<DiffPath, string>
            {
                [DiffPath.Native] = "#718 (x86 idiv terminates process)",
            },
            ExpectedException = static (_, _) => ExceptionPolicy.For<DivideByZeroException>(DiffPath.Managed | DiffPath.Native),
            Inputs = static (_, _) => new DiffBuffers().Out<int>("r", 1, OutputPolicy.Exact),
            Launch = static (_, _) => { },
        };

        DiffPathRunner[] paths =
        [
            new(DiffPath.Managed, static _ => throw new DivideByZeroException("Attempted to divide by zero.")),
            new(DiffPath.Native, run =>
            {
                nativeRan = true;
                run();
            }),
        ];

        var report = DifferentialRunner.Evaluate(skippedNativeCase, [1], paths, NoEntries);
        Assert.False(report.HasFailures, report.ToString());
        Assert.False(nativeRan);
        Assert.Empty(report.Mismatches);

        // When a mismatch does occur on another path, the skipped path shows the reason
        DiffPathRunner[] pathsWithMismatch =
        [
            new(DiffPath.Managed, static _ => { }),
            new(DiffPath.Native, run => run()),
        ];
        var mismatchReport = DifferentialRunner.Evaluate(skippedNativeCase, [1], pathsWithMismatch, NoEntries);
        Assert.True(mismatchReport.HasFailures);
        var mismatch = Assert.Single(mismatchReport.Mismatches);
        var nativeCell = Assert.Single(mismatch.Paths.Where(static p => p.Path == DiffPath.Native));
        Assert.Contains("skipped (#718 (x86 idiv terminates process))", nativeCell.Value);
    }

    // How a host launch of the double-index kernel stops after a thread fails
    public enum ThreadStop
    {
        // Only the failing thread, as on the GPU
        FailingThread,

        // Every thread from the failing one on
        WholeLaunch,

        // None: the failing thread writes on
        Never,
    }

    // r[t] = idx[idx[t]] on the host for each thread, stopping as stop says; throws the first failure at the end
    private static void DoubleIndexOnHost(DiffBuffers b, LaunchShape shape, ThreadStop stop)
    {
        var idx = b.Get<int>("idx");
        var r = b.Get<int>("r");
        Exception? first = null;
        for (int t = 0; t < shape.Rows; t++)
        {
            if ((uint)idx[t] < (uint)idx.Length)
            {
                r[t] = idx[idx[t]];
                continue;
            }

            first ??= new IndexOutOfRangeException("Index was outside the bounds of the array.");
            if (stop == ThreadStop.WholeLaunch)
            {
                break;
            }

            if (stop == ThreadStop.Never)
            {
                r[t] = 99;
            }
        }

        if (first is not null)
        {
            throw first;
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void ExceptionPolicy_Matches_RejectsMissingLocationOrMemberName()
    {
        // When MemberName, SourceFile, Line, or Column is specified on the policy, an exception without that metadata
        // must fail Matches instead of passing silently
        // mutant: ExceptionPolicy.cs Matches skips MemberName check when actual.Data["GSharp.MemberName"] is not a string
        // mutant: ExceptionPolicy.cs Matches skips SourceLocation check when actual.Data["GSharp.SourceLocation"] is not a string
        var bareEx = new IndexOutOfRangeException("bounds fault");

        var memberPolicy = new ExceptionPolicy(typeof(IndexOutOfRangeException)) { MemberName = "MyKernel" };
        Assert.False(memberPolicy.Matches(bareEx, DiffPath.Managed, out string? memberReason));
        Assert.Contains("expected MemberName \"MyKernel\", got none", memberReason);

        var filePolicy = new ExceptionPolicy(typeof(IndexOutOfRangeException)) { SourceFile = "MyKernels.cu.cs" };
        Assert.False(filePolicy.Matches(bareEx, DiffPath.Managed, out string? fileReason));
        Assert.Contains("expected SourceFile \"MyKernels.cu.cs\" in SourceLocation, got none", fileReason);

        var linePolicy = new ExceptionPolicy(typeof(IndexOutOfRangeException)) { Line = 42 };
        Assert.False(linePolicy.Matches(bareEx, DiffPath.Managed, out string? lineReason));
        Assert.Contains("expected Line 42 in SourceLocation, got none", lineReason);

        var colPolicy = new ExceptionPolicy(typeof(IndexOutOfRangeException)) { Column = 9 };
        Assert.False(colPolicy.Matches(bareEx, DiffPath.Managed, out string? colReason));
        Assert.Contains("expected Column 9 in SourceLocation, got none", colReason);

        var fullPolicy = new ExceptionPolicy(typeof(IndexOutOfRangeException))
        {
            MemberName = "MyKernel",
            SourceFile = "MyKernels.cu.cs",
            Line = 42,
            Column = 9,
        };
        bareEx.Data["GSharp.MemberName"] = "MyKernel";
        bareEx.Data["GSharp.SourceLocation"] = "MyKernels.cu.cs(42,9)";
        Assert.True(fullPolicy.Matches(bareEx, DiffPath.Managed, out string? fullReason), fullReason);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void CudaTrapSubprocessRunner_ProtocolAndTimeout_HandlesExecution()
    {
        // (1) Child target protocol writes the structured exception to the file in GSHARP_DIFF_CUDA_TRAP_RESULT_FILE
        //     (surviving TestConsole.Silence()), and preserves the original inner exception type name for RequireCudaInnerException
        // mutant: CudaTrapSubprocessRunner.cs WriteChildResult writes only to Console.Out instead of the result file
        // mutant: ExceptionPolicy.cs Matches checks actual.InnerException.GetType().Name instead of SubprocessCudaInnerException.OriginalTypeName
        string tempResultFile = Path.Combine(Path.GetTempPath(), $"gsharp-harness-test-{Guid.NewGuid():N}.txt");

        try
        {
            // Scoped to this test's flow: setting the process environment would make every differential test running
            // in parallel act as a child and pass with an empty report
            using var child = CudaTrapSubprocessRunner.SimulateChild(
                CudaTrapSubprocessRunner.FormatTarget("HarnessTests.ChildTrapCase", shapeIndex: 0, seed: 3),
                tempResultFile
            );

            // A non-target case in the same child process returns an empty report without writing the result file
            var otherReport = FakeCase().Evaluate([0]);
            Assert.False(otherReport.HasFailures);
            Assert.False(File.Exists(tempResultFile));

            // The targeted case runs only the requested shape/seed and writes its thrown exception to tempResultFile
            var trapCase = new DifferentialCase("HarnessTests.ChildTrapCase")
            {
                Shape = LaunchShapes.SingleThread(),
                ExpectedException = static (_, _) => new ExceptionPolicy(typeof(IndexOutOfRangeException), DiffPath.Cuda)
                {
                    MemberName = "ChildTrapKernel",
                    SourceFile = "ChildTrap.cu.cs",
                    Line = 17,
                    Column = 13,
                    RequireCudaInnerException = true,
                },
                Inputs = static (rng, _) => new DiffBuffers().InScalar("seed", rng.Seed),
                Launch = static (b, _) =>
                {
                    Assert.Equal(3, b.GetScalar<int>("seed"));
                    var ex = new IndexOutOfRangeException("device trap at seed 3", new FakeCudaException("CUDA_ERROR_ILLEGAL_INSTRUCTION"));
                    ex.Data["GSharp.MemberName"] = "ChildTrapKernel";
                    ex.Data["GSharp.SourceLocation"] = "ChildTrap.cu.cs(17,13)";
                    throw ex;
                },
            };

            DifferentialRunner.Evaluate(trapCase, [0, 3], [new DiffPathRunner(DiffPath.Cuda, DiffPaths.RunDirect)], NoEntries);
            Assert.True(CudaTrapSubprocessRunner.TryReadChildResult(tempResultFile, out var reconstructed));
            Assert.NotNull(reconstructed);

            var cudaPolicy = new ExceptionPolicy(typeof(IndexOutOfRangeException), DiffPath.Cuda)
            {
                MemberName = "ChildTrapKernel",
                SourceFile = "ChildTrap.cu.cs",
                Line = 17,
                Column = 13,
                RequireCudaInnerException = true,
            };
            Assert.True(cudaPolicy.Matches(reconstructed, DiffPath.Cuda, out string? matchReason), matchReason);

            // If the inner exception is not a CudaException, RequireCudaInnerException rejects the reconstructed exception
            var nonCudaEx = new IndexOutOfRangeException("non-cuda inner", new InvalidOperationException("not cuda"));
            nonCudaEx.Data["GSharp.MemberName"] = "ChildTrapKernel";
            nonCudaEx.Data["GSharp.SourceLocation"] = "ChildTrap.cu.cs(17,13)";
            CudaTrapSubprocessRunner.WriteChildResult(nonCudaEx, tempResultFile);
            Assert.True(CudaTrapSubprocessRunner.TryReadChildResult(tempResultFile, out var nonCudaReconstructed));
            Assert.NotNull(nonCudaReconstructed);
            Assert.False(cudaPolicy.Matches(nonCudaReconstructed, DiffPath.Cuda, out string? nonCudaReason));
            Assert.Contains("expected CudaException InnerException on Cuda path", nonCudaReason);
        }
        finally
        {
            if (File.Exists(tempResultFile))
            {
                File.Delete(tempResultFile);
            }
        }

        // (2) RunProcessWithTimeout kills a hung process tree when the timeout expires
        // mutant: CudaTrapSubprocessRunner.cs RunProcessWithTimeout returns TimedOut: false when WaitForExit(timeoutMs) fails
        var hangPsi = new ProcessStartInfo(
            OperatingSystem.IsWindows() ? "pwsh" : "sh"
        )
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            hangPsi.ArgumentList.Add("-NoProfile");
            hangPsi.ArgumentList.Add("-Command");
            hangPsi.ArgumentList.Add("Start-Sleep -Seconds 30");
        }
        else
        {
            hangPsi.ArgumentList.Add("-c");
            hangPsi.ArgumentList.Add("sleep 30");
        }

        var (_, _, _, timedOut) = CudaTrapSubprocessRunner.RunProcessWithTimeout(hangPsi, timeoutMs: 150);
        Assert.True(timedOut);
    }

    // ---- Helpers ----

    private sealed class FakeCudaException(string message) : Exception(message);
}
