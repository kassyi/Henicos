#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public sealed class M1DiffSwitchTests
{
    private const int Rows = 64;
    private static readonly LaunchShape s_shape = LaunchShape.ForRows(Rows, blockX: 32);

    private static long[] FillInt64(DiffRandom rng, int count)
    {
        var arr = new long[count];
        for (int idx = 0; idx < count; idx++)
        {
            arr[idx] = unchecked((long)rng.NextUInt64());
        }
        return arr;
    }

    private static ulong[] FillUInt64(DiffRandom rng, int count)
    {
        var arr = new ulong[count];
        for (int idx = 0; idx < count; idx++)
        {
            arr[idx] = rng.NextUInt64();
        }
        return arr;
    }

    private static uint[] FillUInt32(DiffRandom rng, int count)
    {
        var arr = new uint[count];
        for (int idx = 0; idx < count; idx++)
        {
            arr[idx] = unchecked((uint)DiffValues.NextInt32(rng));
        }
        return arr;
    }

    private static short[] FillInt16(DiffRandom rng, int count)
    {
        short[] bounds = [0, 1, -1, short.MinValue, short.MaxValue, -128, 127, 255, 256];
        var arr = new short[count];
        for (int idx = 0; idx < count; idx++)
        {
            arr[idx] = rng.IsBoundarySeed || rng.NextBool()
                ? bounds[idx % bounds.Length]
                : unchecked((short)rng.NextInt32());
        }
        return arr;
    }

    private static ushort[] FillUInt16(DiffRandom rng, int count)
    {
        ushort[] bounds = [0, 1, 127, 128, 255, 256, 32767, 32768, ushort.MaxValue];
        var arr = new ushort[count];
        for (int idx = 0; idx < count; idx++)
        {
            arr[idx] = rng.IsBoundarySeed || rng.NextBool()
                ? bounds[idx % bounds.Length]
                : unchecked((ushort)rng.NextUInt32());
        }
        return arr;
    }

    private static byte[] FillUInt8(DiffRandom rng, int count)
    {
        byte[] bounds = [0, 1, 2, 127, 128, 250, 254, byte.MaxValue];
        var arr = new byte[count];
        for (int idx = 0; idx < count; idx++)
        {
            arr[idx] = rng.IsBoundarySeed || rng.NextBool()
                ? bounds[idx % bounds.Length]
                : unchecked((byte)rng.NextUInt32());
        }
        return arr;
    }

    private static sbyte[] FillInt8(DiffRandom rng, int count)
    {
        sbyte[] bounds = [0, 1, -1, sbyte.MinValue, sbyte.MaxValue, -127, 126];
        var arr = new sbyte[count];
        for (int idx = 0; idx < count; idx++)
        {
            arr[idx] = rng.IsBoundarySeed || rng.NextBool()
                ? bounds[idx % bounds.Length]
                : unchecked((sbyte)rng.NextInt32());
        }
        return arr;
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SwitchIntStatement_MatchesTheOracleOnEveryPath()
    {
        var testCase = new DifferentialCase("M1DiffSwitchKernels.SwitchIntStatementKernel")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) =>
            {
                var a = DiffValues.Int32(rng, rows);
                a[0] = int.MinValue;
                a[1] = M1DiffSwitchKernels.ConstNegCase;
                a[2] = -1;
                a[3] = 0;
                a[4] = 1;
                a[5] = 2;
                a[6] = 3;
                a[7] = M1DiffSwitchKernels.ConstPosCase;
                a[8] = int.MaxValue;
                var b = DiffValues.Int32(rng, rows);
                return new DiffBuffers()
                    .In("a", a)
                    .In("b", b)
                    .Out<int>("dst", rows, ["standard", "no_default", "nested", "bool_switch"], OutputPolicy.Bitwise);
            },
            Launch = static (b, s) => M1DiffSwitchKernels.LaunchSwitchIntStatementKernel(
                b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("dst"), s.Rows,
                gridDimX: s.GridX, blockDimX: s.BlockX),
            Oracle = static b => M1DiffSwitchKernels.SwitchIntStatementKernel(
                b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("dst"), Rows),
        };

        DifferentialRunner.Run(testCase);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SwitchWideInt64Statement_MatchesTheOracleOnEveryPath()
    {
        var testCase = new DifferentialCase("M1DiffSwitchKernels.SwitchWideInt64StatementKernel")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) =>
            {
                var a = FillInt64(rng, rows);
                a[0] = long.MinValue;
                a[1] = -5000000000L;
                a[2] = -1L;
                a[3] = 0L;
                a[4] = 5000000000L;
                a[5] = long.MaxValue;

                var ua = FillUInt64(rng, rows);
                ua[0] = 0UL;
                ua[1] = 1UL;
                ua[2] = 2UL;
                ua[3] = 0x1_0000_0000UL;
                ua[4] = 0x8000_0000_0000_0000UL;
                ua[5] = ulong.MaxValue;

                return new DiffBuffers()
                    .In("a", a)
                    .In("ua", ua)
                    .Out<long>("dst", rows, ["long_switch", "ulong_switch"], OutputPolicy.Bitwise);
            },
            Launch = static (b, s) => M1DiffSwitchKernels.LaunchSwitchWideInt64StatementKernel(
                b.Get<long>("a"), b.Get<ulong>("ua"), b.Get<long>("dst"), s.Rows,
                gridDimX: s.GridX, blockDimX: s.BlockX),
            Oracle = static b => M1DiffSwitchKernels.SwitchWideInt64StatementKernel(
                b.Get<long>("a"), b.Get<ulong>("ua"), b.Get<long>("dst"), Rows),
        };

        DifferentialRunner.Run(testCase);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SwitchNarrowCharEnumStatement_MatchesTheOracleOnEveryPath()
    {
        var testCase = new DifferentialCase("M1DiffSwitchKernels.SwitchNarrowCharEnumStatementKernel")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("ba", FillUInt8(rng, rows))
                .In("sba", FillInt8(rng, rows))
                .In("sa", FillInt16(rng, rows))
                .In("usa", FillUInt16(rng, rows))
                .Out<int>("dst", rows, ["byte_sbyte", "short_ushort", "char_switch", "enum_switch"], OutputPolicy.Bitwise),
            Launch = static (b, s) => M1DiffSwitchKernels.LaunchSwitchNarrowCharEnumStatementKernel(
                b.Get<byte>("ba"), b.Get<sbyte>("sba"), b.Get<short>("sa"), b.Get<ushort>("usa"), b.Get<int>("dst"), s.Rows,
                gridDimX: s.GridX, blockDimX: s.BlockX),
            Oracle = static b => M1DiffSwitchKernels.SwitchNarrowCharEnumStatementKernel(
                b.Get<byte>("ba"), b.Get<sbyte>("sba"), b.Get<short>("sa"), b.Get<ushort>("usa"), b.Get<int>("dst"), Rows),
        };

        DifferentialRunner.Run(testCase);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SwitchGotoAndSharedLocalsStatement_MatchesTheOracleOnEveryPath()
    {
        var testCase = new DifferentialCase("M1DiffSwitchKernels.SwitchGotoAndSharedLocalsKernel")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Int32(rng, rows))
                .In("b", DiffValues.Int32(rng, rows))
                .Out<int>("dst", rows, ["goto_case_chain", "shared_section_locals", "char_goto_case", "user_goto_labels"], OutputPolicy.Bitwise),
            Launch = static (b, s) => M1DiffSwitchKernels.LaunchSwitchGotoAndSharedLocalsKernel(
                b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("dst"), s.Rows,
                gridDimX: s.GridX, blockDimX: s.BlockX),
            Oracle = static b => M1DiffSwitchKernels.SwitchGotoAndSharedLocalsKernel(
                b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("dst"), Rows),
        };

        DifferentialRunner.Run(testCase);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SwitchExpressionIntAndLong_MatchesTheOracleOnEveryPath()
    {
        var testCase = new DifferentialCase("M1DiffSwitchKernels.SwitchExpressionIntAndLongKernel")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) =>
            {
                var la = FillInt64(rng, rows);
                la[0] = long.MinValue;
                la[1] = -1L;
                la[2] = 0L;
                la[3] = 1L;
                la[4] = long.MaxValue;
                return new DiffBuffers()
                    .In("a", DiffValues.Int32(rng, rows))
                    .In("b", DiffValues.Int32(rng, rows))
                    .In("la", la)
                    .Out<long>("dst", rows, ["int_sw_expr", "nested_sw_expr", "long_sw_expr", "side_effect_gov"], OutputPolicy.Bitwise);
            },
            Launch = static (b, s) => M1DiffSwitchKernels.LaunchSwitchExpressionIntAndLongKernel(
                b.Get<int>("a"), b.Get<int>("b"), b.Get<long>("la"), b.Get<long>("dst"), s.Rows,
                gridDimX: s.GridX, blockDimX: s.BlockX),
            Oracle = static b => M1DiffSwitchKernels.SwitchExpressionIntAndLongKernel(
                b.Get<int>("a"), b.Get<int>("b"), b.Get<long>("la"), b.Get<long>("dst"), Rows),
        };

        DifferentialRunner.Run(testCase);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SwitchExpressionFloatSpecial_MatchesTheOracleOnEveryPath()
    {
        var testCase = new DifferentialCase("M1DiffSwitchKernels.SwitchExpressionFloatSpecialKernel")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("sel", DiffValues.Int32(rng, rows))
                .In("fa", DiffValues.Single(rng, rows))
                .Out<float>("dst", rows, ["special_floats", "nan_comparisons"], OutputPolicy.Bitwise),
            Launch = static (b, s) => M1DiffSwitchKernels.LaunchSwitchExpressionFloatSpecialKernel(
                b.Get<int>("sel"), b.Get<float>("fa"), b.Get<float>("dst"), s.Rows,
                gridDimX: s.GridX, blockDimX: s.BlockX),
            Oracle = static b => M1DiffSwitchKernels.SwitchExpressionFloatSpecialKernel(
                b.Get<int>("sel"), b.Get<float>("fa"), b.Get<float>("dst"), Rows),
        };

        DifferentialRunner.Run(testCase);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_TernaryNestedAndRef_MatchesTheOracleOnEveryPath()
    {
        var testCase = new DifferentialCase("M1DiffSwitchKernels.TernaryNestedAndRefKernel")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Int32(rng, rows))
                .In("b", DiffValues.Int32(rng, rows))
                .In("c", DiffValues.Int32(rng, rows))
                .Out<int>("dst", rows, ["nested_assoc", "short_circuit", "ref_ternary", "ref_ternary_lhs_first"], OutputPolicy.Bitwise),
            Launch = static (b, s) => M1DiffSwitchKernels.LaunchTernaryNestedAndRefKernel(
                b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("c"), b.Get<int>("dst"), s.Rows,
                gridDimX: s.GridX, blockDimX: s.BlockX),
            Oracle = static b => M1DiffSwitchKernels.TernaryNestedAndRefKernel(
                b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("c"), b.Get<int>("dst"), Rows),
        };

        DifferentialRunner.Run(testCase);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_TernaryTargetTypedAndNarrow_MatchesTheOracleOnEveryPath()
    {
        var testCase = new DifferentialCase("M1DiffSwitchKernels.TernaryTargetTypedAndNarrowKernel")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("conds", DiffValues.Int32(rng, rows))
                .In("ua", FillUInt32(rng, rows))
                .In("ia", DiffValues.Int32(rng, rows))
                .In("sa", FillInt16(rng, rows))
                .In("usa", FillUInt16(rng, rows))
                .In("ba", FillUInt8(rng, rows))
                .In("sba", FillInt8(rng, rows))
                .Out<long>("ldst", rows, ["uint_int_to_long", "uint_narrow_to_long", "narrow_mixed_to_int", "narrow_var_wrap"], OutputPolicy.Bitwise)
                .Out<double>("ddst", rows, ["uint_int_to_double", "nested_long_to_double"], OutputPolicy.Bitwise),
            Launch = static (b, s) => M1DiffSwitchKernels.LaunchTernaryTargetTypedAndNarrowKernel(
                b.Get<int>("conds"), b.Get<uint>("ua"), b.Get<int>("ia"),
                b.Get<short>("sa"), b.Get<ushort>("usa"), b.Get<byte>("ba"), b.Get<sbyte>("sba"),
                b.Get<long>("ldst"), b.Get<double>("ddst"), s.Rows,
                gridDimX: s.GridX, blockDimX: s.BlockX),
            Oracle = static b => M1DiffSwitchKernels.TernaryTargetTypedAndNarrowKernel(
                b.Get<int>("conds"), b.Get<uint>("ua"), b.Get<int>("ia"),
                b.Get<short>("sa"), b.Get<ushort>("usa"), b.Get<byte>("ba"), b.Get<sbyte>("sba"),
                b.Get<long>("ldst"), b.Get<double>("ddst"), Rows),
        };

        DifferentialRunner.Run(testCase);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullCoalesceAndNullCheckMatching_MatchesTheOracleOnEveryPath()
    {
        var testCase = new DifferentialCase("M1DiffSwitchKernels.NullCoalesceAndNullCheckMatchingKernel")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Int32(rng, rows))
                .In("b", DiffValues.Int32(rng, rows))
                .Out<int>("dst", rows, ["coalesce_short_circuit", "coalesce_chain", "coalesce_assign", "array_span_null_checks"], OutputPolicy.Bitwise),
            Launch = static (b, s) => M1DiffSwitchKernels.LaunchNullCoalesceAndNullCheckMatchingKernel(
                b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("dst"), s.Rows,
                gridDimX: s.GridX, blockDimX: s.BlockX),
            Oracle = static b => M1DiffSwitchKernels.NullCoalesceAndNullCheckMatchingKernel(
                b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("dst"), Rows),
        };

        DifferentialRunner.Run(testCase);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SwitchFlattenedFloatAndGoto_MatchesTheReferenceOnEveryPath()
    {
        var testCase = new DifferentialCase("M1DiffSwitchKernels.SwitchFlattenedFloatAndGotoKernel")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Int32(rng, rows))
                .In("fa", DiffValues.Single(rng, rows))
                .Out<int>("dst", rows, ["flattened_float_switch", "flattened_int_shared_local_switch"], OutputPolicy.Bitwise),
            Launch = static (b, s) => M1DiffSwitchKernels.LaunchSwitchFlattenedFloatAndGotoKernel(
                b.Get<int>("a"), b.Get<float>("fa"), b.Get<int>("dst"), s.Rows,
                gridDimX: s.GridX, blockDimX: s.BlockX),
            Reference = static b =>
            {
                var a = b.Get<int>("a");
                var fa = b.Get<float>("fa");
                var dst = b.Get<int>("dst");
                for (int t = 0; t < Rows; t++)
                {
                    int v = a[t];
                    float fv = (v & 7) switch
                    {
                        0 => float.NaN,
                        1 => 0.0f,
                        2 => -0.0f,
                        3 => float.PositiveInfinity,
                        4 => float.NegativeInfinity,
                        5 => 1.5f,
                        6 => -2.5f,
                        _ => fa[t]
                    };

                    long fTag = (fv != fv) ? long.MinValue
                        : (fv == 0.0f ? 0L
                        : (fv == float.PositiveInfinity ? long.MaxValue
                        : (fv == float.NegativeInfinity ? -5_000_000_000L
                        : (fv == 1.5f ? 15L
                        : (fv == -2.5f ? -25L : 7L)))));

                    int fRes = 0;
                    switch (fTag)
                    {
                        case long.MinValue:
                            fRes = 900;
                            break;
                        case 0L:
                            fRes = 100;
                            break;
                        case long.MaxValue:
                            fRes = 200;
                            goto case 15L;
                        case -5_000_000_000L:
                            fRes = 300;
                            goto default;
                        case 15L:
                            fRes += 15;
                            break;
                        case -25L:
                            fRes = -25;
                            break;
                        default:
                            fRes += 7;
                            break;
                    }

                    int iRes = 0;
                    switch (v & 3)
                    {
                        case 0:
                            const int FlatBase = 40;
                            int sharedAcrossFlat = FlatBase + (v & 0xF);
                            iRes = sharedAcrossFlat;
                            break;
                        case 1:
                            sharedAcrossFlat = FlatBase * 2 + (v & 0xF);
                            iRes = sharedAcrossFlat;
                            goto case 2;
                        case 2:
                            iRes += FlatBase;
                            break;
                        default:
                            iRes = -FlatBase;
                            break;
                    }

                    int col = t * 2;
                    dst[col + 0] = fRes;
                    dst[col + 1] = iRes;
                }
            },
        };

        DifferentialRunner.Run(testCase);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_EmptyStackSpanNullComparison_MatchesTheOracleOnEveryPath()
    {
        var testCase = new DifferentialCase("M1DiffSwitchKernels.EmptyStackSpanNullComparisonReproKernel")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Int32(rng, rows))
                .Out<int>("dst", rows, ["stackalloc_zero_eq_null", "stackalloc_zero_ne_null", "collection_empty_eq_null", "combined_null_flags"], OutputPolicy.Bitwise),
            Launch = static (b, s) => M1DiffSwitchKernels.LaunchEmptyStackSpanNullComparisonReproKernel(
                b.Get<int>("a"), b.Get<int>("dst"), s.Rows,
                gridDimX: s.GridX, blockDimX: s.BlockX),
            Oracle = static b => M1DiffSwitchKernels.EmptyStackSpanNullComparisonReproKernel(
                b.Get<int>("a"), b.Get<int>("dst"), Rows),
        };

        DifferentialRunner.Run(testCase);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullableArrayLikeDefaultValueNullCheck_MatchesTheOracleOnEveryPath()
    {
        var testCase = new DifferentialCase("M1DiffSwitchKernels.NullableArrayLikeDefaultValueNullCheckReproKernel")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Int32(rng, rows))
                .Out<int>("dst", rows, ["mem_is_null", "mem_is_not_null", "mem_eq_ne_hasvalue", "seg_is_null_hasvalue"], OutputPolicy.Bitwise),
            Launch = static (b, s) => M1DiffSwitchKernels.LaunchNullableArrayLikeDefaultValueNullCheckReproKernel(
                b.Get<int>("a"), b.Get<int>("dst"), s.Rows,
                gridDimX: s.GridX, blockDimX: s.BlockX),
            Oracle = static b => M1DiffSwitchKernels.NullableArrayLikeDefaultValueNullCheckReproKernel(
                b.Get<int>("a"), b.Get<int>("dst"), Rows),
        };

        DifferentialRunner.Run(testCase);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_RefTernaryParameterWriteAcrossBarrier_MatchesTheReferenceOnEveryPath()
    {
        var testCase = new DifferentialCase("M1DiffSwitchKernels.RefTernaryParameterWriteAcrossBarrierReproKernel")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("conds", DiffValues.Int32(rng, rows))
                .Out<int>("dst", rows, ["ref_ternary_param_assign", "ref_ternary_param_compound", "ref_ternary_param_inc", "ref_ternary_local_fallback"], OutputPolicy.Bitwise),
            Launch = static (b, s) => M1DiffSwitchKernels.LaunchRefTernaryParameterWriteAcrossBarrierReproKernel(
                b.Get<int>("conds"), b.Get<int>("dst"), 50, 60, 70, s.Rows,
                gridDimX: s.GridX, blockDimX: s.BlockX),
            Reference = static b =>
            {
                var conds = b.Get<int>("conds");
                var dst = b.Get<int>("dst");
                for (int t = 0; t < Rows; t++)
                {
                    int c = conds[t];
                    int paramAssign = 50;
                    int paramCompound = 60;
                    int paramInc = 70;
                    int localFallback = 10;

                    ((c & 1) == 0 ? ref paramAssign : ref localFallback) = 777;
                    ((c & 2) == 0 ? ref paramCompound : ref localFallback) += 100;
                    ++((c & 4) == 0 ? ref paramInc : ref localFallback);

                    int col = t * 4;
                    dst[col + 0] = paramAssign;
                    dst[col + 1] = paramCompound;
                    dst[col + 2] = paramInc;
                    dst[col + 3] = localFallback;
                }
            },
        };

        DifferentialRunner.Run(testCase);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NarrowNullableCoalesceVarPromotion_MatchesTheOracleOnEveryPath()
    {
        var testCase = new DifferentialCase("M1DiffSwitchKernels.NarrowNullableCoalesceVarPromotionReproKernel")
        {
            Shape = s_shape,
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("conds", DiffValues.Int32(rng, rows))
                .In("ba", FillUInt8(rng, rows))
                .In("usa", FillUInt16(rng, rows))
                .Out<int>("dst", rows, ["byte_nullable_coalesce_inc", "ushort_nullable_coalesce_inc", "byte_nullable_ushort_coalesce_inc"], OutputPolicy.Bitwise),
            Launch = static (b, s) => M1DiffSwitchKernels.LaunchNarrowNullableCoalesceVarPromotionReproKernel(
                b.Get<int>("conds"), b.Get<byte>("ba"), b.Get<ushort>("usa"), b.Get<int>("dst"), s.Rows,
                gridDimX: s.GridX, blockDimX: s.BlockX),
            Oracle = static b => M1DiffSwitchKernels.NarrowNullableCoalesceVarPromotionReproKernel(
                b.Get<int>("conds"), b.Get<byte>("ba"), b.Get<ushort>("usa"), b.Get<int>("dst"), Rows),
        };

        DifferentialRunner.Run(testCase);
    }
}
