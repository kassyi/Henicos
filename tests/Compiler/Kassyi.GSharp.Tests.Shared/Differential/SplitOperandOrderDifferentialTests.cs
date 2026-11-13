#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kassyi.GSharp.Core;
using Xunit;
using K = Kassyi.GSharp.Tests.Shared.Differential.SplitOperandOrderDifferentialKernels;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Operands that C# evaluates before a split point (#991, #1535, #1256), and the locations it checks before it, on the
/// CPU paths and CUDA. The reference is the C# of each lane with every split replaced by what it returns: a shuffle
/// that reads the calling lane returns its own value, a barrier that every thread reaches changes nothing.
/// </summary>
/// <remarks>
/// The CPU paths still evaluate some of these operands after the split (#991): those columns are known divergences.
/// No test states the value they give, only that it is not the C# value yet.
/// </remarks>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class SplitOperandOrderDifferentialTests
{
    private const int Rows = 32;

    internal static readonly DifferentialCase s_operandOrder =
        new($"{nameof(SplitOperandOrderDifferentialKernels)}.{nameof(K.OperandOrderKernel)}")
        {
            Shape = LaunchShape.ForRows(Rows, blockX: Rows),
            Seeds = [0, 1],
            Inputs = static (rng, shape) => new DiffBuffers()
                .In("a", Small(rng, shape.Rows, 1))
                .In("b", Small(rng, shape.Rows, 2))
                .In("c", Small(rng, shape.Rows, 3))
                .In("d", Small(rng, shape.Rows, 4))
                .In("e", Small(rng, shape.Rows, 5))
                .In("f", new int[shape.Rows])
                .In("g", Small(rng, shape.Rows, 6))
                .In("h", Small(rng, shape.Rows, 7))
                .In("counter", new int[shape.Rows])
                .In("cells", new SplitOrderCell[shape.Rows])
                .In("flag", new[] { rng.Seed == 0 })
                .Out<int>("output", shape.Rows, K.OrderColumns, OutputPolicy.Exact),
            Launch = static (b, s) => K.LaunchOperandOrderKernel(
                b.Get<int>("a"),
                b.Get<int>("b"),
                b.Get<int>("c"),
                b.Get<int>("d"),
                b.Get<int>("e"),
                b.Get<int>("f"),
                b.Get<int>("g"),
                b.Get<int>("h"),
                b.Get<int>("counter"),
                b.Get<SplitOrderCell>("cells"),
                b.Get<bool>("flag")[0],
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, _) => OperandOrder(b),
        };

    internal static readonly DifferentialCase s_compoundOrder =
        new($"{nameof(SplitOperandOrderDifferentialKernels)}.{nameof(K.CompoundOrderKernel)}")
        {
            Shape = LaunchShape.ForRows(Rows, blockX: Rows),
            Seeds = [0, 1],
            Inputs = static (rng, shape) => new DiffBuffers()
                .In("a", Small(rng, shape.Rows, 11))
                .In("b", Small(rng, shape.Rows, 12))
                .In("c", Small(rng, shape.Rows, 13))
                .In("d", Small(rng, shape.Rows, 14))
                .In("e", Small(rng, shape.Rows, 15))
                .In("xy", new[] { rng.Seed + 3, rng.Seed + 9 })
                .Out<int>("output", shape.Rows, K.CompoundColumns, OutputPolicy.Exact),
            Launch = static (b, s) => K.LaunchCompoundOrderKernel(
                b.Get<int>("a"),
                b.Get<int>("b"),
                b.Get<int>("c"),
                b.Get<int>("d"),
                b.Get<int>("e"),
                b.Get<int>("xy")[0],
                b.Get<int>("xy")[1],
                b.Get<int>("output"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = static (b, _) => CompoundOrder(b),
        };

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void OperandOrder_OperandsBeforeASplit_AreEvaluatedInTheOrderWritten() =>
        DifferentialRunner.Run(s_operandOrder);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void CompoundOrder_TargetReadBeforeASplitInTheValue_KeepsTheValueItRead() =>
        DifferentialRunner.Run(s_compoundOrder);

    // Small values, so that no sum overflows
    private static int[] Small(DiffRandom rng, int rows, int salt) =>
        Enumerable.Range(0, rows).Select(i => (rng.Seed * 31 + i * 7 + salt) % 1000).ToArray();

    // The C# of OperandOrderKernel, lane by lane, with SyncThen, SyncSum, SyncThenBump, SyncRead and the shuffles
    // replaced by what they return
    private static void OperandOrder(DiffBuffers buffers)
    {
        var (a, b, c, d) = (buffers.Get<int>("a"), buffers.Get<int>("b"), buffers.Get<int>("c"), buffers.Get<int>("d"));
        var (e, f, g, h) = (buffers.Get<int>("e"), buffers.Get<int>("f"), buffers.Get<int>("g"), buffers.Get<int>("h"));
        var counter = buffers.Get<int>("counter");
        var cells = buffers.Get<SplitOrderCell>("cells");
        var flag = buffers.Get<bool>("flag")[0];
        var output = buffers.Get<int>("output");
        for (int l = 0; l < Rows; l++)
        {
            int o = l * 16;
            output[o + 0] = (a[l] = l + 100) + a[l];
            output[o + 1] = K.Bump(b, l) + b[l];
            output[o + 2] = c[l] + K.Bump(c, l);
            output[o + 3] = K.Triple(K.Add3(d, l), K.Double(d, l), d[l]);
            int x = l;
            output[o + 4] = 0 + (x + K.SetTenReturnOne(ref x));
            output[o + 5] = K.SetOut(out int y, l) + y;
            output[o + 6] = K.Bump(e, l) + (flag ? e[l] : -1);
            output[o + 7] = e[l];
            int m = K.Mark(f, l), q = flag ? f[l] : -1;
            output[o + 8] = m;
            output[o + 9] = q;
            int p = K.Bump(g, l), r = p + g[l];
            output[o + 10] = r;
            output[o + 11] = K.Bump(h, l) + h[l];
            output[o + 12] = l * 5;
            cells[K.NextIndex(counter, l)].F = l + 1;
            output[o + 13] = counter[l];
            output[o + 14] = cells[l].F;
            int sum = 0;
            for (int k = 0; k < 2; k++)
            {
                sum += k + sum;
            }

            output[o + 15] = sum + (sum + K.Bump(a, l));
        }
    }

    // The C# of CompoundOrderKernel, lane by lane, with the splits replaced by what they return
    private static void CompoundOrder(DiffBuffers buffers)
    {
        var (a, b, c, d) = (buffers.Get<int>("a"), buffers.Get<int>("b"), buffers.Get<int>("c"), buffers.Get<int>("d"));
        var e = buffers.Get<int>("e");
        var (x, y) = (buffers.Get<int>("xy")[0], buffers.Get<int>("xy")[1]);
        var output = buffers.Get<int>("output");
        for (int l = 0; l < Rows; l++)
        {
            int o = l * 13;
            a[l] += K.Bump(a, l);
            output[o + 0] = a[l];
            int local = l;
            local += K.SetTo(ref local, 50);
            output[o + 1] = local;
            output[o + 2] = K.Triple(b[l] += x, b[l], y);
            c[l] += d[l] += K.BumpBoth(c, d, l);
            output[o + 3] = c[l];
            output[o + 4] = d[l];
            int i32 = l + 1;
            i32 += K.SetTo(ref i32, 1000);
            output[o + 5] = i32;
            long i64 = l + 1L;
            i64 += K.SetToLong(ref i64, 1L << 40);
            output[o + 6] = (int)(i64 ^ (i64 >> 32));
            int shifted = l + 1;
            shifted <<= K.SetTo(ref shifted, 3);
            output[o + 7] = shifted;
            int unsignedShifted = -(l + 1);
            unsignedShifted >>>= K.SetTo(ref unsignedShifted, 64);
            output[o + 8] = unsignedShifted;
            float remainder = l + 7.5f;
            remainder %= K.SetToFloat(ref remainder, 1.25f);
            output[o + 9] = (int)(remainder * 16f);
            byte narrow = (byte)(250 + (l & 3));
            narrow += (byte)K.SetToByte(ref narrow, 3);
            output[o + 10] = narrow;
            int difference = l + e[l];
            difference -= K.SetTo(ref difference, 5);
            output[o + 11] = difference;
            bool both = (l & 1) == 0;
            both &= K.SetToBool(ref both, true);
            output[o + 12] = both ? 1 : 0;
        }
    }

    // ---- Where C# checks a location: the exception, and whether the value ran before it ----

    // The paths that still differ from C#, by kernel: an XFAIL entry, removed by the fix of its issue
    private static readonly Dictionary<(string Kernel, DiffPath Path), int> s_knownCheckDivergences = new()
    {
        // #991: the location is checked after the split, so the value runs first
        [(nameof(K.SpanElementTargetChecksFirstKernel), DiffPath.Managed)] = 991,
        [(nameof(K.FieldOfElementTargetChecksFirstKernel), DiffPath.Managed)] = 991,
        [(nameof(K.RefElementArgumentChecksFirstKernel), DiffPath.Managed)] = 991,
        [(nameof(K.FieldOfElementBeforeDivisionKernel), DiffPath.Managed)] = 991,
    };

    public static TheoryData<string, string, int> CheckCases => new()
    {
        // kernel, the exception C# throws, how many times the value ran before it
        { nameof(K.SpanElementTargetChecksFirstKernel), nameof(IndexOutOfRangeException), 0 },
        { nameof(K.SpanElementTargetChecksFirstTwinKernel), nameof(IndexOutOfRangeException), 0 },
        { nameof(K.FieldOfElementTargetChecksFirstKernel), nameof(IndexOutOfRangeException), 0 },
        { nameof(K.FieldOfElementTargetChecksFirstTwinKernel), nameof(IndexOutOfRangeException), 0 },
        { nameof(K.RefElementArgumentChecksFirstKernel), nameof(IndexOutOfRangeException), 0 },
        { nameof(K.RefElementArgumentChecksFirstTwinKernel), nameof(IndexOutOfRangeException), 0 },
        { nameof(K.ElementTargetChecksAtStoreKernel), nameof(IndexOutOfRangeException), 1 },
        { nameof(K.ElementTargetChecksAtStoreTwinKernel), nameof(IndexOutOfRangeException), 1 },
        { nameof(K.FieldOfElementBeforeDivisionKernel), nameof(IndexOutOfRangeException), 0 },
        { nameof(K.ElementBeforeDivisionKernel), nameof(DivideByZeroException), 1 },
    };

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(CheckCases))]
    public void Check_LocationOutOfRange_ThrowsWhereCSharpChecksIt(string kernel, string exception, int valueRuns)
    {
        // The release CUDA build does not check; an index out of range ends the native process (#718)
        var paths = DiffPaths.Available.Where(static p => p.Path == DiffPath.Managed).ToArray();
        Skip.If(paths.Length == 0, "Only the managed path checks an index out of range in process");
        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var counts = new int[1];
            var thrown = Run(path, kernel, counts);
            var observed = (thrown?.GetType().Name ?? "none", counts[0]);
            if (s_knownCheckDivergences.TryGetValue((kernel, path.Path), out var issue))
            {
                Assert.True(observed != (exception, valueRuns),
                    $"{kernel} on {path.Name} no longer differs (#{issue}): remove its known divergence");
                continue;
            }

            Assert.Equal((exception, valueRuns), observed);
        }
    }

    // Launches kernel on one thread with an index one past the end; the exception it throws, if any
    private static Exception? Run(DiffPathRunner path, string kernel, int[] counts)
    {
        var data = new int[8];
        var cells = new SplitOrderCell[4];
        const int I = 4;
        Action launch = kernel switch
        {
            nameof(K.SpanElementTargetChecksFirstKernel) => () =>
                K.LaunchSpanElementTargetChecksFirstKernel(data, counts, I, gridDimX: 1, blockDimX: 1),
            nameof(K.SpanElementTargetChecksFirstTwinKernel) => () =>
                K.LaunchSpanElementTargetChecksFirstTwinKernel(data, counts, I, gridDimX: 1, blockDimX: 1),
            nameof(K.FieldOfElementTargetChecksFirstKernel) => () =>
                K.LaunchFieldOfElementTargetChecksFirstKernel(cells, counts, I, gridDimX: 1, blockDimX: 1),
            nameof(K.FieldOfElementTargetChecksFirstTwinKernel) => () =>
                K.LaunchFieldOfElementTargetChecksFirstTwinKernel(cells, counts, I, gridDimX: 1, blockDimX: 1),
            nameof(K.RefElementArgumentChecksFirstKernel) => () =>
                K.LaunchRefElementArgumentChecksFirstKernel(data, counts, data.Length, gridDimX: 1, blockDimX: 1),
            nameof(K.RefElementArgumentChecksFirstTwinKernel) => () =>
                K.LaunchRefElementArgumentChecksFirstTwinKernel(data, counts, data.Length, gridDimX: 1,
                    blockDimX: 1),
            nameof(K.ElementTargetChecksAtStoreKernel) => () =>
                K.LaunchElementTargetChecksAtStoreKernel(data, counts, data.Length, gridDimX: 1, blockDimX: 1),
            nameof(K.ElementTargetChecksAtStoreTwinKernel) => () =>
                K.LaunchElementTargetChecksAtStoreTwinKernel(data, counts, data.Length, gridDimX: 1, blockDimX: 1),
            nameof(K.FieldOfElementBeforeDivisionKernel) => () =>
                K.LaunchFieldOfElementBeforeDivisionKernel(cells, counts, I, 0, gridDimX: 1, blockDimX: 1),
            nameof(K.ElementBeforeDivisionKernel) => () =>
                K.LaunchElementBeforeDivisionKernel(data, counts, data.Length, 0, gridDimX: 1, blockDimX: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(kernel), kernel, "Unknown kernel.")
        };
        try
        {
            path.Run(launch);
            return null;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or DivideByZeroException)
        {
            return ex;
        }
    }
}
