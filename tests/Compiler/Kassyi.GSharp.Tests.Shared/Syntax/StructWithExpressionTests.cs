using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// <c>with</c> on a struct copies the value once, assigns the initializers in the written order and leaves the source
/// unchanged. The expected values are worked out by hand from the kernels.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
public class StructWithExpressionTests : TranspilerTestBase
{
    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void WithExpression_PlainStruct_CopiesAssignsAndLeavesSourceUnchanged()
    {
        var r = new int[6];
        StructWithExpressionKernels.LaunchPlainStructKernel(r, blockDimX: 1);
        Assert.Equal([10, 2, 30, 1, 2, 3], r);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void WithExpression_EmptyInitializer_CopiesOriginalValue()
    {
        var r = new int[3];
        StructWithExpressionKernels.LaunchEmptyInitializerKernel(r, blockDimX: 1);
        Assert.Equal([4, 99, 5], r);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void WithExpression_ReadonlyStructWithInitProperties_CopiesAndAssignsProperties()
    {
        var r = new int[3];
        StructWithExpressionKernels.LaunchReadonlyStructKernel(r, blockDimX: 1);
        Assert.Equal([1, 20, 2], r);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void WithExpression_RecordStruct_CopiesAndAssignsFields()
    {
        var r = new int[3];
        StructWithExpressionKernels.LaunchRecordStructKernel(r, blockDimX: 1);
        Assert.Equal([10, 2, 1], r);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void WithExpression_ReadonlyRecordStruct_CopiesAndAssignsProperties()
    {
        var r = new int[3];
        StructWithExpressionKernels.LaunchReadonlyRecordStructKernel(r, blockDimX: 1);
        Assert.Equal([1, 20, 2], r);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void WithExpression_NestedStructField_UpdatesNestedFieldAndPreservesSiblingFields()
    {
        var r = new int[5];
        StructWithExpressionKernels.LaunchNestedStructKernel(r, blockDimX: 1);
        Assert.Equal([100, 2, 8, 1, 7], r);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void WithExpression_ChainedWithClauses_AppliesSuccessiveUpdates()
    {
        var r = new int[3];
        StructWithExpressionKernels.LaunchChainedWithKernel(r, blockDimX: 1);
        Assert.Equal([10, 20, 3], r);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void WithExpression_ArrayField_PerformsShallowCopy()
    {
        var r = new int[4];
        StructWithExpressionKernels.LaunchArrayFieldKernel(r, blockDimX: 1);
        // r[0] is the shared array written through the copy; r[1] reads it through the source
        Assert.Equal([7, 7, 1, 5], r);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void WithExpression_GenericStruct_CopiesAndAssignsGenericFields()
    {
        var r = new int[3];
        StructWithExpressionKernels.LaunchGenericStructKernel(r, blockDimX: 1);
        Assert.Equal([1, 20, 2], r);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void WithExpression_RefStruct_CopiesAndAssignsRefFields()
    {
        var r = new int[3];
        StructWithExpressionKernels.LaunchRefStructKernel(r, blockDimX: 1);
        Assert.Equal([10, 2, 1], r);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void WithExpression_InlineArrayStruct_CopiesAllElements()
    {
        var r = new int[4];
        StructWithExpressionKernels.LaunchInlineArrayStructKernel(r, blockDimX: 1);
        Assert.Equal([5, 8, 50, 8], r);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void WithExpression_FixedBufferStruct_CopiesFixedBuffer()
    {
        var r = new int[5];
        StructWithExpressionKernels.LaunchFixedBufferStructKernel(r, blockDimX: 1);
        Assert.Equal([5, 50, 6, 1, 2], r);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void WithExpression_ExpressionOperand_EvaluatesOperandOnlyOnce()
    {
        var r = new int[4];
        var counter = new int[1];
        StructWithExpressionKernels.LaunchOperandEvaluatedOnceKernel(r, counter, blockDimX: 1);
        Assert.Equal([1, 1, 2, 30], r);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void WithExpression_MultipleInitializers_RunsInWrittenOrderAndReadsOriginal()
    {
        var r = new int[3];
        var counter = new int[1];
        StructWithExpressionKernels.LaunchAssignmentOrderKernel(r, counter, blockDimX: 1);
        Assert.Equal([1, 2, 300], r);
    }

    [SkippableTheory]
    [InlineData(1, 100)]
    [InlineData(0, 200)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void WithExpression_InsideArgumentAndTernary_EvaluatesCorrectBranchAndValue(int flag, int expectedTernary)
    {
        var r = new int[4];
        StructWithExpressionKernels.LaunchExpressionPositionsKernel(r, flag, blockDimX: 1);
        Assert.Equal([6, expectedTernary, 9, 33], r);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void WithExpression_AcrossBarriers_PreservesValuesAcrossThreadBarrier()
    {
        var r = new int[8];
        StructWithExpressionKernels.LaunchAcrossBarrierKernel(r, blockDimX: 8);
        for (var tid = 0; tid < 8; tid++)
        {
            Assert.Equal(tid + tid + 100, r[tid]);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void WithExpression_InSuspendingDeviceFunction_PreservesValuesAcrossSuspension()
    {
        var r = new int[4];
        StructWithExpressionKernels.LaunchSuspendingDeviceFunctionKernel(r, blockDimX: 4);
        for (var tid = 0; tid < 4; tid++)
        {
            Assert.Equal(tid + 1 + 10 + 3, r[tid]);
        }
    }
}
