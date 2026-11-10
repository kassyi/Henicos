#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.IntegerTrapAndCheckedDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for the shift-count masking boundary matrix across negative and &gt;= width counts (#722) and
/// <c>unchecked</c> arithmetic wrap-around.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
[Collection(TestCollections.IntegerTrapAndCheckedDifferential)]
public class IntegerShiftAndWrapDifferentialTests
{
    private static readonly DifferentialCase s_shiftCountMaskBoundaryMatrix = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.ShiftCountMaskBoundaryMatrixKernel)))
    {
        Shape = LaunchShapes.PartialWarp(74, 37),
        ExtraShapesSeed0Only =
        [
            LaunchShapes.Linear(64, 32),
        ],
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            int[] shiftCounts =
            [
                -1, -31, -32, -63, -64, 0, 1, 7, 15, 16, 31, 32, 33, 63, 64, 65,
                int.MinValue, int.MaxValue,
            ];
            var iVals = DiffValues.Int32(rng, rows);
            var uVals = new uint[rows];
            var lVals = new long[rows];
            var ulVals = new ulong[rows];
            var sVals = DiffValues.Int16(rng, rows);
            var sbVals = DiffValues.SByte(rng, rows);
            var counts = new int[rows];

            for (int k = 0; k < rows; k++)
            {
                uVals[k] = (uint)iVals[k];
                lVals[k] = rng.IsBoundarySeed
                    ? s_int64Boundaries[k % s_int64Boundaries.Length]
                    : ((long)rng.NextInt32() << 32) | rng.NextUInt32();
                ulVals[k] = rng.IsBoundarySeed
                    ? s_uint64Boundaries[k % s_uint64Boundaries.Length]
                    : (ulong)lVals[k];
                counts[k] = rng.IsBoundarySeed
                    ? shiftCounts[k % shiftCounts.Length]
                    : (rng.NextBool() ? shiftCounts[rng.NextBelow(shiftCounts.Length)] : rng.NextInt32());
            }

            return new DiffBuffers()
                .In("iVals", iVals, readOnly: true)
                .In("uVals", uVals, readOnly: true)
                .In("lVals", lVals, readOnly: true)
                .In("ulVals", ulVals, readOnly: true)
                .In("sVals", sVals, readOnly: true)
                .In("sbVals", sbVals, readOnly: true)
                .In("counts", counts, readOnly: true)
                .Out<int>(
                    "dst",
                    rows,
                    IntegerTrapAndCheckedDifferentialKernels.ShiftMaskBoundaryColumns,
                    OutputPolicy.Exact);
        },
        Launch = static (b, s) =>
            IntegerTrapAndCheckedDifferentialKernels.LaunchShiftCountMaskBoundaryMatrixKernel(
            b.Get<int>("iVals"),
            b.Get<uint>("uVals"),
            b.Get<long>("lVals"),
            b.Get<ulong>("ulVals"),
            b.Get<short>("sVals"),
            b.Get<sbyte>("sbVals"),
            b.Get<int>("counts"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) =>
            IntegerTrapAndCheckedDifferentialKernels.ShiftCountMaskBoundaryMatrixKernel(
            b.Get<int>("iVals"),
            b.Get<uint>("uVals"),
            b.Get<long>("lVals"),
            b.Get<ulong>("ulVals"),
            b.Get<short>("sVals"),
            b.Get<sbyte>("sbVals"),
            b.Get<int>("counts"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_uncheckedIntegerWrap = new(
        KernelName(nameof(IntegerTrapAndCheckedDifferentialKernels.UncheckedIntegerWrapKernel)))
    {
        Shape = LaunchShapes.PartialWarp(74, 37),
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var (left, right) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("left", left, readOnly: true)
                .In("right", right, readOnly: true)
                .Out<int>(
                    "dst",
                    rows,
                    IntegerTrapAndCheckedDifferentialKernels.UncheckedIntegerWrapColumns,
                    OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchUncheckedIntegerWrapKernel(
            b.Get<int>("left"),
            b.Get<int>("right"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.UncheckedIntegerWrapKernel(
            b.Get<int>("left"),
            b.Get<int>("right"),
            b.Get<int>("dst"),
            s.Rows),
    };

    [SkippableFact]
    public void Test_ShiftCountMaskBoundaryMatrix_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_shiftCountMaskBoundaryMatrix);

    [SkippableFact]
    public void Test_UncheckedIntegerWrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_uncheckedIntegerWrap);
}
