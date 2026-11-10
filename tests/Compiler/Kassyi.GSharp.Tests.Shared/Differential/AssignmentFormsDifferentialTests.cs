#nullable enable
using System;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests of the assignment and conversion forms of <see cref="AssignmentFormsDifferentialKernels"/>: the
/// floating-point remainder (#727), <c>Nullable&lt;T&gt;</c> casts and <c>GetValueOrDefault</c> (#1143), discards
/// (#1289), <c>null</c> converted to an array, computed property assignments (#908) and nested and conditional
/// deconstructions (#1102).
/// </summary>
public class AssignmentFormsDifferentialTests
{
    // Seed 0 gives the cross product of the 32 single boundaries (NaN, both infinities, both zeros, subnormal, max)
    private const int RemainderRows = 1024;
    private const int Rows = 256;

    // The double boundaries: the single ones widened, and the values only a double holds
    private static readonly double[] s_doubleBoundaries =
    [
        .. Array.ConvertAll(DiffValues.SingleBoundaries, static v => (double)v),
        double.Epsilon,
        -double.Epsilon,
        double.MaxValue,
        double.MinValue,
        1e300,
        -1e-300,
        0.1,
        -7.25,
    ];

    private static double NextDouble(DiffRandom rng) =>
        rng.NextBool()
            ? s_doubleBoundaries[rng.NextBelow(s_doubleBoundaries.Length)]
            : BitConverter.UInt64BitsToDouble(rng.NextUInt64());

    private static readonly DifferentialCase s_floatRemainder = new(
        $"{nameof(AssignmentFormsDifferentialKernels)}.{nameof(AssignmentFormsDifferentialKernels.FloatRemainderKernel)}")
    {
        Shape = LaunchShape.ForRows(RemainderRows),
        Inputs = static (rng, rows) =>
        {
            var (a, b) = DiffValues.Pairs(rng, rows, DiffValues.SingleBoundaries, DiffValues.NextSingle);
            var (c, d) = DiffValues.Pairs(rng, rows, s_doubleBoundaries, NextDouble);
            return new DiffBuffers()
                .In("a", a)
                .In("b", b)
                .In("c", c)
                .In("d", d)
                .Out<float>("rf", rows, AssignmentFormsDifferentialKernels.FloatRemainderSingleColumns,
                    OutputPolicy.Bitwise)
                .Out<double>("rd", rows, AssignmentFormsDifferentialKernels.FloatRemainderDoubleColumns,
                    OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => AssignmentFormsDifferentialKernels.LaunchFloatRemainderKernel(
            b.Get<float>("a"),
            b.Get<float>("b"),
            b.Get<double>("c"),
            b.Get<double>("d"),
            b.Get<float>("rf"),
            b.Get<double>("rd"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => AssignmentFormsDifferentialKernels.FloatRemainderKernel(
            b.Get<float>("a"),
            b.Get<float>("b"),
            b.Get<double>("c"),
            b.Get<double>("d"),
            b.Get<float>("rf"),
            b.Get<double>("rd")),
    };

    private static readonly DifferentialCase s_nullableConversion = new(
        $"{nameof(AssignmentFormsDifferentialKernels)}.{nameof(AssignmentFormsDifferentialKernels.NullableConversionKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows),
        Inputs = static (rng, rows) =>
        {
            var values = DiffValues.Int32(rng, rows);
            var fvalues = DiffValues.Single(rng, rows);
            var has = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                has[k] = rng.IsBoundarySeed ? (k / 7) & 1 : (rng.NextBool() ? 1 : 0);
            }

            return new DiffBuffers()
                .In("has", has)
                .In("values", values)
                .In("fvalues", fvalues)
                .Out<int>("dst", rows, AssignmentFormsDifferentialKernels.NullableConversionColumns,
                    OutputPolicy.Exact);
        },
        Launch = static (b, s) => AssignmentFormsDifferentialKernels.LaunchNullableConversionKernel(
            b.Get<int>("has"),
            b.Get<int>("values"),
            b.Get<float>("fvalues"),
            b.Get<int>("dst"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => AssignmentFormsDifferentialKernels.NullableConversionKernel(
            b.Get<int>("has"),
            b.Get<int>("values"),
            b.Get<float>("fvalues"),
            b.Get<int>("dst")),
    };

    private static readonly DifferentialCase s_nullArray = new(
        $"{nameof(AssignmentFormsDifferentialKernels)}.{nameof(AssignmentFormsDifferentialKernels.NullArrayKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows),
        Inputs = static (rng, rows) =>
        {
            var has = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                has[k] = rng.IsBoundarySeed ? k & 1 : (rng.NextBool() ? 1 : 0);
            }

            return new DiffBuffers()
                .In("has", has)
                .In("a", DiffValues.Int32(rng, rows))
                .Out<int>("r", rows, AssignmentFormsDifferentialKernels.NullArrayColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => AssignmentFormsDifferentialKernels.LaunchNullArrayKernel(
            b.Get<int>("has"),
            b.Get<int>("a"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => AssignmentFormsDifferentialKernels.NullArrayKernel(
            b.Get<int>("has"),
            b.Get<int>("a"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_discard = new(
        $"{nameof(AssignmentFormsDifferentialKernels)}.{nameof(AssignmentFormsDifferentialKernels.DiscardKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows),
        Inputs = static (rng, rows) =>
        {
            var (a, b) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("a", a)
                .In("b", b)
                .Out<int>("r", rows, AssignmentFormsDifferentialKernels.DiscardColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => AssignmentFormsDifferentialKernels.LaunchDiscardKernel(
            b.Get<int>("a"),
            b.Get<int>("b"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => AssignmentFormsDifferentialKernels.DiscardKernel(
            b.Get<int>("a"),
            b.Get<int>("b"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_computedProperty = new(
        $"{nameof(AssignmentFormsDifferentialKernels)}.{nameof(AssignmentFormsDifferentialKernels.ComputedPropertyKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows),
        Inputs = static (rng, rows) =>
        {
            var (a, b) = DiffValues.Int32Pairs(rng, rows);
            var f = DiffValues.Single(rng, rows);
            return new DiffBuffers()
                .In("a", a)
                .In("b", b)
                .In("f", f)
                .Out<int>("r", rows, AssignmentFormsDifferentialKernels.ComputedPropertyColumns, OutputPolicy.Exact)
                .Out<float>("rf", rows, AssignmentFormsDifferentialKernels.ComputedPropertySingleColumns,
                    OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => AssignmentFormsDifferentialKernels.LaunchComputedPropertyKernel(
            b.Get<int>("a"),
            b.Get<int>("b"),
            b.Get<float>("f"),
            b.Get<int>("r"),
            b.Get<float>("rf"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => AssignmentFormsDifferentialKernels.ComputedPropertyKernel(
            b.Get<int>("a"),
            b.Get<int>("b"),
            b.Get<float>("f"),
            b.Get<int>("r"),
            b.Get<float>("rf")),
    };

    private static readonly DifferentialCase s_deconstruction = new(
        $"{nameof(AssignmentFormsDifferentialKernels)}.{nameof(AssignmentFormsDifferentialKernels.DeconstructionShapesKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("values", DiffValues.Int32(rng, rows * 3))
            .Out<int>("r", rows, AssignmentFormsDifferentialKernels.DeconstructionColumns, OutputPolicy.Exact),
        Launch = static (b, s) => AssignmentFormsDifferentialKernels.LaunchDeconstructionShapesKernel(
            b.Get<int>("values"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => AssignmentFormsDifferentialKernels.DeconstructionShapesKernel(
            b.Get<int>("values"),
            b.Get<int>("r")),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_FloatRemainder_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_floatRemainder);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullableConversion_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_nullableConversion);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullArray_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_nullArray);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Discard_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_discard);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_PropertyAccessorAssignment_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_computedProperty);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DeconstructionShapes_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_deconstruction);
}
