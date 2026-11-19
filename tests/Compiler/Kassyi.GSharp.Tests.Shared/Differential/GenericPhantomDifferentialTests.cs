#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for generic device functions whose type parameters are named nowhere, called with several type
/// arguments, one of them waiting at a barrier: every path computes what the C# execution computes.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class GenericPhantomDifferentialTests
{
    private const int Rows = 128;

    internal static readonly DifferentialCase s_phantom = new(
        $"{nameof(GenericPhantomDifferentialKernels)}.{nameof(GenericPhantomDifferentialKernels.PhantomKernel)}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (xs, _) = DiffValues.Int32Pairs(rng, rows);
            return new DiffBuffers()
                .In("xs", xs)
                .Out<int>("r", rows, GenericPhantomDifferentialKernels.Columns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => GenericPhantomDifferentialKernels.LaunchPhantomKernel(
            b.Get<int>("xs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        // The kernel waits at a barrier, which the C# execution cannot run thread by thread
        Reference = static (b, s) =>
        {
            var xs = b.Get<int>("xs");
            var r = b.Get<int>("r");
            for (var tid = 0; tid < xs.Length; tid++)
            {
                var x = xs[tid] & 0xFFFF;
                r[tid * 4] = (x * 3) + 1;
                r[(tid * 4) + 1] = (x * 3) + 2;
                r[(tid * 4) + 2] = (x - 5) * 2;
                r[(tid * 4) + 3] = (x * 3) + 4;
            }
        },
    };

    [SkippableFact]
    public void PhantomKernel_TypeParametersNamedNowhere_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_phantom);
}
