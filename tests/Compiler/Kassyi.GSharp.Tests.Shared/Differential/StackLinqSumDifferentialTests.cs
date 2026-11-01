#nullable enable
using System.Numerics;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <c>StackLinq</c> <see cref="float"/> and <see cref="double"/> <c>Sum</c> overloads (#730).
/// Verifies that the managed path sums elements sequentially from start to end, matching the GPU and the native CPU path
/// bitwise even on ill-conditioned sequences where SIMD lane summation rounds differently.
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
        Reference = FillReference<float>,
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
        Reference = FillReference<double>,
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_FloatSum_MatchesTheOracleOnEveryPath()
    {
        // Every form of StackLinq float Sum (direct, selector, Select, Where, Take, static call, and [GpuDeviceFunction]
        // direct and transitive) sums elements in index order from 0 to Length - 1, matching the GPU on ill-conditioned
        // inputs where SIMD vector reduction rounds away small terms (1.0f vs 0.0f).
        // mutant: ManagedExecutionRewriter_Invocation.cs TryRewriteSequentialSum returns null (direct, static and device forms fail with 0 vs 1)
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
        // mutant: ManagedRewriteClassifier.cs IsSequentialSum excludes SpecialType.System_Double (double columns fail with 0 vs 1)
        Assert.NotEmpty(DiffPaths.Available);
        DifferentialRunner.Run(s_doubleSum);
    }

    private static void FillReference<T>(DiffBuffers b) where T : unmanaged, INumberBase<T>
    {
        var data = b.Get<T>("data");
        var r = b.Get<T>("r");
        T sum = T.Zero;
        foreach (T v in data)
        {
            sum += v;
        }

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
