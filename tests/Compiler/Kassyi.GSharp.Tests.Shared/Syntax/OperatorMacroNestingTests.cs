using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// An operator macro used as an operand of another one is computed into a temporary first, in the C# order of
/// evaluation, on the GPU and the native CPU build (<see cref="OperatorMacroNestingManagedTests"/>: the managed fallback).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class OperatorMacroNestingTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_NestedOperatorMacro_IsComputedIntoATemporary()
    {
        int[] values = [1, 2, 3];
        var result = new int[2];

        OperatorMacroNestingKernels.LaunchNestedKernel(values, result, blockDimX: 1);

        // Combine(l, r) = l * 10 + r. c - (a - b) = Combine(3, Combine(1, 2)) = 42;
        // (a - b) - (b - (a - c)) with c = 42: Combine(12, Combine(2, Combine(1, 42))) = 120 + 72
        Assert.Equal([42, 192], result);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_OperandBeforeNestedOperatorMacro_IsReadFirst()
    {
        int[] entries = [7];
        var result = new int[1];

        OperatorMacroNestingKernels.LaunchNestedOrderKernel(entries, result, blockDimX: 1);

        // The left operand reads entries[0] = 7 before the inner operator writes 100
        Assert.Equal(82, result[0]);
        Assert.Equal(100, entries[0]);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_OperandBeforeInnerCallThatReadsMemory_IsEvaluatedFirst()
    {
        var data = new int[1];
        var result = new int[1];

        OperatorMacroNestingOrderKernels.LaunchInnerReadsMemoryKernel(data, result, blockDimX: 1);

        // WriteFirst sets data[0] = 5 before a / source reads it: Combine(5, 1 * 10 + 5) = 65 (60 if read before)
        Assert.Equal(65, result[0]);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_OperandBeforeInnerCallThatWritesItsContext_IsReadFirst()
    {
        var result = new int[2];

        OperatorMacroNestingOrderKernels.LaunchInnerWritesContextKernel(result, blockDimX: 1);

        // log.Last = 3 is read before a & b sets it to 9: Combine(3, 12) = 42 (102 if read after)
        Assert.Equal([42, 9], result);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_NestedOperatorMacroUnderShiftUnaryAndIdentityCast_IsComputedIntoATemporary()
    {
        var result = new int[3];

        OperatorMacroShapeKernels.LaunchShapesKernel(result, blockDimX: 1);

        // (a - b) << 2 = Shift(Combine(1, 2), 2) = 1202; -(a - b) = 7 - 12; c - (ShapeValue)(a - b) = 3 * 10 + 12
        Assert.Equal([1202, -5, 42], result);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_OperandBeforeInnerCallThatWritesARefContext_IsReadFirst()
    {
        var result = new int[2];

        OperatorMacroNestingOrderKernels.LaunchInnerWritesRefContextKernel(result, blockDimX: 1);

        // log.Last = 3 is read through the ref parameter before a & b sets it to 9: Combine(3, 12) = 42
        Assert.Equal([42, 9], result);
    }
}
