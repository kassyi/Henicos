#nullable enable
using System.Numerics;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <c>StackLinq</c> <see cref="float"/> and <see cref="double"/> <c>Sum</c> overloads (#730).
/// Every path gives the result of System.Linq's Sum: the elements are added in sequence order, and a float sum adds them
/// in a double accumulator, bitwise even on ill-conditioned sequences where SIMD lane summation or a float accumulator
/// rounds differently.
/// </summary>
public class StackLinqSumDifferentialTests
{
    private const int Rows = 4;

    private static readonly DifferentialCase s_floatSum = new("StackLinqSumDifferentialKernels.FloatSumMatrixKernel")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 4),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("data", BuildFloatInput(rng))
            .Out<float>("r", rows, StackLinqSumDifferentialKernels.Columns, OutputPolicy.Bitwise),
        Launch = static (b, s) => StackLinqSumDifferentialKernels.LaunchFloatSumMatrixKernel(
            b.Get<float>("data"),
            b.Get<float>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Reference = static (b, _) => FillReference(b, (float)System.Linq.Enumerable.Sum(b.Get<float>("data"))),
    };

    private static readonly DifferentialCase s_doubleSum = new("StackLinqSumDifferentialKernels.DoubleSumMatrixKernel")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 4),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("data", BuildDoubleInput(rng))
            .Out<double>("r", rows, StackLinqSumDifferentialKernels.Columns, OutputPolicy.Bitwise),
        Launch = static (b, s) => StackLinqSumDifferentialKernels.LaunchDoubleSumMatrixKernel(
            b.Get<double>("data"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Reference = static (b, _) => FillReference(b, System.Linq.Enumerable.Sum(b.Get<double>("data"))),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_FloatSum_MatchesTheOracleOnEveryPath()
    {
        // Every form of StackLinq float Sum (direct, selector, Select, Where, Take, static call, and [GpuDeviceFunction]
        // direct and transitive) sums elements in index order from 0 to Length - 1, matching the GPU on ill-conditioned
        // inputs where SIMD vector reduction or a float accumulator rounds away small terms (8.0f vs 1.0f or 0.0f).
        // mutant: StackLinq.cuh __gsharp_linq_sum_accumulator<float> keeps float (the native columns fail with 1 vs 8)
        Assert.NotEmpty(DiffPaths.Available);
        DifferentialRunner.Run(s_floatSum);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DoubleSum_MatchesTheOracleOnEveryPath()
    {
        // Every form of StackLinq double Sum sums elements in index order from 0 to Length - 1, matching the GPU on
        // ill-conditioned inputs where SIMD vector reduction rounds away small terms (1.0 vs 0.0).
        // mutant: StackLinqExtensions.Sum.cs double Sum sums a contiguous source with SumVectorized again (the managed columns fail)
        Assert.NotEmpty(DiffPaths.Available);
        DifferentialRunner.Run(s_doubleSum);
    }

    // Every column holds the sum, except the last: a device function that adds two sums
    private static void FillReference<T>(DiffBuffers b, T sum) where T : unmanaged, INumberBase<T>
    {
        var r = b.Get<T>("r");
        int cols = StackLinqSumDifferentialKernels.Columns.Length;
        for (int t = 0; t < Rows; t++)
        {
            int o = t * cols;
            r.AsSpan(o, cols - 1).Fill(sum);
            r[o + cols - 1] = sum + sum;
        }
    }

    private static T[] Alternating<T>(T scale, T small) where T : INumberBase<T>
    {
        var values = new T[16];
        for (int i = 0; i < values.Length; i += 4)
        {
            values[i] = scale;
            values[i + 1] = small;
            values[i + 2] = -scale;
            values[i + 3] = small;
        }

        return values;
    }

    private static float[] BuildFloatInput(DiffRandom rng) =>
        rng.IsBoundarySeed
            ? Alternating(1e8f, 1f)
            : Alternating((1 + rng.NextBelow(4)) * 1e8f, (float)(1 + rng.NextBelow(7)));

    private static double[] BuildDoubleInput(DiffRandom rng) =>
        rng.IsBoundarySeed
            ? Alternating(1e16, 1.0)
            : Alternating((1 + rng.NextBelow(4)) * 1e16, (double)(1 + rng.NextBelow(7)));
}
