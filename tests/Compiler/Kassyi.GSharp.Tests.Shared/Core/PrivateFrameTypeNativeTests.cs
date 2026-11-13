using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// The kernels of <see cref="PrivateFrameTypeKernels"/> on the native target (CPU or CUDA): device functions that keep a
/// private nested struct across a barrier. <c>PrivateFrameTypeManagedTests</c> runs the same kernels on the managed
/// fallback, and both must give these values.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Compiler)]
public class PrivateFrameTypeNativeTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PrivateStruct_SameClassAsKernel_ComputesValues() => Run(PrivateFrameTypeCases.SameClass);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PrivateStruct_FrameHoldsPrivateFrame_ComputesValues() => Run(PrivateFrameTypeCases.SameClassChain);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PrivateStruct_FunctionOfNestedClass_ComputesValues() => Run(PrivateFrameTypeCases.NestedClass);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PrivateStruct_FunctionOfNestedGenericClass_ComputesValues() => Run(PrivateFrameTypeCases.GenericClass);

    private static void Run(PrivateFrameTypeCase run)
    {
        int[] data = [1, 2, 3, 4];
        var result = new int[4];

        run.Launch(data, result);

        Assert.Equal(run.Expected, result);
    }
}
