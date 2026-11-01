#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <see cref="SwitchDifferentialKernels"/>: <c>switch</c> statements and expressions,
/// <c>goto case</c> / <c>goto default</c>, ternary <c>?:</c> and null-coalescing <c>??</c> / <c>??=</c>.
/// </summary>
public sealed class SwitchDifferentialTests
{
    private const int Rows = 64;
    private static readonly LaunchShape s_shape = LaunchShape.ForRows(Rows, blockX: 32);

    private static readonly DifferentialCase s_switchIntStatement = new(
        $"{nameof(SwitchDifferentialKernels)}.{nameof(SwitchDifferentialKernels.SwitchIntStatementKernel)}")
    {
        Shape = s_shape,
        Inputs = static (rng, rows) =>
        {
            var a = DiffValues.Int32(rng, rows);
            a[0] = int.MinValue;
            a[1] = SwitchDifferentialKernels.ConstNegCase;
            a[2] = -1;
            a[3] = 0;
            a[4] = 1;
            a[5] = 2;
            a[6] = 3;
            a[7] = SwitchDifferentialKernels.ConstPosCase;
            a[8] = int.MaxValue;
            var b = DiffValues.Int32(rng, rows);
            return new DiffBuffers()
                .In("a", a)
                .In("b", b)
                .Out<int>("dst", rows, ["standard", "no_default", "nested", "bool_switch"], OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => SwitchDifferentialKernels.LaunchSwitchIntStatementKernel(
            b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static b => SwitchDifferentialKernels.SwitchIntStatementKernel(
            b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("dst"), Rows),
    };

    private static readonly DifferentialCase s_switchWideInt64Statement = new(
        $"{nameof(SwitchDifferentialKernels)}.{nameof(SwitchDifferentialKernels.SwitchWideInt64StatementKernel)}")
    {
        Shape = s_shape,
        Inputs = static (rng, rows) =>
        {
            var a = DiffValues.Random(rng, rows, static r => unchecked((long)r.NextUInt64()));
            a[0] = long.MinValue;
            a[1] = -5000000000L;
            a[2] = -1L;
            a[3] = 0L;
            a[4] = 5000000000L;
            a[5] = long.MaxValue;

            var ua = DiffValues.Random(rng, rows, static r => r.NextUInt64());
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
        Launch = static (b, s) => SwitchDifferentialKernels.LaunchSwitchWideInt64StatementKernel(
            b.Get<long>("a"), b.Get<ulong>("ua"), b.Get<long>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static b => SwitchDifferentialKernels.SwitchWideInt64StatementKernel(
            b.Get<long>("a"), b.Get<ulong>("ua"), b.Get<long>("dst"), Rows),
    };

    private static readonly DifferentialCase s_switchNarrowCharEnumStatement = new(
        $"{nameof(SwitchDifferentialKernels)}.{nameof(SwitchDifferentialKernels.SwitchNarrowCharEnumStatementKernel)}")
    {
        Shape = s_shape,
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("ba", DiffValues.Byte(rng, rows))
            .In("sba", DiffValues.SByte(rng, rows))
            .In("sa", DiffValues.Int16(rng, rows))
            .In("usa", DiffValues.UInt16(rng, rows))
            .Out<int>("dst", rows, ["byte_sbyte", "short_ushort", "char_switch", "enum_switch"], OutputPolicy.Bitwise),
        Launch = static (b, s) => SwitchDifferentialKernels.LaunchSwitchNarrowCharEnumStatementKernel(
            b.Get<byte>("ba"), b.Get<sbyte>("sba"), b.Get<short>("sa"), b.Get<ushort>("usa"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static b => SwitchDifferentialKernels.SwitchNarrowCharEnumStatementKernel(
            b.Get<byte>("ba"), b.Get<sbyte>("sba"), b.Get<short>("sa"), b.Get<ushort>("usa"), b.Get<int>("dst"), Rows),
    };

    private static readonly DifferentialCase s_switchGotoAndSharedLocals = new(
        $"{nameof(SwitchDifferentialKernels)}.{nameof(SwitchDifferentialKernels.SwitchGotoAndSharedLocalsKernel)}")
    {
        Shape = s_shape,
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .In("b", DiffValues.Int32(rng, rows))
            .Out<int>("dst", rows, ["goto_case_chain", "shared_section_locals", "char_goto_case", "user_goto_labels"], OutputPolicy.Bitwise),
        Launch = static (b, s) => SwitchDifferentialKernels.LaunchSwitchGotoAndSharedLocalsKernel(
            b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static b => SwitchDifferentialKernels.SwitchGotoAndSharedLocalsKernel(
            b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("dst"), Rows),
    };

    private static readonly DifferentialCase s_switchExpressionIntAndLong = new(
        $"{nameof(SwitchDifferentialKernels)}.{nameof(SwitchDifferentialKernels.SwitchExpressionIntAndLongKernel)}")
    {
        Shape = s_shape,
        Inputs = static (rng, rows) =>
        {
            var la = DiffValues.Random(rng, rows, static r => unchecked((long)r.NextUInt64()));
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
        Launch = static (b, s) => SwitchDifferentialKernels.LaunchSwitchExpressionIntAndLongKernel(
            b.Get<int>("a"), b.Get<int>("b"), b.Get<long>("la"), b.Get<long>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static b => SwitchDifferentialKernels.SwitchExpressionIntAndLongKernel(
            b.Get<int>("a"), b.Get<int>("b"), b.Get<long>("la"), b.Get<long>("dst"), Rows),
    };

    private static readonly DifferentialCase s_switchExpressionFloatSpecial = new(
        $"{nameof(SwitchDifferentialKernels)}.{nameof(SwitchDifferentialKernels.SwitchExpressionFloatSpecialKernel)}")
    {
        Shape = s_shape,
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("sel", DiffValues.Int32(rng, rows))
            .In("fa", DiffValues.Single(rng, rows))
            .Out<float>("dst", rows, ["special_floats", "nan_comparisons"], OutputPolicy.Bitwise),
        Launch = static (b, s) => SwitchDifferentialKernels.LaunchSwitchExpressionFloatSpecialKernel(
            b.Get<int>("sel"), b.Get<float>("fa"), b.Get<float>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static b => SwitchDifferentialKernels.SwitchExpressionFloatSpecialKernel(
            b.Get<int>("sel"), b.Get<float>("fa"), b.Get<float>("dst"), Rows),
    };

    private static readonly DifferentialCase s_ternaryNestedAndRef = new(
        $"{nameof(SwitchDifferentialKernels)}.{nameof(SwitchDifferentialKernels.TernaryNestedAndRefKernel)}")
    {
        Shape = s_shape,
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .In("b", DiffValues.Int32(rng, rows))
            .In("c", DiffValues.Int32(rng, rows))
            .Out<int>("dst", rows, ["nested_assoc", "short_circuit", "ref_ternary", "ref_ternary_lhs_first"], OutputPolicy.Bitwise),
        Launch = static (b, s) => SwitchDifferentialKernels.LaunchTernaryNestedAndRefKernel(
            b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("c"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static b => SwitchDifferentialKernels.TernaryNestedAndRefKernel(
            b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("c"), b.Get<int>("dst"), Rows),
    };

    private static readonly DifferentialCase s_ternaryTargetTypedAndNarrow = new(
        $"{nameof(SwitchDifferentialKernels)}.{nameof(SwitchDifferentialKernels.TernaryTargetTypedAndNarrowKernel)}")
    {
        Shape = s_shape,
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("conds", DiffValues.Int32(rng, rows))
            .In("ua", DiffValues.Random(rng, rows, static r => unchecked((uint)DiffValues.NextInt32(r))))
            .In("ia", DiffValues.Int32(rng, rows))
            .In("sa", DiffValues.Int16(rng, rows))
            .In("usa", DiffValues.UInt16(rng, rows))
            .In("ba", DiffValues.Byte(rng, rows))
            .In("sba", DiffValues.SByte(rng, rows))
            .Out<long>("ldst", rows, ["uint_int_to_long", "uint_narrow_to_long", "narrow_mixed_to_int", "narrow_var_wrap"], OutputPolicy.Bitwise)
            .Out<double>("ddst", rows, ["uint_int_to_double", "nested_long_to_double"], OutputPolicy.Bitwise),
        Launch = static (b, s) => SwitchDifferentialKernels.LaunchTernaryTargetTypedAndNarrowKernel(
            b.Get<int>("conds"), b.Get<uint>("ua"), b.Get<int>("ia"),
            b.Get<short>("sa"), b.Get<ushort>("usa"), b.Get<byte>("ba"), b.Get<sbyte>("sba"),
            b.Get<long>("ldst"), b.Get<double>("ddst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static b => SwitchDifferentialKernels.TernaryTargetTypedAndNarrowKernel(
            b.Get<int>("conds"), b.Get<uint>("ua"), b.Get<int>("ia"),
            b.Get<short>("sa"), b.Get<ushort>("usa"), b.Get<byte>("ba"), b.Get<sbyte>("sba"),
            b.Get<long>("ldst"), b.Get<double>("ddst"), Rows),
    };

    private static readonly DifferentialCase s_nullCoalesceAndNullCheckMatching = new(
        $"{nameof(SwitchDifferentialKernels)}.{nameof(SwitchDifferentialKernels.NullCoalesceAndNullCheckMatchingKernel)}")
    {
        Shape = s_shape,
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .In("b", DiffValues.Int32(rng, rows))
            .Out<int>("dst", rows, ["coalesce_short_circuit", "coalesce_chain", "coalesce_assign", "array_span_null_checks"], OutputPolicy.Bitwise),
        Launch = static (b, s) => SwitchDifferentialKernels.LaunchNullCoalesceAndNullCheckMatchingKernel(
            b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static b => SwitchDifferentialKernels.NullCoalesceAndNullCheckMatchingKernel(
            b.Get<int>("a"), b.Get<int>("b"), b.Get<int>("dst"), Rows),
    };

    // The kernel calls Gpu.SyncThreads() inside a switch section, which the sequential oracle cannot run; the reference
    // is the same computation without the barriers
    private static readonly DifferentialCase s_switchFlattenedFloatAndGoto = new(
        $"{nameof(SwitchDifferentialKernels)}.{nameof(SwitchDifferentialKernels.SwitchFlattenedFloatAndGotoKernel)}")
    {
        Shape = s_shape,
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("a", DiffValues.Int32(rng, rows))
            .In("fa", DiffValues.Single(rng, rows))
            .Out<int>("dst", rows, ["flattened_float_switch", "flattened_int_shared_local_switch"], OutputPolicy.Bitwise),
        Launch = static (b, s) => SwitchDifferentialKernels.LaunchSwitchFlattenedFloatAndGotoKernel(
            b.Get<int>("a"), b.Get<float>("fa"), b.Get<int>("dst"), s.Rows,
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Reference = static b => FlattenedFloatAndGotoReference(b.Get<int>("a"), b.Get<float>("fa"), b.Get<int>("dst")),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SwitchIntStatement_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_switchIntStatement);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SwitchWideInt64Statement_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_switchWideInt64Statement);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SwitchNarrowCharEnumStatement_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_switchNarrowCharEnumStatement);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SwitchGotoAndSharedLocalsStatement_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_switchGotoAndSharedLocals);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SwitchExpressionIntAndLong_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_switchExpressionIntAndLong);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SwitchExpressionFloatSpecial_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_switchExpressionFloatSpecial);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_TernaryNestedAndRef_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_ternaryNestedAndRef);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_TernaryTargetTypedAndNarrow_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_ternaryTargetTypedAndNarrow);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullCoalesceAndNullCheckMatching_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullCoalesceAndNullCheckMatching);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SwitchFlattenedFloatAndGoto_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_switchFlattenedFloatAndGoto);

    private static void FlattenedFloatAndGotoReference(int[] a, float[] fa, int[] dst)
    {
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
    }
}
