#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public class DifferentialRunnerTests
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
        Oracle = static (b, _) => HarnessProbeKernels.UInt32AddMatrixKernel(b.Get<uint>("a"), b.Get<uint>("b"), b.Get<uint>("r")),
    };

    // ---- The runner ----

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void DifferentialRunner_CorrectPaths_ReportsNothing()
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
    public void DifferentialRunner_OneFlippedBit_IsReportedWithItsCell()
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

        // The message names the seed, the cell, the inputs and the values with their bits
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
    public void DifferentialRunner_ManyMismatches_AreAllCountedAndFiveShownPerSeed()
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
    public void DifferentialRunner_ThrowingPath_ReportsDivergence()
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
    public void DifferentialRunner_OracleOrderCheck_StopsARacingKernel()
    {
        // LastWriterKernel's result is the id of the thread that runs last: forward and reverse orders disagree, so the
        // oracle has no single answer and the harness stops before any path runs
        // mutant: DifferentialRunner.cs RunOracle `new[] { ThreadOrder.Reverse, ThreadOrder.Permutation(seed) }` -> `new[] { ThreadOrder.Forward }`
        var racing = new DifferentialCase("HarnessProbeKernels.LastWriterKernel")
        {
            Shape = LaunchShape.ForRows(8, blockX: 4),
            Inputs = static (_, _) => new DiffBuffers().Out<int>("r", 1, OutputPolicy.Exact),
            Launch = static (_, _) => throw new InvalidOperationException("no path may run after the order check fails"),
            Oracle = static (b, _) => HarnessProbeKernels.LastWriterKernel(b.Get<int>("r")),
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
    [Scenario(TestScenario.Boundary)]
    public void DifferentialRunner_OracleOrderCheck_AcceptsAnyNaNOnlyForComputedCells()
    {
        // The oracle writes a NaN with a new payload on every call, as the JIT does when it quiets a signaling NaN in one
        // run and not in the other: a computed cell (Bitwise) accepts it, a moved cell (Exact) must keep every bit
        // mutant: DifferentialRunner.cs CheckOrderIndependence `|| (... Bitwise && CellsMatch)` removed
        var computed = new DifferentialCase("HarnessTests.NaNPayloadPerCall")
        {
            Shape = LaunchShape.ForRows(4, blockX: 4),
            Inputs = static (_, _) => new DiffBuffers().Out<float>("r", 1, OutputPolicy.Bitwise),
            Launch = static (b, _) => b.Get<float>("r")[0] = float.NaN,
            Oracle = static (b, _) => b.Get<float>("r")[0] = NextPayloadNaN(),
        };
        Assert.False(DifferentialRunner.Evaluate(computed, [0], s_nativeOnly, s_noEntries).HasFailures);

        // mutant: DiffBuffers.cs CellsMatch lets an Exact cell match any NaN, as it does for Bitwise
        var moved = new DifferentialCase("HarnessTests.NaNPayloadPerCall")
        {
            Shape = computed.Shape,
            Inputs = static (_, _) => new DiffBuffers().Out<float>("r", 1, OutputPolicy.Exact),
            Launch = static (_, _) => throw new InvalidOperationException("no path may run after the order check fails"),
            Oracle = computed.Oracle,
        };
        var ex = Assert.Throws<DifferentialHarnessException>(() => DifferentialRunner.Evaluate(moved, [0], s_nativeOnly, s_noEntries));
        Assert.Contains("kernel output depends on thread order", ex.Message);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void DifferentialRunner_MalformedCase_ReportsHarnessError()
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
            Oracle = static (_, _) => throw new DivideByZeroException(),
        };
        var ex = Assert.Throws<DifferentialHarnessException>(() => DifferentialRunner.Evaluate(throwing, [4], s_nativeOnly, s_noEntries));
        Assert.Contains("threw for seed 4", ex.Message);
        Assert.IsType<DivideByZeroException>(ex.InnerException);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void DifferentialRunner_ReferenceModel_ReplacesTheOracle()
    {
        // A reference model runs once, without a thread context, and its output is the expected one
        // mutant: DifferentialRunner.cs RunOracle `reference(expected, shape)` -> `reference(initial.Clone(), shape)`
        var withReference = new DifferentialCase(FakeCaseName)
        {
            Shape = s_fakeShape,
            Inputs = static (_, _) => new DiffBuffers().Out<int>("r", 2, OutputPolicy.Exact),
            Launch = static (b, _) => b.Get<int>("r")[1] = 42,
            Reference = static (b, _) => b.Get<int>("r")[1] = 42,
        };

        var report = DifferentialRunner.Evaluate(withReference, [0], s_nativeOnly, s_noEntries);
        Assert.False(report.HasFailures, report.ToString());
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void DifferentialRunner_NativePathWithoutLibrary_ReportsHarnessError()
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

    // ---- A real kernel on the paths of this process ----

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void DifferentialRunner_UInt32AddKernel_MatchesTheOracleOnEveryPath()
    {
        // Tests.Cpu compares the managed and the native path, Tests.Cuda the CUDA path, with the oracle over seeds 0 to 8
        // (seed 0: the boundary cross product, 441 rows in 7 blocks of 64)
        // mutant: HarnessTests.cs s_uint32Add Launch `gridDimX: s.GridX` -> `gridDimX: s.GridX - 1` (the last 57 rows stay 0)
        // On Tests.Cpu the native path's preflight finds the real library (the name the launcher loads)
        // mutant: DiffPaths.cs NativeLibraryPath `Kassyi.GSharp.Kernels_` -> `Kassyi.GSharp.Kernel_` (native library not found)
        Assert.NotEmpty(DiffPaths.Available);
        DifferentialRunner.Run(s_uint32Add);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public unsafe void LaunchPtr_DeviceExecution_ExecutesAndMatchesManagedLaunchOnCuda()
    {
        // LaunchPtr uploads buffers via DiffPointerScope, runs the pointer kernel, and copies device/staging memory back
        // to the host buffers on Dispose so the cells match Launch and the oracle
        // mutant: DiffPointerScope.cs Dispose skips `deviceBuffer.CopyToHost(writable)` / `CopyTo(writable)`
#if GSHARP_CUDA
        CudaCapability.EnsureGpuAvailable();
        var cudaPtrCase = new DifferentialCase("HarnessProbeKernels.LastWriterKernel")
        {
            Shape = LaunchShapes.SingleThread(),
            Inputs = static (_, _) => new DiffBuffers().RefScalar("r", -99, OutputPolicy.Exact),
            Launch = static (b, s) => HarnessProbeKernels.LaunchLastWriterKernel(
                b.Get<int>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            LaunchPtr = static (b, s) =>
            {
                using var scope = b.PinOrUpload();
                HarnessProbeKernels.LaunchLastWriterKernelPtr(
                    scope.GetArray("r"),
                    gridDimX: s.GridX,
                    blockDimX: s.BlockX
                );
            },
            Oracle = static (b, _) => HarnessProbeKernels.LastWriterKernel(b.Get<int>("r")),
        };

        var cudaReport = DifferentialRunner.Evaluate(cudaPtrCase, [0], DiffPaths.Available, s_noEntries);
        Assert.False(cudaReport.HasFailures, cudaReport.ToString());
#else
        var hostSimCase = new DifferentialCase("HarnessTests.FakePointerKernel")
        {
            Shape = LaunchShapes.SingleThread(),
            Inputs = static (_, _) => new DiffBuffers().RefScalar("r", -99, OutputPolicy.Exact),
            Launch = static (b, _) => b.SetScalar("r", 0),
            LaunchPtr = static (b, _) =>
            {
                using var scope = b.PinOrUpload();
                int* ptr = (int*)scope.Get("r");
                ptr[0] = 0;
            },
            Oracle = static (b, _) => HarnessProbeKernels.LastWriterKernel(b.Get<int>("r")),
        };

        var simReport = DifferentialRunner.Evaluate(
            hostSimCase,
            [0],
            [new DiffPathRunner(DiffPath.Cuda, DiffPaths.RunDirect)],
            s_noEntries
        );
        Assert.False(simReport.HasFailures, simReport.ToString());
#endif
    }

    // ---- Helpers ----

    private static int s_nanPayload;

    private static float NextPayloadNaN() =>
        BitConverter.UInt32BitsToSingle(0x7FC0_0000u | (uint)(Interlocked.Increment(ref s_nanPayload) & 0x3F_FFFF));

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
