#if GSHARP_CPU
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// The kernels of <see cref="PrivateFrameTypeKernels"/> on the managed fallback, whose state machines keep the private
/// nested struct in the frames of the device functions; the values are those of <see cref="PrivateFrameTypeNativeTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
[Collection(TestCollections.GlobalStateSequential)]
public class PrivateFrameTypeManagedTests
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

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PrivateStruct_KernelOfAnotherClass_ComputesValues() => Run(PrivateFrameTypeCases.OtherClass);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PrivateStruct_DeviceFunctionOfAnotherClass_ComputesValues() =>
        Run(PrivateFrameTypeCases.OtherClassChain);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PrivateStruct_TwoFramesWithWarpShuffle_EachMeetsItsOwnShuffle() =>
        Run(PrivateFrameTypeCases.OtherClassWarp);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PrivateStruct_ProtectedFunctionFromDerivedClass_ComputesValues() =>
        Run(PrivateFrameTypeCases.DerivedClass);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PrivateStruct_UnsafeFunctionFromSafeKernel_ComputesValues() => Run(PrivateFrameTypeCases.PointerFrame);

    private static void Run(PrivateFrameTypeCase run)
    {
        int[] data = [1, 2, 3, 4];
        var result = new int[4];

        ManagedKernelRun.Run(() => run.Launch(data, result));

        Assert.Equal(run.Expected, result);
    }
}
#endif
