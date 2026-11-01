#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests (<c>Nullable&lt;T&gt;</c> / <c>T?</c> representation, <c>HasValue</c>,
/// <c>Value</c>, null-coalescing <c>??</c>, compound <c>??=</c>, and mixed-sign promotion).
/// </summary>
public class M1DiffNullableTests
{
    private const int Rows = 441;

    private static readonly DifferentialCase s_coalesceAndSideEffects = new(
        $"{nameof(M1DiffNullableKernels)}.{nameof(M1DiffNullableKernels.NullableCoalesceAndSideEffects)}")
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
                .Out<int>("r", rows, M1DiffNullableKernels.CoalesceAndSideEffectsColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => M1DiffNullableKernels.LaunchNullableCoalesceAndSideEffects(
            b.Get<int>("hasA"),
            b.Get<int>("valsA"),
            b.Get<int>("hasB"),
            b.Get<int>("valsB"),
            b.Get<int>("fallbacks"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => M1DiffNullableKernels.NullableCoalesceAndSideEffects(
            b.Get<int>("hasA"),
            b.Get<int>("valsA"),
            b.Get<int>("hasB"),
            b.Get<int>("valsB"),
            b.Get<int>("fallbacks"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_deviceFunctionsAndKleene = new(
        $"{nameof(M1DiffNullableKernels)}.{nameof(M1DiffNullableKernels.NullableDeviceFunctionsAndKleeneLogic)}")
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
                .Out<int>("r", rows, M1DiffNullableKernels.DeviceFunctionsAndKleeneColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => M1DiffNullableKernels.LaunchNullableDeviceFunctionsAndKleeneLogic(
            b.Get<int>("hasA"),
            b.Get<int>("valsA"),
            b.Get<int>("hasB"),
            b.Get<int>("valsB"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => M1DiffNullableKernels.NullableDeviceFunctionsAndKleeneLogic(
            b.Get<int>("hasA"),
            b.Get<int>("valsA"),
            b.Get<int>("hasB"),
            b.Get<int>("valsB"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_floatCoalesceWithNaN = new(
        $"{nameof(M1DiffNullableKernels)}.{nameof(M1DiffNullableKernels.NullableFloatCoalesceWithNaN)}")
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
                .Out<float>("r", rows, M1DiffNullableKernels.FloatCoalesceWithNaNColumns, OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => M1DiffNullableKernels.LaunchNullableFloatCoalesceWithNaN(
            b.Get<int>("hasA"),
            b.Get<float>("valsA"),
            b.Get<float>("fallbacks"),
            b.Get<float>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => M1DiffNullableKernels.NullableFloatCoalesceWithNaN(
            b.Get<int>("hasA"),
            b.Get<float>("valsA"),
            b.Get<float>("fallbacks"),
            b.Get<float>("r")),
    };

    private static readonly DifferentialCase s_coalesceWiderWithNarrower = new(
        $"{nameof(M1DiffNullableKernels)}.{nameof(M1DiffNullableKernels.CoalesceWiderNullableWithNarrowerNullable)}")
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
                .Out<int>("r", rows, M1DiffNullableKernels.CoalesceWiderWithNarrowerColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => M1DiffNullableKernels.LaunchCoalesceWiderNullableWithNarrowerNullable(
            b.Get<int>("hasA"),
            b.Get<int>("valsA"),
            b.Get<int>("hasB"),
            b.Get<short>("valsB"),
            b.Get<ushort>("uValsB"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static b => M1DiffNullableKernels.CoalesceWiderNullableWithNarrowerNullable(
            b.Get<int>("hasA"),
            b.Get<int>("valsA"),
            b.Get<int>("hasB"),
            b.Get<short>("valsB"),
            b.Get<ushort>("uValsB"),
            b.Get<int>("r")),
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
}
