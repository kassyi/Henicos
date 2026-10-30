#if GSHARP_CPU
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Nested operator macros on the managed fallback: the inner operator is computed into a temporary first, and the
/// operands before it that it interferes with are evaluated before it, in the C# order (#711). The values are those of
/// <see cref="OperatorMacroNestingTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class OperatorMacroNestingManagedTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_NestedOperatorMacro_IsComputedIntoATemporary()
    {
        int[] values = [1, 2, 3];
        var result = new int[2];

        ManagedKernelRun.Run(() => OperatorMacroNestingKernels.LaunchNestedKernel(values, result, blockDimX: 1));

        Assert.Equal([42, 192], result);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_OperandBeforeNestedOperatorMacro_IsReadFirst()
    {
        int[] entries = [7];
        var result = new int[1];

        ManagedKernelRun.Run(() => OperatorMacroNestingKernels.LaunchNestedOrderKernel(entries, result, blockDimX: 1));

        Assert.Equal(82, result[0]);
        Assert.Equal(100, entries[0]);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_OperandBeforeInnerCallThatReadsMemory_IsEvaluatedFirst()
    {
        var data = new int[1];
        var result = new int[1];

        ManagedKernelRun.Run(() =>
            OperatorMacroNestingOrderKernels.LaunchInnerReadsMemoryKernel(data, result, blockDimX: 1));

        Assert.Equal(65, result[0]);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_OperandBeforeInnerCallThatWritesItsContext_IsReadFirst()
    {
        var result = new int[2];

        ManagedKernelRun.Run(() => OperatorMacroNestingOrderKernels.LaunchInnerWritesContextKernel(result, blockDimX: 1));

        Assert.Equal([42, 9], result);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_NestedOperatorMacroUnderShiftUnaryAndIdentityCast_IsComputedIntoATemporary()
    {
        var result = new int[3];

        ManagedKernelRun.Run(() => OperatorMacroShapeKernels.LaunchShapesKernel(result, blockDimX: 1));

        Assert.Equal([1202, -5, 42], result);
    }

    // CombineThroughRef is a device function that expands the operator macro: the kernel calls its managed version
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_OperandBeforeInnerCallThatWritesARefContext_IsReadFirst()
    {
        var result = new int[2];

        ManagedKernelRun.Run(() =>
            OperatorMacroNestingOrderKernels.LaunchInnerWritesRefContextKernel(result, blockDimX: 1));

        Assert.Equal([42, 9], result);
    }
}
#endif
