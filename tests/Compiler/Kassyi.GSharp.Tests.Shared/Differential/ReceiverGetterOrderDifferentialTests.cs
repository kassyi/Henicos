#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for the order of a computed struct property's getter or setter next to an operand it interferes
/// with: read as a receiver (<c>h.Next.Value</c>), through the implicit <c>this</c> (<c>Pair(Count, NextInt)</c>), or
/// assigned (#1606).
/// </summary>
public class ReceiverGetterOrderDifferentialTests
{
    private const int Rows = 64;

    private const string KernelName = nameof(ReceiverGetterOrderDifferentialKernels.ReceiverGetterOrderKernel);

    private static readonly DifferentialCase s_receiverGetterOrder = new(
        $"{nameof(ReceiverGetterOrderDifferentialKernels)}.{KernelName}")
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("data", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, ReceiverGetterOrderDifferentialKernels.Columns, OutputPolicy.Exact),
        Launch = static (b, s) => ReceiverGetterOrderDifferentialKernels.LaunchReceiverGetterOrderKernel(
            b.Get<int>("data"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => ReceiverGetterOrderDifferentialKernels.ReceiverGetterOrderKernel(
            b.Get<int>("data"),
            b.Get<int>("r")),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ComputedPropertyAccessor_NextToAnInterferingOperand_RunsInTheCSharpOrderOnEveryPath() =>
        DifferentialRunner.Run(s_receiverGetterOrder);
}
