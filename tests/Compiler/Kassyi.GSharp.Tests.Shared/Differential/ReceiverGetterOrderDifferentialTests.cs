#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for the order of a computed struct property's getter or setter next to an operand it interferes
/// with: read as a receiver (<c>h.Next.Value</c>), through the implicit <c>this</c> (<c>Pair(Count, NextInt)</c>), or
/// called by an assignment, an object initializer or a <c>with</c> expression (#1606).
/// </summary>
public class ReceiverGetterOrderDifferentialTests
{
    private const int Rows = 64;

    private static readonly DifferentialCase s_receiverGetter = new(
        KernelName(nameof(ReceiverGetterOrderDifferentialKernels.ReceiverGetterKernel)))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 32),
        Inputs = static (rng, rows) => CreateBuffers(rng, rows, ReceiverGetterOrderDifferentialKernels.ReceiverColumns),
        Launch = static (b, s) => ReceiverGetterOrderDifferentialKernels.LaunchReceiverGetterKernel(
            b.Get<int>("data"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => ReceiverGetterOrderDifferentialKernels.ReceiverGetterKernel(
            b.Get<int>("data"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_implicitThisGetter = new(
        KernelName(nameof(ReceiverGetterOrderDifferentialKernels.ImplicitThisGetterKernel)))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 32),
        Inputs = static (rng, rows) =>
            CreateBuffers(rng, rows, ReceiverGetterOrderDifferentialKernels.ImplicitThisColumns),
        Launch = static (b, s) => ReceiverGetterOrderDifferentialKernels.LaunchImplicitThisGetterKernel(
            b.Get<int>("data"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => ReceiverGetterOrderDifferentialKernels.ImplicitThisGetterKernel(
            b.Get<int>("data"),
            b.Get<int>("r")),
    };

    private static readonly DifferentialCase s_accessorCall = new(
        KernelName(nameof(ReceiverGetterOrderDifferentialKernels.AccessorCallKernel)))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 32),
        Inputs = static (rng, rows) => CreateBuffers(rng, rows, ReceiverGetterOrderDifferentialKernels.AccessorColumns),
        Launch = static (b, s) => ReceiverGetterOrderDifferentialKernels.LaunchAccessorCallKernel(
            b.Get<int>("data"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, _) => ReceiverGetterOrderDifferentialKernels.AccessorCallKernel(
            b.Get<int>("data"),
            b.Get<int>("r")),
    };

    private static string KernelName(string kernel) => $"{nameof(ReceiverGetterOrderDifferentialKernels)}.{kernel}";

    private static DiffBuffers CreateBuffers(DiffRandom rng, LaunchShape rows, string[] columns) =>
        new DiffBuffers()
            .In("data", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, columns, OutputPolicy.Exact);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ReceiverGetter_NextToAnInterferingOperand_RunsInTheCSharpOrderOnEveryPath() =>
        DifferentialRunner.Run(s_receiverGetter);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void ImplicitThisGetter_NextToAnInterferingOperand_RunsInTheCSharpOrderOnEveryPath() =>
        DifferentialRunner.Run(s_implicitThisGetter);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void AssignmentAccessor_NextToAnInterferingOperand_RunsInTheCSharpOrderOnEveryPath() =>
        DifferentialRunner.Run(s_accessorCall);
}
