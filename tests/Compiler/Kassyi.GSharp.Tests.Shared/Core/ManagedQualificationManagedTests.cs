#if GSHARP_CPU
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// The kernels of <see cref="ManagedQualificationKernels"/> on the managed fallback, which copies the kernel body into a
/// generated file without the user's <c>using</c> directives: the names must come out fully qualified (#433). The values
/// are those of the native runs in <see cref="ManagedQualificationNativeTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
[Collection(TestCollections.GlobalStateSequential)]
public class ManagedQualificationManagedTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_UsingDirective_TypeAndClassOfAnotherNamespace()
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        ManagedKernelRun.Run(() => ManagedQualificationKernels.LaunchUsingDirectiveKernel(values, result, blockDimX: 4));

        // A + B = v + 2v
        Assert.Equal([3, 6, 9, 12], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_UsingStatic_DeviceFunctionWithoutItsClass()
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        ManagedKernelRun.Run(() => ManagedQualificationKernels.LaunchUsingStaticKernel(values, result, blockDimX: 4));

        // 3v + 1
        Assert.Equal([4, 7, 10, 13], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_HoistedLocal_TypeOfAnotherNamespace()
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        ManagedKernelRun.Run(() => ManagedQualificationKernels.LaunchHoistedLocalKernel(values, result, blockDimX: 4));

        // 10v + 2v
        Assert.Equal([12, 24, 36, 48], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_RefStructInitializer_ConstantOfAnotherNamespace()
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        ManagedKernelRun.Run(() => ManagedQualificationKernels.LaunchRefStructInitializerKernel(values, result, blockDimX: 4));

        // the next thread's value + Offset (5)
        Assert.Equal([7, 8, 9, 6], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_TypeForms_GenericNestedEnumStackallocAlias()
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        ManagedKernelRun.Run(() => ManagedQualificationKernels.LaunchTypeFormsKernel(values, result, blockDimX: 4));

        // v * (1 for Low, 100 for High) + Inner.X (5) + Twice(1)
        Assert.Equal([8, 207, 10, 407], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_ExtensionCalls_ByValueRefAndIn()
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        ManagedKernelRun.Run(() => ManagedQualificationKernels.LaunchExtensionCallKernel(values, result, blockDimX: 4));

        // (2v + 1) + (v + 1) * 100 + (v + 1) * 1000
        Assert.Equal([2203, 3305, 4407, 5509], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_SuspendingCallee_NamesOfItsNamespace()
    {
        var buffer = new int[4];
        var result = new int[4];

        ManagedKernelRun.Run(() => ManagedQualificationKernels.LaunchSuspendingCalleeKernel(buffer, result, blockDimX: 4));

        // 3 * (next thread + 1)
        Assert.Equal([6, 9, 12, 3], result);
    }
}
#endif
