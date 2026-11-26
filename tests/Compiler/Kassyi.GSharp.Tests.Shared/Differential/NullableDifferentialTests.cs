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
            operands: new NullableCoalesceOperands
            {
                FirstPresence = b.Get<int>("hasA"),
                FirstValues = b.Get<int>("valsA"),
                SecondPresence = b.Get<int>("hasB"),
                SecondValues = b.Get<int>("valsB"),
                Fallbacks = b.Get<int>("fallbacks"),
            },
            results: b.Get<int>("r"),
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
            operands: new NullablePairOperands
            {
                FirstPresence = b.Get<int>("hasA"),
                FirstValues = b.Get<int>("valsA"),
                SecondPresence = b.Get<int>("hasB"),
                SecondValues = b.Get<int>("valsB"),
            },
            results: b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_floatCoalesceWithNaN = DiffCases.Create<int, float, float, float>(
        NullableDifferentialKernels.LaunchNullableFloatCoalesceWithNaN,
        inputs: static (rng, rows) =>
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
        shapes: LaunchShapes.OneDimensionalMatrix(Rows),
        firstInputName: "hasA",
        secondInputName: "valsA",
        thirdInputName: "fallbacks",
        outputName: "r");

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
            operands: new WiderWithNarrowerNullableOperands
            {
                WidePresence = b.Get<int>("hasA"),
                WideValues = b.Get<int>("valsA"),
                NarrowPresence = b.Get<int>("hasB"),
                NarrowSignedValues = b.Get<short>("valsB"),
                NarrowUnsignedValues = b.Get<ushort>("uValsB"),
            },
            results: b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    // The reproductions of #998 run 64 rows across one-dimensional launch shapes
    private const int ReproRows = 64;

    internal static readonly DifferentialCase s_emptyStackSpanNullComparison = DiffCases.Create(
        NullableDifferentialKernels.LaunchEmptyStackSpanNullComparisonReproKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .Out<int>("dst", rows, ["stackalloc_zero_eq_null", "stackalloc_zero_ne_null", "collection_empty_eq_null", "combined_null_flags"], OutputPolicy.Bitwise),
        shapes: LaunchShapes.OneDimensionalMatrix(ReproRows));

    internal static readonly DifferentialCase s_nullableArrayLikeDefaultValueNullCheck = DiffCases.Create(
        NullableDifferentialKernels.LaunchNullableArrayLikeDefaultValueNullCheckReproKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .Out<int>("dst", rows, ["mem_is_null", "mem_is_not_null", "mem_eq_ne_hasvalue", "seg_is_null_hasvalue"], OutputPolicy.Bitwise),
        shapes: LaunchShapes.OneDimensionalMatrix(ReproRows));

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
            operands: new NarrowCoalescePromotionOperands
            {
                Conditions = b.Get<int>("conds"),
                ByteValues = b.Get<byte>("ba"),
                UInt16Values = b.Get<ushort>("usa"),
            },
            results: b.Get<int>("dst"),
            rowCount: s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_nullableMemoryParamAndLocal = DiffCases.Create(
        NullableDifferentialKernels.LaunchNullableMemoryParamAndLocalReproKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .Out<int>("dst", rows, ["null_mem", "non_null_mem"], OutputPolicy.Exact),
        shapes: LaunchShapes.OneDimensionalMatrix(ReproRows),
        bufferName: "dst");

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

    internal static readonly DifferentialCase s_liftedDoubleToNullableIntCast = DiffCases.CreateRows<double, int, int>(
        NullableDifferentialKernels.LaunchLiftedDoubleToNullableIntCastReproKernel,
        inputs: static (rng, rows) =>
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
        shapes: LaunchShapes.OneDimensionalMatrix(ReproRows),
        firstInputName: "src",
        secondInputName: "conds",
        outputName: "dst");

    internal static readonly DifferentialCase s_coalesceAssignmentValue = DiffCases.CreateRows<int, int, long>(
        NullableDifferentialKernels.LaunchCoalesceAssignmentValueKernel,
        inputs: static (rng, rows) =>
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
        shapes: LaunchShapes.OneDimensionalMatrix(ReproRows),
        firstInputName: "conds",
        secondInputName: "vals",
        outputName: "dst");

    internal static readonly DifferentialCase s_nullLiteralCastToArray = DiffCases.Create(
        NullableDifferentialKernels.LaunchNullLiteralCastToArrayKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .Out<int>(
                "dst",
                rows,
                ["cast_is_null", "picked_is_null", "parenthesized_is_null", "coalesce_keeps_array", "default_is_null",
                 "default_expression_is_null"],
                OutputPolicy.Exact),
        shapes: LaunchShapes.OneDimensionalMatrix(ReproRows));

    internal static readonly DifferentialCase s_emptyArrayIsNotNull = DiffCases.Create(
        NullableDifferentialKernels.LaunchEmptyArrayIsNotNullKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("empty", Array.Empty<int>())
            .In("other", DiffValues.Int32(rng, rows))
            .Out<int>(
                "dst",
                rows,
                ["empty_is_null", "empty_is_null_pattern", "coalesce_length", "empty_length", "slice_length",
                 "slice_is_null"],
                OutputPolicy.Exact),
        shapes: LaunchShapes.OneDimensionalMatrix(ReproRows),
        firstInputName: "empty",
        secondInputName: "other",
        outputName: "dst");

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

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableOperations_NullLiteralCastToArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullLiteralCastToArray);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NullableOperations_EmptyArrayFromTheHost_IsNotNull() => DifferentialRunner.Run(s_emptyArrayIsNotNull);
}
