#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public class DifferentialKnownDivergencesTests
{
    private const string FakeCaseName = "HarnessTests.FakeAddKernel";

    private static readonly string[] s_fakeColumns = ["sum", "xor"];

    // A host "path": the launch runs as it is, on the calling thread
    private static readonly IReadOnlyList<DiffPathRunner> s_nativeOnly = [new(DiffPath.Native, DiffPaths.RunDirect)];

    private static readonly IReadOnlyList<KnownDivergence> s_noEntries = [];

    // 5 rows in 3 blocks of 2: the last thread has no row and must return without writing
    private static readonly LaunchShape s_fakeShape = LaunchShape.ForRows(5, blockX: 2);

    // ---- Known divergences ----

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void KnownDivergence_StillDiffers_Passes()
    {
        // The registered column differs on the registered path: the case passes, and the cells are kept as known
        // mutant: KnownDivergences.cs Covers `(Paths & path) != 0` -> `(Paths & path) == 0`
        KnownDivergence[] entries = [new(FakeCaseName, "sum", DiffPath.Native, Issue: 999)];
        var report = FakeCase(corrupt: CorruptSums, corrupted: DiffPath.Native).Evaluate([0, 1], entries);

        Assert.False(report.HasFailures, report.ToString());
        Assert.Equal(10, report.Mismatches.Count);
        Assert.All(report.Mismatches, static m => Assert.True(m.IsKnown));
        Assert.Empty(report.StaleDivergences);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void KnownDivergence_NoLongerDiffers_Fails()
    {
        // The registered column matches the oracle on every seed (the bug was fixed): the case fails and asks for the
        // entry to be removed, so a fix cannot leave a stale entry behind
        // mutant: DifferentialRunner.cs Evaluate `AddStaleEntries(paths, entries, report);` removed
        KnownDivergence[] entries = [new(FakeCaseName, "sum", DiffPath.Native | DiffPath.Cuda, Issue: 999)];
        var report = FakeCase().Evaluate([0, 1], entries);

        Assert.True(report.HasFailures);
        Assert.Empty(report.Mismatches);
        var stale = Assert.Single(report.StaleDivergences);
        Assert.Equal((entries[0], DiffPath.Native), (stale.Entry, stale.Path));
        Assert.Contains("known divergence \"sum\" on native, cuda (#999) no longer differs on native for any seed: remove it from KnownDivergences", report.ToString());

        // A registered path that this process does not run (cuda) is not checked here: only native is reported above
        // mutant: DifferentialRunner.cs AddStaleEntries `if ((entry.Paths & path.Path) == 0) continue;` removed (managed is reported too)
        Assert.DoesNotContain(report.StaleDivergences, static s => s.Path == DiffPath.Managed);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void KnownDivergence_MatchingCaseColumnAndPaths_AppliesOnlyToTarget()
    {
        // An entry for another path, another column or another case does not excuse a divergence
        // mutant: KnownDivergences.cs Covers `Column == column &&` removed
        KnownDivergence[] otherPath = [new(FakeCaseName, "sum", DiffPath.Cuda, Issue: 999)];
        KnownDivergence[] otherColumn = [new(FakeCaseName, "xor", DiffPath.Native, Issue: 999)];
        KnownDivergence[] otherCase = [new("HarnessTests.Other", "sum", DiffPath.Native, Issue: 999)];

        foreach (var entries in new[] { otherPath, otherColumn, otherCase })
        {
            var report = FakeCase(DiffPath.Native, CorruptSums).Evaluate([0], entries);
            Assert.True(report.HasFailures);
            Assert.Equal(5, report.UnexpectedMismatches.Count);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void KnownDivergence_AnotherHost_IsIgnored()
    {
        // The native path is built by MSVC on Windows and GCC or Clang elsewhere, so an entry can apply to one host only.
        // On the other host it neither excuses a divergence nor is reported stale
        // mutant: DifferentialRunner.cs Evaluate `&& k.AppliesToThisHost` removed
        var otherHost = OperatingSystem.IsWindows() ? DiffHost.Unix : DiffHost.Windows;
        KnownDivergence[] entries = [new(FakeCaseName, "sum", DiffPath.Native, Issue: 999, Hosts: otherHost)];

        var diverging = FakeCase(DiffPath.Native, CorruptSums).Evaluate([0], entries);
        Assert.Equal(5, diverging.UnexpectedMismatches.Count);

        var matching = FakeCase().Evaluate([0, 1], entries);
        Assert.False(matching.HasFailures, matching.ToString());
        Assert.Empty(matching.StaleDivergences);

        // The same entry for this host still excuses the divergence
        // mutant: KnownDivergences.cs AppliesToThisHost `!= 0` -> `== 0`
        KnownDivergence[] thisHost = [entries[0] with { Hosts = otherHost ^ DiffHost.Any }];
        Assert.False(FakeCase(DiffPath.Native, CorruptSums).Evaluate([0], thisHost).HasFailures);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void KnownDivergence_PathThatThrew_IsNotStale()
    {
        // A path that threw produced no output, so it cannot show that the entry no longer differs: only the failure is reported
        // mutant: DifferentialRunner.cs AddStaleEntries `|| report.Failures.Any(f => f.Path == path.Path)` removed
        KnownDivergence[] entries = [new(FakeCaseName, "sum", DiffPath.Native, Issue: 999)];
        var report = FakeCase(DiffPath.Native, static _ => throw new InvalidOperationException("probe")).Evaluate([0], entries);

        Assert.Single(report.Failures);
        Assert.Empty(report.StaleDivergences);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void KnownDivergence_ShapePredicate_DoesNotReportStaleOnOtherShapes()
    {
        // A KnownDivergence restricted to 2D shapes (GridY > 1) must not be reported as StaleDivergence when only 1D shapes run
        // mutant: DifferentialRunner.cs AddStaleEntries `if (entry.ShapePredicate is not null && !report.ExecutedShapes.Any(s => entry.ShapePredicate(s))) continue;` removed
        KnownDivergence[] twoDimOnlyEntry =
        [
            new(FakeCaseName, "sum", DiffPath.Native, Issue: 690, ShapePredicate: static s => s.GridY > 1),
        ];

        var oneDimReport = FakeCase().Evaluate([0], twoDimOnlyEntry);
        Assert.False(oneDimReport.HasFailures, oneDimReport.ToString());
        Assert.Empty(oneDimReport.StaleDivergences);

        // When a matching 2D shape actually runs and does not diverge, the entry IS reported as stale
        var twoDimCase = FakeCase().Case with { Shapes = [LaunchShapes.TwoDimensional(5)] };
        var twoDimReport = DifferentialRunner.Evaluate(twoDimCase, [0], s_nativeOnly, twoDimOnlyEntry);
        Assert.True(twoDimReport.HasFailures);
        Assert.Single(twoDimReport.StaleDivergences);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void DifferentialRunner_StaleCheck_CanBeSkippedForReproducedSeeds()
    {
        // A reproduced single seed may lack the input that shows the entry, so the stale check is off for explicit seeds
        // mutant: DifferentialRunner.cs Evaluate `if (checkStale)` -> `if (true)`
        KnownDivergence[] entries = [new(FakeCaseName, "sum", DiffPath.Native, Issue: 999)];
        var fake = FakeCase();

        Assert.Single(fake.Evaluate([0], entries).StaleDivergences);
        var skipped = DifferentialRunner.Evaluate(fake.Case, [0], fake.Paths, entries, checkStale: false);
        Assert.Empty(skipped.StaleDivergences);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void DifferentialRunner_ThrowingPathCoveredByKnownDivergence_Passes()
    {
        // When Managed throws unexpectedly on an unchecked path, a KnownDivergence on $exception waives it
        var throwingCase = new DifferentialCase("HarnessTests.ThrowingManagedKernel")
        {
            Shape = LaunchShapes.SingleThread(),
            Inputs = static (_, _) => new DiffBuffers().Out<int>("r", 1, OutputPolicy.Exact),
            Launch = static (b, _) => b.Get<int>("r")[0] = 42,
            Reference = static (b, _) => b.Get<int>("r")[0] = 42,
        };

        DiffPathRunner[] paths =
        [
            new(DiffPath.Managed, static _ => throw new OverflowException("Arithmetic operation resulted in an overflow.")),
            new(DiffPath.Native, static run => run()),
        ];

        // Without KnownDivergence, the thrown exception causes failure
        var unexcusedReport = DifferentialRunner.Evaluate(throwingCase, [0], paths, s_noEntries);
        Assert.True(unexcusedReport.HasFailures);

        // With KnownDivergence covering $exception, the failure is waived
        KnownDivergence[] known =
        [
            new("HarnessTests.ThrowingManagedKernel", KnownDivergence.ExceptionColumn, DiffPath.Managed, Issue: 718),
        ];
        var excusedReport = DifferentialRunner.Evaluate(throwingCase, [0], paths, known);
        Assert.False(excusedReport.HasFailures, excusedReport.ToString());
        Assert.Empty(excusedReport.StaleDivergences);
    }

    // ---- Helpers ----

    private static void CorruptSums(int[] r)
    {
        for (int i = 0; i < r.Length; i += 2)
        {
            r[i]++;
        }
    }

    // r[t * 2] = a + b, r[t * 2 + 1] = a ^ b for 5 rows. The oracle is C# that reads the thread context, as a kernel
    // does; the launch computes on the host, then applies corrupt to r when it runs on one of the corrupted paths
    private static FakeRun FakeCase(
        DiffPath paths = DiffPath.Managed | DiffPath.Native,
        Action<int[]>? corrupt = null,
        DiffPath corrupted = DiffPath.Managed | DiffPath.Native | DiffPath.Cuda
    )
    {
        // Set by the path runner before each launch: whether this launch is on a corrupted path
        var corruptThisLaunch = new StrongBox<bool>();
        var runners = new[] { DiffPath.Managed, DiffPath.Native, DiffPath.Cuda }
            .Where(p => (paths & p) != 0)
            .Select(p => new DiffPathRunner(p, launch =>
            {
                corruptThisLaunch.Value = (corrupted & p) != 0;
                launch();
            }))
            .ToList();

        var testCase = new DifferentialCase(FakeCaseName)
        {
            Shape = s_fakeShape,
            Inputs = static (rng, rows) =>
            {
                var (a, b) = DiffValues.Int32Pairs(rng, rows);
                return new DiffBuffers().In("a", a).In("b", b).Out<int>("r", rows, s_fakeColumns, OutputPolicy.Exact);
            },
            Launch = (buffers, shape) =>
            {
                var a = buffers.Get<int>("a");
                var b = buffers.Get<int>("b");
                var r = buffers.Get<int>("r");
                for (int t = 0; t < shape.Rows; t++)
                {
                    r[t * 2] = unchecked(a[t] + b[t]);
                    r[(t * 2) + 1] = a[t] ^ b[t];
                }

                if (corruptThisLaunch.Value)
                {
                    corrupt?.Invoke(r);
                }
            },
            Oracle = static (buffers, _) =>
            {
                var a = buffers.Get<int>("a");
                var b = buffers.Get<int>("b");
                var r = buffers.Get<int>("r");
                int blockId = BlockIndex.X + (BlockIndex.Y * GridDimension.X);
                int threadInBlock = ThreadIndex.X + (ThreadIndex.Y * BlockDimension.X);
                int t = (blockId * BlockDimension.X * BlockDimension.Y) + threadInBlock;
                if (t >= a.Length)
                {
                    return;
                }

                r[t * 2] = unchecked(a[t] + b[t]);
                r[(t * 2) + 1] = a[t] ^ b[t];
            },
        };
        return new FakeRun(testCase, runners);
    }

    // A fake case with the host paths that run it
    private sealed record FakeRun(DifferentialCase Case, IReadOnlyList<DiffPathRunner> Paths)
    {
        public DifferentialReport Evaluate(IReadOnlyList<int> seeds, IReadOnlyList<KnownDivergence>? entries = null) =>
            DifferentialRunner.Evaluate(Case, seeds, Paths, entries ?? []);
    }
}
