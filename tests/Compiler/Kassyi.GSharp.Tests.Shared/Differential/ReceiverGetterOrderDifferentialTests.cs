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

    internal static readonly DifferentialCase s_receiverGetter = DiffCases.Create(
        ReceiverGetterOrderDifferentialKernels.LaunchReceiverGetterKernel,
        inputs: static (rng, rows) => CreateBuffers(rng, rows, ReceiverGetterOrderDifferentialKernels.ReceiverColumns),
        shape: LaunchShape.ForRows(Rows, blockX: 32),
        inputName: "data",
        outputName: "r");

    internal static readonly DifferentialCase s_implicitThisGetter = DiffCases.Create(
        ReceiverGetterOrderDifferentialKernels.LaunchImplicitThisGetterKernel,
        inputs: static (rng, rows) =>
            CreateBuffers(rng, rows, ReceiverGetterOrderDifferentialKernels.ImplicitThisColumns),
        shape: LaunchShape.ForRows(Rows, blockX: 32),
        inputName: "data",
        outputName: "r");

    internal static readonly DifferentialCase s_accessorCall = DiffCases.Create(
        ReceiverGetterOrderDifferentialKernels.LaunchAccessorCallKernel,
        inputs: static (rng, rows) => CreateBuffers(rng, rows, ReceiverGetterOrderDifferentialKernels.AccessorColumns),
        shape: LaunchShape.ForRows(Rows, blockX: 32),
        inputName: "data",
        outputName: "r");

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
