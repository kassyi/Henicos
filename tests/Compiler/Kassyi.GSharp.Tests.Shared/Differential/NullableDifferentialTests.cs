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

    internal static readonly DifferentialCase s_coalesceAndSideEffects = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NullableCoalesceAndSideEffects)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
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
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_deviceFunctionsAndKleene = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NullableDeviceFunctionsAndKleeneLogic)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
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
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_floatCoalesceWithNaN = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NullableFloatCoalesceWithNaN)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
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
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_coalesceWiderWithNarrower = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.CoalesceWiderNullableWithNarrowerNullable)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
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
        OracleIsLaunch = true,
    };

    // The reproductions of #998 run 64 rows across one-dimensional launch shapes
    private const int ReproRows = 64;

    internal static readonly DifferentialCase s_emptyStackSpanNullComparison = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.EmptyStackSpanNullComparisonReproKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(ReproRows),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .Out<int>("dst", rows, ["stackalloc_zero_eq_null", "stackalloc_zero_ne_null", "collection_empty_eq_null", "combined_null_flags"], OutputPolicy.Bitwise),
        Launch = static (b, s) => NullableDifferentialKernels.LaunchEmptyStackSpanNullComparisonReproKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_nullableArrayLikeDefaultValueNullCheck = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NullableArrayLikeDefaultValueNullCheckReproKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(ReproRows),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .Out<int>("dst", rows, ["mem_is_null", "mem_is_not_null", "mem_eq_ne_hasvalue", "seg_is_null_hasvalue"], OutputPolicy.Bitwise),
        Launch = static (b, s) => NullableDifferentialKernels.LaunchNullableArrayLikeDefaultValueNullCheckReproKernel(
            b.Get<int>("a"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_narrowNullableCoalesceVarPromotion = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NarrowNullableCoalesceVarPromotionReproKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(ReproRows),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("conds", DiffValues.Int32(rng, rows))
            .In("ba", DiffValues.Byte(rng, rows))
            .In("usa", DiffValues.UInt16(rng, rows))
            .Out<int>("dst", rows, ["byte_nullable_coalesce_inc", "ushort_nullable_coalesce_inc", "byte_nullable_ushort_coalesce_inc"], OutputPolicy.Bitwise),
        Launch = static (b, s) => NullableDifferentialKernels.LaunchNarrowNullableCoalesceVarPromotionReproKernel(
            b.Get<int>("conds"), b.Get<byte>("ba"), b.Get<ushort>("usa"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_nullableMemoryParamAndLocal = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NullableMemoryParamAndLocalReproKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(ReproRows),
        Inputs = static (rng, rows) => new DiffBuffers()
            .Out<int>("dst", rows, ["null_mem", "non_null_mem"], OutputPolicy.Exact),
        Launch = static (b, s) => NullableDifferentialKernels.LaunchNullableMemoryParamAndLocalReproKernel(
            b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_nullableMemoryKernelParamToLocal_Null = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NullableMemoryKernelParamToLocalReproKernel)}_Null")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(ReproRows),
        Inputs = static (rng, rows) => new DiffBuffers()
            .Out<int>("dst", rows, ["is_null", "has_value"], OutputPolicy.Exact),
        Launch = static (b, s) => NullableDifferentialKernels.LaunchNullableMemoryKernelParamToLocalReproKernel(
            null, b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_nullableMemoryKernelParamToLocal_WithValue = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.NullableMemoryKernelParamToLocalReproKernel)}_WithValue")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(ReproRows),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("bias", new int[] { 42, 84 })
            .Out<int>("dst", rows, ["is_null", "has_value"], OutputPolicy.Exact),
        Launch = static (b, s) => NullableDifferentialKernels.LaunchNullableMemoryKernelParamToLocalReproKernel(
            b.Get<int>("bias"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_liftedDoubleToNullableIntCast = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.LiftedDoubleToNullableIntCastReproKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(ReproRows),
        Inputs = static (rng, rows) =>
        {
            var src = new double[rows];
            var conds = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                conds[k] = k % 5 == 0 ? 0 : 1;
                src[k] = (k % 5) switch
                {
                    1 => 1e20,
                    2 => double.NaN,
                    3 => -1e20,
                    4 => 42.5,
                    _ => 0.0
                };
            }

            return new DiffBuffers()
                .In("src", src)
                .In("conds", conds)
                .Out<int>("dst", rows, ["is_null", "val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => NullableDifferentialKernels.LaunchLiftedDoubleToNullableIntCastReproKernel(
            b.Get<double>("src"), b.Get<int>("conds"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_coalesceAssignmentValue = new(
        $"{nameof(NullableDifferentialKernels)}.{nameof(NullableDifferentialKernels.CoalesceAssignmentValueKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(ReproRows),
        Inputs = static (rng, rows) =>
        {
            var conds = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                // The boundary seed walks every combination of null and non-null targets of columns 0 to 4 and both of
                // column 5
                conds[k] = rng.IsBoundarySeed ? k | ((k & 1) << 6) : rng.NextInt32();
            }

            return new DiffBuffers()
                .In("conds", conds)
                .In("vals", DiffValues.Int32(rng, rows))
                .Out<long>(
                    "dst",
                    rows,
                    ["xor_of_value", "nullable_value", "nested_value", "widened_value", "promoted_value",
                     "value_before_write"],
                    OutputPolicy.Exact);
        },
        Launch = static (b, s) => NullableDifferentialKernels.LaunchCoalesceAssignmentValueKernel(
            b.Get<int>("conds"), b.Get<int>("vals"), b.Get<long>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableOperations_CoalesceAndSideEffects_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_coalesceAndSideEffects);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableOperations_DeviceFunctionsAndKleeneLogic_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_deviceFunctionsAndKleene);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableOperations_FloatCoalesceWithNaN_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_floatCoalesceWithNaN);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableOperations_CoalesceWiderWithNarrower_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_coalesceWiderWithNarrower);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableOperations_EmptyStackSpanNullComparison_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_emptyStackSpanNullComparison);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableOperations_ArrayLikeDefaultValueNullCheck_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullableArrayLikeDefaultValueNullCheck);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableOperations_NarrowCoalesceVarPromotion_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_narrowNullableCoalesceVarPromotion);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableOperations_MemoryParamAndLocal_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullableMemoryParamAndLocal);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableOperations_MemoryKernelParamToLocalNull_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullableMemoryKernelParamToLocal_Null);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableOperations_MemoryKernelParamToLocalWithValue_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullableMemoryKernelParamToLocal_WithValue);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableOperations_LiftedDoubleToNullableIntCast_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_liftedDoubleToNullableIntCast);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableOperations_CoalesceAssignmentValue_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_coalesceAssignmentValue);
}
