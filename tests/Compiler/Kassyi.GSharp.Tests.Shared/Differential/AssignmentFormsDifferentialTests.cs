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

    internal static readonly DifferentialCase s_floatRemainder = new(
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
            operands: new FloatRemainderOperands
            {
                SingleDividends = b.Get<float>("a"),
                SingleDivisors = b.Get<float>("b"),
                DoubleDividends = b.Get<double>("c"),
                DoubleDivisors = b.Get<double>("d"),
            },
            results: new FloatRemainderResults
            {
                SingleRemainders = b.Get<float>("rf"),
                DoubleRemainders = b.Get<double>("rd"),
            },
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_nullableConversion = DiffCases.Create<int, int, float, int>(
        AssignmentFormsDifferentialKernels.LaunchNullableConversionKernel,
        inputs: static (rng, rows) =>
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
        shape: LaunchShape.ForRows(Rows),
        firstInputName: "has",
        secondInputName: "values",
        thirdInputName: "fvalues");

    internal static readonly DifferentialCase s_nullArray = DiffCases.Create(
        AssignmentFormsDifferentialKernels.LaunchNullArrayKernel,
        inputs: static (rng, rows) =>
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
        shape: LaunchShape.ForRows(Rows),
        firstInputName: "has",
        secondInputName: "a",
        outputName: "r");

    internal static readonly DifferentialCase s_discard = DiffCases.Create(
        AssignmentFormsDifferentialKernels.LaunchDiscardKernel,
        inputs: static (rng, rows) =>
        {
            var (a, b) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("a", a)
                .In("b", b)
                .Out<int>("r", rows, AssignmentFormsDifferentialKernels.DiscardColumns, OutputPolicy.Exact);
        },
        shape: LaunchShape.ForRows(Rows),
        outputName: "r");

    internal static readonly DifferentialCase s_computedProperty = new(
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
            operands: new ComputedPropertyOperands
            {
                FirstInt32Values = b.Get<int>("a"),
                SecondInt32Values = b.Get<int>("b"),
                SingleValues = b.Get<float>("f"),
            },
            results: new ComputedPropertyResults
            {
                Int32Results = b.Get<int>("r"),
                SingleResults = b.Get<float>("rf"),
            },
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_deconstruction = DiffCases.Create(
        AssignmentFormsDifferentialKernels.LaunchDeconstructionShapesKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("values", DiffValues.Int32(rng, rows * 3))
            .Out<int>("r", rows, AssignmentFormsDifferentialKernels.DeconstructionColumns, OutputPolicy.Exact),
        shape: LaunchShape.ForRows(Rows),
        inputName: "values",
        outputName: "r");

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Remainder_FloatOperands_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_floatRemainder);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Conversion_NullableTypes_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_nullableConversion);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Assignment_NullArray_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_nullArray);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.HappyPath)]
    public void Assignment_DiscardPattern_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_discard);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Assignment_ComputedProperty_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_computedProperty);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Deconstruction_VariousShapes_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_deconstruction);
}
