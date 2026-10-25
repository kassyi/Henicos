using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Execution tests for hoisted local variables and labels that share their name with struct members (#481), lambda
/// parameters (#504), or sibling-block labels.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class HoistedNameIdentityTests : TranspilerTestBase
{
    internal const int BlockSize = 32;

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_BarrierLocalSameNameInitializerKernel_PreservesMemberAndLocal() =>
        AssertInitializerSameName();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_BarrierLocalSameNameWithKernel_PreservesMemberAndLocal() =>
        AssertWithSameName();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_BarrierLocalSameNameMemberAccessKernel_PreservesMemberAndLocal() =>
        AssertMemberAccessSameName();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_BarrierLocalSameNameLambdaParameterKernel_DoesNotCaptureHoistedLocal() =>
        AssertLambdaParameterSameName();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_SiblingBlockSameLabelKernel_BranchesToEachBlockLabel() =>
        AssertSiblingBlockSameLabel();

    internal static void AssertInitializerSameName()
    {
        var output = new int[BlockSize];
        HoistedNameIdentityKernels.LaunchBarrierLocalSameNameInitializerKernel(output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(t * 100 + 1, output[t]);
        }
    }

    internal static void AssertWithSameName()
    {
        var output = new int[BlockSize];
        HoistedNameIdentityKernels.LaunchBarrierLocalSameNameWithKernel(output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(t * 100 + 3, output[t]);
        }
    }

    internal static void AssertMemberAccessSameName()
    {
        var output = new int[BlockSize];
        HoistedNameIdentityKernels.LaunchBarrierLocalSameNameMemberAccessKernel(output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            Assert.Equal(t * 100 + 5, output[t]);
        }
    }

    internal static void AssertLambdaParameterSameName()
    {
        var output = new int[BlockSize];
        HoistedNameIdentityKernels.LaunchBarrierLocalSameNameLambdaParameterKernel(output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            // sum = 0 + 1 + 2 = 3; output[t] = 3 + t
            Assert.Equal(3 + t, output[t]);
        }
    }

    internal static void AssertSiblingBlockSameLabel()
    {
        var output = new int[BlockSize];
        HoistedNameIdentityKernels.LaunchSiblingBlockSameLabelKernel(output, blockDimX: BlockSize);
        for (int t = 0; t < BlockSize; t++)
        {
            // Even t: t + 10 (block 1 goto done) + 2000 (block 2 fallthrough) = t + 2010
            // Odd t:  t + 100 (block 1 fallthrough) + 1000 (block 2 goto done) = t + 1100
            int expected = (t & 1) == 0 ? t + 2010 : t + 1100;
            Assert.Equal(expected, output[t]);
        }
    }
}
