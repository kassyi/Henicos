using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The kernels of <see cref="OperatorMacroArgumentKernels"/> on the native target (CPU or CUDA), whose values the
/// managed fallback must reproduce (<c>OperatorMacroArgumentManagedTests</c>).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class OperatorMacroArgumentTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InOperand_IsPassed()
    {
        var result = new int[1];

        OperatorMacroArgumentKernels.LaunchInArgumentKernel(result, blockDimX: 1);

        Assert.Equal(OperatorMacroArgumentCases.InArgument, result);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_InOperandBeforeAnInnerWrite_IsReadThroughItsVariable()
    {
        InValue[] values = [new InValue(3), new InValue(5)];
        var result = new int[2];

        OperatorMacroArgumentKernels.LaunchInSpillKernel(values, result, blockDimX: 1);

        Assert.Equal(OperatorMacroArgumentCases.InSpill, new[] { result[0], result[1], values[0].V, values[1].V });
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_RefResultBeforeAnInnerCall_IsTheElementChosenFirst()
    {
        var values = new SpillValue[2];
        var calls = new int[1];

        OperatorMacroArgumentKernels.LaunchRefSpillKernel(values, calls, blockDimX: 1);

        Assert.Equal(OperatorMacroArgumentCases.RefSpill, new[] { values[0].V, values[1].V, calls[0] });
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_OutResult_IsAssignedByTheTarget()
    {
        var result = new int[2];

        OperatorMacroArgumentKernels.LaunchOutResultKernel(result, blockDimX: 1);

        Assert.Equal(OperatorMacroArgumentCases.OutResult, result);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_InOperandReturnedByReferenceBeforeAnInnerWrite_IsReadThroughTheReference()
    {
        InValue[] values = [new InValue(3), new InValue(5)];
        var result = new int[2];

        OperatorMacroArgumentKernels.LaunchRefCallSpillKernel(values, result, blockDimX: 1);

        Assert.Equal(OperatorMacroArgumentCases.InSpill, new[] { result[0], result[1], values[0].V, values[1].V });
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_SharedMemoryInOperandBeforeAnInnerWrite_IsReadThroughTheElement()
    {
        var result = new int[3];

        OperatorMacroArgumentKernels.LaunchSharedRefSpillKernel(result, blockDimX: 1);

        Assert.Equal(OperatorMacroArgumentCases.SharedSpill, result);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_AutoPropertyTargetAndOperand_AreItsStorage()
    {
        var result = new int[5];

        OperatorMacroArgumentKernels.LaunchPropertyTargetKernel(result, blockDimX: 1);

        Assert.Equal(OperatorMacroArgumentCases.PropertyTarget, result);
    }

}

/// <summary>The expected values of <see cref="OperatorMacroArgumentKernels"/>.</summary>
internal static class OperatorMacroArgumentCases
{
    // 1 * 10 + 2
    public static readonly int[] InArgument = [12];

    // a & b = 12 writes values[0] = 7 first; the outer target reads values[0] through its in parameter: 7 * 10 + 12
    // (42 if the element were copied before the inner call); two calls; values[1] untouched
    public static readonly int[] InSpill = [82, 2, 7, 5];

    // values[0] = 1 * 10 + (2 * 10 + 3), values[1] untouched, two calls
    public static readonly int[] RefSpill = [33, 0, 2];

    // 4 + 4, 5 + 5
    public static readonly int[] OutResult = [8, 10];

    // a | b = 12 writes shared[0] = 7 first; the outer target reads shared[0] through its in parameter: 7 * 10 + 12 (42
    // if the element were copied before the inner call); shared[0] written again, shared[1] untouched
    public static readonly int[] SharedSpill = [82, 7, 5];

    // h.P = x + y: 1 + 10 = 11, then 11 + 1 * 100. h.P = h.P + y: 111 + 10 = 121, then left is the result itself:
    // 121 + 121 * 100 (11221 if left were a copy). o.Holder.P = x + y as the first; h.P = o.Auto.P + y reads a copy
    // of o.Holder: 121 + 111 * 100. The device function: as the first
    public static readonly int[] PropertyTarget = [111, 12221, 111, 11221, 111];
}
