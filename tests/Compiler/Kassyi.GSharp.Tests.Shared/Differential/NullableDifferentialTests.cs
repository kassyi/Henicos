#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests (<c>Nullable&lt;T&gt;</c> / <c>T?</c> representation, <c>HasValue</c>,
/// <c>Value</c>, null-coalescing <c>??</c>, compound <c>??=</c>, mixed-sign promotion, and null comparisons of empty spans and
/// nullable array-like structs).
/// </summary>
public class NullableDifferentialTests
{
    private const int Rows = 441;

    private static readonly DifferentialCase s_coalesceAndSideEffects = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NullableCoalesceAndSideEffects)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (valsA, valsB) = DiffValues.Int32Pairs(rng, rows);
            var fallbacks = DiffValues.Int32(rng, rows);
            var hasA = new int[rows];
            var hasB = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                hasA[k] = rng.IsBoundarySeed ? (k & 1) : (rng.NextBool() ? 1 : 0);
                hasB[k] = rng.IsBoundarySeed ? ((k >> 1) & 1) : (rng.NextBool() ? 1 : 0);
            }

            return new DiffBuffers()
                .In("hasA", hasA)
                .In("valsA", valsA)
                .In("hasB", hasB)
                .In("valsB", valsB)
                .In("fallbacks", fallbacks)
                .Out<int>("r", rows, NullableDifferentialKernels.CoalesceAndSideEffectsColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => NullableDifferentialKernels.LaunchNullableCoalesceAndSideEffects(
            b.Get<int>("hasA"),
            b.Get<int>("valsA"),
            b.Get<int>("hasB"),
            b.Get<int>("valsB"),
            b.Get<int>("fallbacks"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => NullableDifferentialKernels.NullableCoalesceAndSideEffects(
            b.Get<int>("hasA"),
            b.Get<int>("valsA"),
            b.Get<int>("hasB"),
            b.Get<int>("valsB"),
            b.Get<int>("fallbacks"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_deviceFunctionsAndKleene = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NullableDeviceFunctionsAndKleeneLogic)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (valsA, valsB) = DiffValues.Int32Pairs(rng, rows);
            var hasA = new int[rows];
            var hasB = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                // Cycle through (0,0), (1,0), (0,1), (1,1) so all 9 Kleene states are hit on seed 0
                hasA[k] = rng.IsBoundarySeed ? (k % 3 == 0 ? 0 : 1) : (rng.NextBool() ? 1 : 0);
                hasB[k] = rng.IsBoundarySeed ? ((k / 3) % 3 == 0 ? 0 : 1) : (rng.NextBool() ? 1 : 0);
            }

            return new DiffBuffers()
                .In("hasA", hasA)
                .In("valsA", valsA)
                .In("hasB", hasB)
                .In("valsB", valsB)
                .Out<int>("r", rows, NullableDifferentialKernels.DeviceFunctionsAndKleeneColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => NullableDifferentialKernels.LaunchNullableDeviceFunctionsAndKleeneLogic(
            b.Get<int>("hasA"),
            b.Get<int>("valsA"),
            b.Get<int>("hasB"),
            b.Get<int>("valsB"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => NullableDifferentialKernels.NullableDeviceFunctionsAndKleeneLogic(
            b.Get<int>("hasA"),
            b.Get<int>("valsA"),
            b.Get<int>("hasB"),
            b.Get<int>("valsB"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_floatCoalesceWithNaN = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NullableFloatCoalesceWithNaN)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var valsA = DiffValues.Single(rng, rows);
            var fallbacks = DiffValues.Single(rng, rows);
            var hasA = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                hasA[k] = rng.IsBoundarySeed ? (k & 1) : (rng.NextBool() ? 1 : 0);
            }

            return new DiffBuffers()
                .In("hasA", hasA)
                .In("valsA", valsA)
                .In("fallbacks", fallbacks)
                .Out<float>("r", rows, NullableDifferentialKernels.FloatCoalesceWithNaNColumns, OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => NullableDifferentialKernels.LaunchNullableFloatCoalesceWithNaN(
            b.Get<int>("hasA"),
            b.Get<float>("valsA"),
            b.Get<float>("fallbacks"),
            b.Get<float>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => NullableDifferentialKernels.NullableFloatCoalesceWithNaN(
            b.Get<int>("hasA"),
            b.Get<float>("valsA"),
            b.Get<float>("fallbacks"),
            b.Get<float>("r")),
    };

    private static readonly DifferentialCase s_coalesceWiderWithNarrower = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.CoalesceWiderNullableWithNarrowerNullable)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (valsA, rawB) = DiffValues.Int32Pairs(rng, rows);
            var valsB = Array.ConvertAll(rawB, static v => unchecked((short)v));
            var uValsB = Array.ConvertAll(rawB, static v => unchecked((ushort)v));
            var hasA = new int[rows];
            var hasB = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                hasA[k] = rng.IsBoundarySeed ? ((k & 1) == 0 ? 1 : 0) : (rng.NextBool() ? 1 : 0);
                hasB[k] = rng.IsBoundarySeed ? (((k >> 1) & 1) == 0 ? 1 : 0) : (rng.NextBool() ? 1 : 0);
            }

            return new DiffBuffers()
                .In("hasA", hasA)
                .In("valsA", valsA)
                .In("hasB", hasB)
                .In("valsB", valsB)
                .In("uValsB", uValsB)
                .Out<int>("r", rows, NullableDifferentialKernels.CoalesceWiderWithNarrowerColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => NullableDifferentialKernels.LaunchCoalesceWiderNullableWithNarrowerNullable(
            b.Get<int>("hasA"),
            b.Get<int>("valsA"),
            b.Get<int>("hasB"),
            b.Get<short>("valsB"),
            b.Get<ushort>("uValsB"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => NullableDifferentialKernels.CoalesceWiderNullableWithNarrowerNullable(
            b.Get<int>("hasA"),
            b.Get<int>("valsA"),
            b.Get<int>("hasB"),
            b.Get<short>("valsB"),
            b.Get<ushort>("uValsB"),
            b.Get<int>("r")),
    };

    // The reproductions of #998 run 64 rows in blocks of 32
    private const int ReproRows = 64;
    private static readonly LaunchShape s_reproShape = LaunchShape.ForRows(ReproRows, blockX: 32);

    private static readonly DifferentialCase s_emptyStackSpanNullComparison = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.EmptyStackSpanNullComparisonReproKernel)}")
    {
        Shape = s_reproShape,
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .Out<int>("dst", rows, ["stackalloc_zero_eq_null", "stackalloc_zero_ne_null", "collection_empty_eq_null", "combined_null_flags"], OutputPolicy.Bitwise),
        Launch = static (b, s) => NullableDifferentialKernels.LaunchEmptyStackSpanNullComparisonReproKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static b => NullableDifferentialKernels.EmptyStackSpanNullComparisonReproKernel(
            b.Get<int>("a"), b.Get<int>("dst"), ReproRows),
    };

    private static readonly DifferentialCase s_nullableArrayLikeDefaultValueNullCheck = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NullableArrayLikeDefaultValueNullCheckReproKernel)}")
    {
        Shape = s_reproShape,
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .Out<int>("dst", rows, ["mem_is_null", "mem_is_not_null", "mem_eq_ne_hasvalue", "seg_is_null_hasvalue"], OutputPolicy.Bitwise),
        Launch = static (b, s) => NullableDifferentialKernels.LaunchNullableArrayLikeDefaultValueNullCheckReproKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static b => NullableDifferentialKernels.NullableArrayLikeDefaultValueNullCheckReproKernel(
            b.Get<int>("a"), b.Get<int>("dst"), ReproRows),
    };

    private static readonly DifferentialCase s_narrowNullableCoalesceVarPromotion = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NarrowNullableCoalesceVarPromotionReproKernel)}")
    {
        Shape = s_reproShape,
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("conds", DiffValues.Int32(rng, rows))
            .In("ba", DiffValues.Byte(rng, rows))
            .In("usa", DiffValues.UInt16(rng, rows))
            .Out<int>("dst", rows, ["byte_nullable_coalesce_inc", "ushort_nullable_coalesce_inc", "byte_nullable_ushort_coalesce_inc"], OutputPolicy.Bitwise),
        Launch = static (b, s) => NullableDifferentialKernels.LaunchNarrowNullableCoalesceVarPromotionReproKernel(
            b.Get<int>("conds"), b.Get<byte>("ba"), b.Get<ushort>("usa"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static b => NullableDifferentialKernels.NarrowNullableCoalesceVarPromotionReproKernel(
            b.Get<int>("conds"), b.Get<byte>("ba"), b.Get<ushort>("usa"), b.Get<int>("dst"), ReproRows),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullableCoalesceAndSideEffects_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_coalesceAndSideEffects);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullableDeviceFunctionsAndKleeneLogic_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_deviceFunctionsAndKleene);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullableFloatCoalesceWithNaN_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_floatCoalesceWithNaN);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CoalesceWiderNullableWithNarrowerNullable_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_coalesceWiderWithNarrower);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_EmptyStackSpanNullComparison_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_emptyStackSpanNullComparison);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullableArrayLikeDefaultValueNullCheck_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullableArrayLikeDefaultValueNullCheck);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NarrowNullableCoalesceVarPromotion_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_narrowNullableCoalesceVarPromotion);
}
