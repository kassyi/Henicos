#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for a variable passed without the <c>in</c> keyword to an <c>in</c> parameter of a device
/// function that reaches a barrier: the callee reads the variable, not a copy taken before the call, on every path
/// (#1585).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class InArgumentAliasDifferentialTests
{
    private const int Rows = 128;

    // The oracle cannot run barriers; the reference is the C# result of each column: the value the callee wrote
    // through the other reference, except for the element of a value, which C# copies
    private static readonly DifferentialCase s_inArgumentAlias = new(
        nameof(InArgumentAliasDifferentialKernels) + "." +
        nameof(InArgumentAliasDifferentialKernels.InArgumentAliasKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, InArgumentAliasDifferentialKernels.Columns, OutputPolicy.Exact),
        Launch = static (b, s) => InArgumentAliasDifferentialKernels.LaunchInArgumentAliasKernel(
            b.Get<int>("xs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, _) =>
        {
            var xs = b.Get<int>("xs");
            var r = b.Get<int>("r");
            int columns = InArgumentAliasDifferentialKernels.Columns.Length;
            for (int tid = 0; tid < xs.Length; tid++)
            {
                int x = xs[tid] & 0x3FF;
                for (int c = 0; c < columns - 1; c++)
                {
                    r[(tid * columns) + c] = x + 7;
                }

                r[(tid * columns) + columns - 1] = x;
            }
        },
    };

    [SkippableFact]
    public void SuspendingCall_InParameterGivenAVariableWithoutTheKeyword_PassesTheVariableOnEveryPath() =>
        DifferentialRunner.Run(s_inArgumentAlias);
}
