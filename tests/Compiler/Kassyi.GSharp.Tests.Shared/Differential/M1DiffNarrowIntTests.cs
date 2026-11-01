#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for narrow integers (<see cref="M1DiffNarrowIntKernels"/>): compound assignments, boundary
/// <c>++</c>/<c>--</c>, <c>~</c> and <c>&gt;&gt;&gt;</c>, narrow and signed enum operators, mixed-sign division/remainder,
/// <c>BitOperations</c> return signedness (#876), and compound/tuple assignment target evaluation order.
/// </summary>
public class M1DiffNarrowIntTests
{
    private static readonly int[] s_narrowAndIntBoundaries =
    [
        0,
        1,
        -1,
        2,
        -2,
        126,
        127,
        128,
        -127,
        -128,
        -129,
        200,
        254,
        255,
        256,
        32766,
        32767,
        32768,
        -32767,
        -32768,
        -32769,
        60000,
        65534,
        65535,
        65536,
        int.MaxValue - 1,
        int.MaxValue,
        int.MinValue + 1,
        int.MinValue,
    ];

    private static readonly int[] s_shiftBoundaries =
    [
        0,
        1,
        7,
        8,
        9,
        15,
        16,
        17,
        31,
        32,
        33,
        63,
        64,
        -1,
        -32,
    ];

    private static readonly long[] s_int64DivBoundaries =
    [
        0L,
        1L,
        -1L,
        2L,
        -2L,
        3L,
        -3L,
        127L,
        -128L,
        255L,
        32767L,
        -32768L,
        65535L,
        int.MaxValue,
        int.MinValue,
        (long)uint.MaxValue,
        (long)uint.MaxValue + 1L,
        long.MaxValue - 1L,
        long.MaxValue,
        long.MinValue + 1L,
        long.MinValue,
    ];

    private static readonly DifferentialCase s_narrowCompoundAndStep = new("M1DiffNarrowIntKernels.NarrowCompoundAndStepMatrixKernel")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) =>
        {
            var a = NarrowIntValues(rng, rows);
            var b = NarrowIntValues(rng, rows);
            var sh = ShiftValues(rng, rows);
            return new DiffBuffers()
                .In("a", a)
                .In("b", b)
                .In("sh", sh)
                .Out<int>("r", rows, M1DiffNarrowIntKernels.NarrowCompoundAndStepColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => M1DiffNarrowIntKernels.LaunchNarrowCompoundAndStepMatrixKernel(
            b.Get<int>("a"),
            b.Get<int>("b"),
            b.Get<int>("sh"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffNarrowIntKernels.NarrowCompoundAndStepMatrixKernel(
            b.Get<int>("a"),
            b.Get<int>("b"),
            b.Get<int>("sh"),
            b.Get<int>("r")
        ),
    };

    private static readonly DifferentialCase s_narrowEnumAndSignedEnum = new("M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", NarrowIntValues(rng, rows))
            .In("b", NarrowIntValues(rng, rows))
            .Out<int>("r", rows, M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumColumns, OutputPolicy.Exact),
        Launch = static (b, s) => M1DiffNarrowIntKernels.LaunchNarrowEnumAndSignedEnumMatrixKernel(
            b.Get<int>("a"),
            b.Get<int>("b"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffNarrowIntKernels.NarrowEnumAndSignedEnumMatrixKernel(
            b.Get<int>("a"),
            b.Get<int>("b"),
            b.Get<int>("r")
        ),
    };

    private static readonly DifferentialCase s_mixedSignDivRem = new("M1DiffNarrowIntKernels.MixedSignDivRemMatrixKernel")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) =>
        {
            var l = Int64Values(rng, rows);
            var iNz = SafeNonZeroInt32Values(rng, rows);
            var uNz = SafeNonZeroUInt32Values(rng, rows);
            var lNz = SafeNonZeroInt64Values(rng, rows);
            return new DiffBuffers()
                .In("l", l)
                .In("iNz", iNz)
                .In("uNz", uNz)
                .In("lNz", lNz)
                .Out<long>("r", rows, M1DiffNarrowIntKernels.MixedSignDivRemColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => M1DiffNarrowIntKernels.LaunchMixedSignDivRemMatrixKernel(
            b.Get<long>("l"),
            b.Get<int>("iNz"),
            b.Get<uint>("uNz"),
            b.Get<long>("lNz"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffNarrowIntKernels.MixedSignDivRemMatrixKernel(
            b.Get<long>("l"),
            b.Get<int>("iNz"),
            b.Get<uint>("uNz"),
            b.Get<long>("lNz"),
            b.Get<long>("r")
        ),
    };

    private static readonly DifferentialCase s_bitOpsReturnSign = new("M1DiffNarrowIntKernels.BitOpsReturnSignMatrixKernel")
    {
        Shape = LaunchShape.ForRows(32, blockX: 32),
        Inputs = static (rng, rows) =>
        {
            var u = Array.ConvertAll(DiffValues.Int32(rng, rows), static v => unchecked((uint)v));
            var ul = Array.ConvertAll(Int64Values(rng, rows), static v => unchecked((ulong)v));
            return new DiffBuffers()
                .In("u", u)
                .In("ul", ul)
                .Out<int>("r", rows, M1DiffNarrowIntKernels.BitOpsReturnSignColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => M1DiffNarrowIntKernels.LaunchBitOpsReturnSignMatrixKernel(
            b.Get<uint>("u"),
            b.Get<ulong>("ul"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffNarrowIntKernels.BitOpsReturnSignMatrixKernel(
            b.Get<uint>("u"),
            b.Get<ulong>("ul"),
            b.Get<int>("r")
        ),
    };

    private static readonly DifferentialCase s_targetEvaluationOrder = new("M1DiffNarrowIntKernels.TargetEvaluationOrderMatrixKernel")
    {
        Shape = LaunchShape.ForRows(16, blockX: 16),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, M1DiffNarrowIntKernels.TargetEvaluationOrderColumns, OutputPolicy.Exact),
        Launch = static (b, s) => M1DiffNarrowIntKernels.LaunchTargetEvaluationOrderMatrixKernel(
            b.Get<int>("a"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffNarrowIntKernels.TargetEvaluationOrderMatrixKernel(
            b.Get<int>("a"),
            b.Get<int>("r")
        ),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NarrowCompoundAndStep_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_narrowCompoundAndStep);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NarrowEnumAndSignedEnum_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_narrowEnumAndSignedEnum);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MixedSignDivRem_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_mixedSignDivRem);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_BitOpsReturnSign_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_bitOpsReturnSign);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_TargetEvaluationOrder_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_targetEvaluationOrder);

    private static int[] NarrowIntValues(DiffRandom rng, int count) =>
        Values(rng, count, s_narrowAndIntBoundaries, static r =>
            r.NextBool()
                ? s_narrowAndIntBoundaries[r.NextBelow(s_narrowAndIntBoundaries.Length)]
                : unchecked((int)r.NextUInt64()));

    private static int[] ShiftValues(DiffRandom rng, int count) =>
        Values(rng, count, s_shiftBoundaries, static r =>
            r.NextBool()
                ? s_shiftBoundaries[r.NextBelow(s_shiftBoundaries.Length)]
                : unchecked((int)r.NextUInt64()));

    private static long[] Int64Values(DiffRandom rng, int count) =>
        Values(rng, count, s_int64DivBoundaries, static r =>
            r.NextBool()
                ? s_int64DivBoundaries[r.NextBelow(s_int64DivBoundaries.Length)]
                : unchecked((long)r.NextUInt64()));

    private static T[] Values<T>(DiffRandom rng, int count, T[] boundaries, Func<DiffRandom, T> next)
    {
        var values = new T[count];
        for (int idx = 0; idx < count; idx++)
        {
            values[idx] = rng.IsBoundarySeed ? boundaries[idx % boundaries.Length] : next(rng);
        }

        return values;
    }

    private static int[] SafeNonZeroInt32Values(DiffRandom rng, int count) =>
        Array.ConvertAll(NarrowIntValues(rng, count), static v => v == 0 || v == -1 ? 3 : v);

    private static uint[] SafeNonZeroUInt32Values(DiffRandom rng, int count) =>
        Array.ConvertAll(NarrowIntValues(rng, count), static v =>
        {
            uint u = unchecked((uint)v);
            return u == 0u ? 3u : u;
        });

    private static long[] SafeNonZeroInt64Values(DiffRandom rng, int count) =>
        Array.ConvertAll(Int64Values(rng, count), static v => v == 0L || v == -1L ? 3L : v);
}
