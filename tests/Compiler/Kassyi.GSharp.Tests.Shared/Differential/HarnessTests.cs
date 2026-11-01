#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Tests of the differential harness itself (design §4.5, §7 PR 1). Each check names, above it, the one-line change of
/// the harness (mutant) that makes it fail, as property-test-design §5 asks. The unit tests run fake paths that compute
/// on the host, so they need neither a GPU nor a native build; the last test runs a real kernel on the paths of the
/// process.
/// </summary>
public class HarnessTests
{
    private const string FakeCaseName = "HarnessTests.FakeAddKernel";

    private static readonly string[] s_fakeColumns = ["sum", "xor"];

    // A host "path": the launch runs as it is, on the calling thread
    private static readonly IReadOnlyList<DiffPathRunner> s_nativeOnly = [new(DiffPath.Native, DiffPaths.RunDirect)];

    private static readonly IReadOnlyList<KnownDivergence> s_noEntries = [];

    // 5 rows in 3 blocks of 2: the last thread has no row and must return without writing
    private static readonly LaunchShape s_fakeShape = LaunchShape.ForRows(5, blockX: 2);

    private static readonly DifferentialCase s_uint32Add = new("HarnessProbeKernels.UInt32AddMatrixKernel")
    {
        // Seed 0: the 441 rows of the boundary cross product, 7 blocks of 64 (the last block is partly idle)
        Shape = LaunchShape.ForRows(DiffValues.Int32Boundaries.Length * DiffValues.Int32Boundaries.Length, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (a, b) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("a", Array.ConvertAll(a, static v => unchecked((uint)v)))
                .In("b", Array.ConvertAll(b, static v => unchecked((uint)v)))
                .Out<uint>("r", rows, HarnessProbeKernels.UInt32AddColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => HarnessProbeKernels.LaunchUInt32AddMatrixKernel(
            b.Get<uint>("a"),
            b.Get<uint>("b"),
            b.Get<uint>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => HarnessProbeKernels.UInt32AddMatrixKernel(b.Get<uint>("a"), b.Get<uint>("b"), b.Get<uint>("r")),
    };

    // ---- DiffRandom ----

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DiffRandom_SplitMix64_ProducesThePublishedSequence()
    {
        // The reference outputs of SplitMix64 (Vigna's splitmix64.c) for seeds 0 and 1234567, so a seed printed by a
        // failure reproduces on any machine and in any port of the generator
        // mutant: DiffRandom.cs Mix `z ^ (z >> 30)` -> `z ^ (z >> 31)`
        var zero = new DiffRandom(0);
        Assert.Equal(0xE220A8397B1DCDAFul, zero.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4ul, zero.NextUInt64());
        Assert.Equal(0x06C45D188009454Ful, zero.NextUInt64());

        var other = new DiffRandom(1234567);
        Assert.Equal(6457827717110365317ul, other.NextUInt64());
        Assert.Equal(3203168211198807973ul, other.NextUInt64());
        Assert.Equal(9817491932198370423ul, other.NextUInt64());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DiffRandom_NextBelow_StaysInRange()
    {
        // NextBelow(n) is in [0, n) and reaches both ends for a small n
        // mutant: DiffRandom.cs NextBelow `>> 32` -> `>> 31`
        var rng = new DiffRandom(7);
        var seen = new bool[3];
        for (int i = 0; i < 1000; i++)
        {
            int v = rng.NextBelow(3);
            Assert.InRange(v, 0, 2);
            seen[v] = true;
        }

        Assert.All(seen, Assert.True);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextBelow(0));
    }

    // ---- DiffValues ----

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DiffValues_SeedZero_IsTheBoundaryCrossProduct()
    {
        int n = DiffValues.Int32Boundaries.Length;
        var (left, right) = DiffValues.Int32Pairs(new DiffRandom(0), n * n);

        // Fixed rows: row r is (B[r / n], B[r % n]), so a failure's row number names the same inputs in every run
        // mutant: DiffValues.cs Pairs `boundaries[r / n % n]` <-> `boundaries[r % n]` (left and right swapped)
        Assert.Equal((0, 0), (left[0], right[0]));
        Assert.Equal((0, 1), (left[1], right[1]));
        Assert.Equal((1, 0), (left[n], right[n]));
        Assert.Equal((int.MaxValue, int.MinValue), (left[5 * n + 6], right[5 * n + 6]));
        Assert.Equal((65, 65), (left[n * n - 1], right[n * n - 1]));

        // Every pair of boundaries appears exactly once
        // mutant: DiffValues.cs Pairs `boundaries[r / n % n]` -> `boundaries[r % n]`
        Assert.Equal(n * n, left.Zip(right).Distinct().Count());

        // Single values repeat the table in order
        Assert.Equal(DiffValues.SingleBoundaries.Select(BitConverter.SingleToUInt32Bits),
            DiffValues.Single(new DiffRandom(0), DiffValues.SingleBoundaries.Length).Select(BitConverter.SingleToUInt32Bits));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DiffValues_RandomSeeds_AreDeterministicAndMixBoundariesWithRandomBits()
    {
        // The same seed gives the same values; another seed gives other values
        // mutant: DiffValues.cs Values `rng.IsBoundarySeed ?` -> `true ?` (every seed returns the boundary table)
        var first = DiffValues.Int32(new DiffRandom(3), 400);
        Assert.Equal(first, DiffValues.Int32(new DiffRandom(3), 400));
        Assert.NotEqual(first, DiffValues.Int32(new DiffRandom(4), 400));

        // Both boundaries and values outside the table occur (each with probability about 1/2)
        // mutant: DiffValues.cs NextInt32 `rng.NextBool() ?` -> `false ?`
        var boundaries = DiffValues.Int32Boundaries.ToHashSet();
        Assert.InRange(first.Count(boundaries.Contains), 150, 250);

        var singles = DiffValues.Single(new DiffRandom(3), 400);
        Assert.Equal(singles.Select(BitConverter.SingleToUInt32Bits), DiffValues.Single(new DiffRandom(3), 400).Select(BitConverter.SingleToUInt32Bits));
        Assert.Contains(singles, static v => !DiffValues.SingleBoundaries.Contains(v));
    }

    // ---- LaunchShape and ThreadOrder ----

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Test_LaunchShape_ContextOf_NumbersThreadsLikeTheManagedLauncher()
    {
        // ForRows rounds the grid up: 441 rows in blocks of 64 take 7 blocks
        // mutant: LaunchShape.cs ForRows `+ blockX - 1` removed
        var rows = LaunchShape.ForRows(441, blockX: 64);
        Assert.Equal((7u, 448), (rows.GridX, rows.ThreadCount));

        // x fastest inside the block, then y, then z; blocks in the same order
        // mutant: LaunchShape.cs ContextOf `thread % bx, thread / bx % by` -> `thread / by % bx, thread % by`
        var shape = new LaunchShape(GridX: 2, GridY: 2, GridZ: 2, BlockX: 4, BlockY: 2, BlockZ: 2, Rows: 128);
        var context = shape.ContextOf((5 * 16) + (1 * 8) + (1 * 4) + 3);
        Assert.Equal(new GpuThreadContext(3, 1, 1, 1, 0, 1, 4, 2, 2, 2, 2, 2), context);
        Assert.Equal(new GpuThreadContext(2, 0, 1, 1, 0, 1, 4, 2, 2, 2, 2, 2), shape.ContextOf((5 * 16) + (1 * 8) + (0 * 4) + 2));
        Assert.Equal(new GpuThreadContext(0, 0, 0, 0, 0, 0, 4, 2, 2, 2, 2, 2), shape.ContextOf(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => shape.ContextOf(128));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ThreadOrder_EnumeratesEveryThreadOnce()
    {
        var shape = LaunchShape.ForRows(64, blockX: 16);
        int[] forward = Enumerable.Range(0, 64).ToArray();

        Assert.Equal(forward, ThreadOrder.Forward.Enumerate(shape));
        Assert.Equal(forward.Reverse(), ThreadOrder.Reverse.Enumerate(shape));

        // A permutation: every id once, not the forward order, and the same for the same seed
        // mutant: ThreadOrder.cs Enumerate `int j = rng.NextBelow(i + 1);` -> `int j = i;`
        var permuted = ThreadOrder.Permutation(5).Enumerate(shape);
        Assert.Equal(forward, permuted.Order());
        Assert.NotEqual(forward, permuted);
        Assert.Equal(permuted, ThreadOrder.Permutation(5).Enumerate(shape));
    }

    // ---- Comparison policies ----

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0x3F800000u, 0x3F800000u, true, true)] // 1f, 1f
    [InlineData(0x3F800000u, 0x3F800001u, false, false)] // 1f, the next float: the last mantissa bit
    [InlineData(0x00000000u, 0x80000000u, false, false)] // 0f, -0f: the sign bit (== would call them equal)
    [InlineData(0x7FC00000u, 0x7FC00000u, true, true)] // the same NaN
    [InlineData(0x7FC00000u, 0xFFC00001u, true, false)] // NaN, a negative NaN with a payload
    [InlineData(0x7FC00000u, 0x7F800000u, false, false)] // NaN, +infinity
    [InlineData(0x7FC00000u, 0x3F800000u, false, false)] // NaN, 1f
    public void Test_Policies_CompareSingleBits(uint left, uint right, bool bitwiseMatch, bool exactMatch)
    {
        // Bitwise: one differing bit is a mismatch, but any NaN matches any NaN (.NET passes a NaN through, PTX returns
        // the canonical one). Exact: every bit, NaN payloads included
        // mutant: DiffBuffers.cs CellsMatch `|| (IsNaN(...) && IsNaN(...))` removed (the NaN rows fail)
        // mutant: DiffBuffers.cs BitsEqual compared through `x.Equals(y)` (0f and -0f match, NaN never does)
        Assert.Equal(bitwiseMatch, CellsMatch(BitConverter.UInt32BitsToSingle(left), BitConverter.UInt32BitsToSingle(right), OutputPolicy.Bitwise));
        Assert.Equal(exactMatch, CellsMatch(BitConverter.UInt32BitsToSingle(left), BitConverter.UInt32BitsToSingle(right), OutputPolicy.Exact));
        Assert.True(CellsMatch(BitConverter.UInt32BitsToSingle(left), BitConverter.UInt32BitsToSingle(right), OutputPolicy.Ignore));
    }

    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0x3FF0000000000000ul, 0x3FF0000000000001ul, false)] // 1.0, the next double
    [InlineData(0x0000000000000000ul, 0x8000000000000000ul, false)] // 0.0, -0.0
    [InlineData(0x7FF8000000000000ul, 0xFFF8000000000001ul, true)] // two NaNs
    [InlineData(0x7FF8000000000000ul, 0x7FF0000000000000ul, false)] // NaN, +infinity
    public void Test_Policies_CompareDoubleBits(ulong left, ulong right, bool bitwiseMatch)
    {
        // As for float, on 64 bits
        // mutant: DiffBuffers.cs IsNaN `double d => double.IsNaN(d)` removed
        Assert.Equal(bitwiseMatch, CellsMatch(BitConverter.UInt64BitsToDouble(left), BitConverter.UInt64BitsToDouble(right), OutputPolicy.Bitwise));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Test_Policies_CompareEveryBitOfIntegers()
    {
        // The lowest and the highest bit of each width are mismatches
        // mutant: DiffBuffers.cs BitsEqual compares only the first byte (`.Slice(0, 1)` on both spans)
        Assert.False(CellsMatch(0, 1, OutputPolicy.Exact));
        Assert.False(CellsMatch(0, int.MinValue, OutputPolicy.Exact));
        Assert.False(CellsMatch(0L, long.MinValue, OutputPolicy.Bitwise));
        Assert.False(CellsMatch((byte)0, (byte)0x80, OutputPolicy.Exact));
        Assert.True(CellsMatch(-1L, -1L, OutputPolicy.Exact));
    }

    // ---- The runner ----

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Runner_CorrectPaths_ReportNothing()
    {
        // A launch that computes what the oracle computes passes every seed
        // mutant: DifferentialRunner.cs Compare `if (actuals.All(...))` -> `if (!actuals.All(...))`
        var report = FakeCase().Evaluate(DifferentialSeeds.Fixed);

        Assert.False(report.HasFailures, report.ToString());
        Assert.Empty(report.Mismatches);
        Assert.Empty(report.Failures);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Test_Runner_OneFlippedBit_IsReportedWithItsCell()
    {
        // One flipped bit in row 3, column "xor", on the native path only: exactly that cell is reported, per seed, with
        // the value of every path
        // mutant: DifferentialRunner.cs Compare `oracle.ColumnOf(i)` -> `oracle.RowOf(i)`
        var report = FakeCase(corrupt: static r => r[(3 * 2) + 1] ^= 1, corrupted: DiffPath.Native).Evaluate([0, 5]);

        Assert.True(report.HasFailures);
        Assert.Equal(2, report.Mismatches.Count);
        var cell = report.Mismatches[0];
        Assert.Equal((0, "r", 7, 3, 1, "xor"), (cell.Seed, cell.Buffer, cell.Index, cell.Row, cell.Column, cell.ColumnName));
        Assert.Equal([false, true], cell.Paths.Select(static p => p.Differs));
        Assert.Equal(5, report.Mismatches[1].Seed);

        // The message names the seed, the cell, the inputs and the values with their bits (design §4.4)
        // mutant: DifferentialReport.cs ToString `if (cell.Inputs.Length > 0)` -> `if (false)`
        string message = report.ToString();
        Assert.Contains("Differential mismatch in HarnessTests.FakeAddKernel (oracle: sequential C#)", message);
        Assert.Contains("seed 0 (boundary tables), 1 mismatching cell; first:", message);
        Assert.Contains("r[7] thread 3, column 1 \"xor\"", message);
        Assert.Contains("inputs: a=0 (0x00000000)  b=2 (0x00000002)", message);
        Assert.Contains("oracle : 2 (0x00000002)", message);
        Assert.Contains("native : 3 (0x00000003)    <- differs", message);
        Assert.Contains("column summary: \"xor\" 2 cells (native)", message);
        Assert.Contains("reproduce: DifferentialRunner.Run(HarnessTests.FakeAddKernel, seeds: [0, 5])", message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Test_Runner_ManyMismatches_AreAllCountedAndFiveShownPerSeed()
    {
        // Every "sum" cell is wrong: all 5 rows are counted, and the message prints the first 5 cells of the seed
        // mutant: DifferentialReport.cs `cells.Take(CellsShownPerSeed)` -> `cells.Take(1)`
        var report = FakeCase(DiffPath.Native, CorruptSums).Evaluate([1]);

        Assert.Equal(5, report.UnexpectedMismatches.Count);
        Assert.Equal(5, Regex.Matches(report.ToString(), "<- differs").Count);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Test_Runner_ThrowingPath_IsADivergence()
    {
        // A path that throws (as #711 and #728 threw GpuOnlyException) fails the case and is named in the message
        // mutant: DifferentialRunner.cs Evaluate `report.Add(new PathFailure(...))` removed
        var report = FakeCase(DiffPath.Native, static _ => throw new InvalidOperationException("probe")).Evaluate([0]);

        Assert.True(report.HasFailures);
        var failure = Assert.Single(report.Failures);
        Assert.Equal(DiffPath.Native, failure.Path);
        Assert.Contains("native threw System.InvalidOperationException: probe", report.ToString());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Test_Runner_OracleOrderCheck_StopsARacingKernel()
    {
        // LastWriterKernel's result is the id of the thread that runs last: forward and reverse orders disagree, so the
        // oracle has no single answer and the harness stops before any path runs
        // mutant: DifferentialRunner.cs RunOracle `new[] { ThreadOrder.Reverse, ThreadOrder.Permutation(seed) }` -> `new[] { ThreadOrder.Forward }`
        var racing = new DifferentialCase("HarnessProbeKernels.LastWriterKernel")
        {
            Shape = LaunchShape.ForRows(8, blockX: 4),
            Inputs = static (_, _) => new DiffBuffers().Out<int>("r", 1, OutputPolicy.Exact),
            Launch = static (_, _) => throw new InvalidOperationException("no path may run after the order check fails"),
            Oracle = static b => HarnessProbeKernels.LastWriterKernel(b.Get<int>("r")),
        };

        var ex = Assert.Throws<DifferentialHarnessException>(() => DifferentialRunner.Evaluate(racing, [0], s_nativeOnly, s_noEntries));
        Assert.Contains("kernel output depends on thread order", ex.Message);
        Assert.Contains("r[0] column \"r\" is 7 (0x00000007) in forward order and 0 (0x00000000) in reverse order", ex.Message);

        // An Ignore buffer is left out of the order check (the old values of atomics)
        // mutant: DifferentialRunner.cs CheckOrderIndependence `continue` for Ignore removed
        var ignored = new DifferentialCase("HarnessProbeKernels.LastWriterKernel")
        {
            Shape = racing.Shape,
            Inputs = static (_, _) => new DiffBuffers().Out<int>("r", 1, OutputPolicy.Ignore),
            Launch = static (_, _) => { },
            Oracle = racing.Oracle,
        };
        Assert.False(DifferentialRunner.Evaluate(ignored, [0], s_nativeOnly, s_noEntries).HasFailures);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Test_Runner_MalformedCase_IsAHarnessError()
    {
        // Neither or both of Oracle and Reference: the case cannot be judged
        var neither = new DifferentialCase(FakeCaseName)
        {
            Shape = s_fakeShape,
            Inputs = static (_, _) => new DiffBuffers(),
            Launch = static (_, _) => { },
        };
        Assert.Throws<DifferentialHarnessException>(() => DifferentialRunner.Evaluate(neither, [0], s_nativeOnly, s_noEntries));

        // An oracle that throws is reported with its seed, not as a path divergence
        var throwing = new DifferentialCase(FakeCaseName)
        {
            Shape = s_fakeShape,
            Inputs = static (_, _) => new DiffBuffers(),
            Launch = static (_, _) => { },
            Oracle = static _ => throw new DivideByZeroException(),
        };
        var ex = Assert.Throws<DifferentialHarnessException>(() => DifferentialRunner.Evaluate(throwing, [4], s_nativeOnly, s_noEntries));
        Assert.Contains("threw for seed 4", ex.Message);
        Assert.IsType<DivideByZeroException>(ex.InnerException);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Runner_ReferenceModel_ReplacesTheOracle()
    {
        // A reference model runs once, without a thread context, and its output is the expected one
        // mutant: DifferentialRunner.cs RunOracle `reference(expected)` -> `reference(initial.Clone())`
        var withReference = new DifferentialCase(FakeCaseName)
        {
            Shape = s_fakeShape,
            Inputs = static (_, _) => new DiffBuffers().Out<int>("r", 2, OutputPolicy.Exact),
            Launch = static (b, _) => b.Get<int>("r")[1] = 42,
            Reference = static b => b.Get<int>("r")[1] = 42,
        };

        var report = DifferentialRunner.Evaluate(withReference, [0], s_nativeOnly, s_noEntries);
        Assert.False(report.HasFailures, report.ToString());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Test_Runner_NativePathWithoutLibrary_IsAHarnessError()
    {
        // Without its library the launcher silently runs the managed fallback, so a "native" path would compare managed
        // with the oracle twice and miss every native divergence: the runner stops before any launch instead
        // mutant: DiffPaths.cs EnsureNativeLibrary `if (!CpuKernelLoader.ShouldUseManagedExecution(dllPath))` -> `if (CpuKernelLoader.ShouldUseManagedExecution(dllPath))`
        // mutant: DifferentialRunner.cs Evaluate `path.Preflight?.Invoke();` removed
        string missing = Path.Combine(AppContext.BaseDirectory, "Kassyi.GSharp.Kernels_HarnessTests_Missing_x64.dll");
        var fake = FakeCase(DiffPath.Native, static _ => throw new InvalidOperationException("no launch may run"));

        var ex = Assert.Throws<DifferentialHarnessException>(
            () => DifferentialRunner.Evaluate(fake.Case, [0], [DiffPaths.Native(missing)], s_noEntries)
        );
        Assert.Equal($"native library not found: {missing}; the Native path would run managed", ex.Message);
    }

    // ---- Known divergences ----

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Test_KnownDivergence_ThatStillDiffers_Passes()
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
    public void Test_KnownDivergence_ThatNoLongerDiffers_Fails()
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
    public void Test_KnownDivergence_CoversOnlyItsCaseColumnAndPaths()
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
    public void Test_KnownDivergence_ForAnotherHost_IsIgnored()
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
    public void Test_KnownDivergence_OnAPathThatThrew_IsNotStale()
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
    public void Test_Runner_StaleCheck_CanBeSkippedForReproducedSeeds()
    {
        // A reproduced single seed may lack the input that shows the entry, so the stale check is off for explicit seeds
        // mutant: DifferentialRunner.cs Evaluate `if (checkStale)` -> `if (true)`
        KnownDivergence[] entries = [new(FakeCaseName, "sum", DiffPath.Native, Issue: 999)];
        var fake = FakeCase();

        Assert.Single(fake.Evaluate([0], entries).StaleDivergences);
        var skipped = DifferentialRunner.Evaluate(fake.Case, [0], fake.Paths, entries, checkStale: false);
        Assert.Empty(skipped.StaleDivergences);
    }

    // ---- Seeds ----

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Seeds_TestSeedAddsOneDerivedSeed()
    {
        // Without TEST_SEED the seeds are 0 to 8 (no environment dependence); with it, one more seed of at least 9
        // that the same TEST_SEED always gives
        // mutant: DifferentialSeeds.cs For `[.. Fixed, derived]` -> `Fixed`
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8], DifferentialSeeds.For(null));
        Assert.Equal(DifferentialSeeds.Fixed, DifferentialSeeds.For(" "));

        var withSeed = DifferentialSeeds.For("42");
        Assert.Equal(10, withSeed.Count);
        Assert.Equal(DifferentialSeeds.Fixed, withSeed.Take(9));
        Assert.True(withSeed[9] >= DifferentialSeeds.FirstDerivedSeed);
        Assert.Equal(withSeed, DifferentialSeeds.For("42"));

        // Consecutive TEST_SEED values (test-stability iterations) give different seeds; a non-integer is hashed stably
        // mutant: DifferentialSeeds.cs Derive `DiffRandom.Mix(value) % Range` -> `0`
        Assert.NotEqual(DifferentialSeeds.Derive("42"), DifferentialSeeds.Derive("43"));
        Assert.Equal(DifferentialSeeds.Derive("random"), DifferentialSeeds.Derive("random"));
        Assert.True(DifferentialSeeds.Derive("-1") >= DifferentialSeeds.FirstDerivedSeed);
    }

    // ---- A real kernel on the paths of this process ----

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_UInt32AddKernel_MatchesTheOracleOnEveryPath()
    {
        // Tests.Cpu compares the managed and the native path, Tests.Cuda the CUDA path, with the oracle over seeds 0 to 8
        // (seed 0: the boundary cross product, 441 rows in 7 blocks of 64)
        // mutant: HarnessTests.cs s_uint32Add Launch `gridDimX: s.GridX` -> `gridDimX: s.GridX - 1` (the last 57 rows stay 0)
        // On Tests.Cpu the native path's preflight finds the real library (the name the launcher loads)
        // mutant: DiffPaths.cs NativeLibraryPath `Kassyi.GSharp.Kernels_` -> `Kassyi.GSharp.Kernel_` (native library not found)
        Assert.NotEmpty(DiffPaths.Available);
        DifferentialRunner.Run(s_uint32Add);
    }

    // ---- Helpers ----

    private static void CorruptSums(int[] r)
    {
        for (int i = 0; i < r.Length; i += 2)
        {
            r[i]++;
        }
    }

    private static bool CellsMatch<T>(T left, T right, OutputPolicy policy)
        where T : unmanaged
    {
        var a = new DiffBuffers().Out<T>("x", 1, policy);
        var b = a.Clone();
        a.Get<T>("x")[0] = left;
        b.Get<T>("x")[0] = right;
        return a.Find("x").CellsMatch(b.Find("x"), 0);
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
            Oracle = static buffers =>
            {
                var a = buffers.Get<int>("a");
                var b = buffers.Get<int>("b");
                var r = buffers.Get<int>("r");
                int t = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
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
