using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// The kernels of <see cref="ManagedQualificationKernels"/> on the native target (CPU or CUDA): they name types, device
/// functions, constants and extension methods of another namespace through <c>using</c>, <c>using static</c> and an alias,
/// which the native output resolves by symbol. <see cref="ManagedQualificationManagedTests"/> runs the same kernels on
/// the managed fallback (#433), and both must give these values.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Compiler)]
public class ManagedQualificationNativeTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UsingDirective_TypeAndClassOfAnotherNamespace()
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        ManagedQualificationKernels.LaunchUsingDirectiveKernel(values, result, blockDimX: 4);

        // A + B = v + 2v
        Assert.Equal([3, 6, 9, 12], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UsingStatic_DeviceFunctionWithoutItsClass()
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        ManagedQualificationKernels.LaunchUsingStaticKernel(values, result, blockDimX: 4);

        // 3v + 1
        Assert.Equal([4, 7, 10, 13], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_HoistedLocal_TypeOfAnotherNamespace()
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        ManagedQualificationKernels.LaunchHoistedLocalKernel(values, result, blockDimX: 4);

        // 10v + 2v
        Assert.Equal([12, 24, 36, 48], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_RefStructInitializer_ConstantOfAnotherNamespace()
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        ManagedQualificationKernels.LaunchRefStructInitializerKernel(values, result, blockDimX: 4);

        // the next thread's value + Offset (5)
        Assert.Equal([7, 8, 9, 6], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TypeForms_GenericNestedEnumStackallocAlias()
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        ManagedQualificationKernels.LaunchTypeFormsKernel(values, result, blockDimX: 4);

        // v * (1 for Low, 100 for High) + Inner.X (5) + Twice(1)
        Assert.Equal([8, 207, 10, 407], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ExtensionCalls_ByValueRefAndIn()
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        ManagedQualificationKernels.LaunchExtensionCallKernel(values, result, blockDimX: 4);

        // (2v + 1) + (v + 1) * 100 + (v + 1) * 1000
        Assert.Equal([2203, 3305, 4407, 5509], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SuspendingCallee_NamesOfItsNamespace()
    {
        var buffer = new int[4];
        var result = new int[4];

        ManagedQualificationKernels.LaunchSuspendingCalleeKernel(buffer, result, blockDimX: 4);

        // 3 * (next thread + 1)
        Assert.Equal([6, 9, 12, 3], result);
    }
}
