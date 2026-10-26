#if GSHARP_CPU
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.GSharp.Tests.Shared.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// The computed property kernels of <see cref="StructAbiKernels"/> on the managed fallback, which runs the C# body: the
/// third way, next to the CPU and CUDA native ones of <see cref="StructAbiTests"/>, that must give the same values (#471).
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
[Collection(TestCollections.GlobalStateSequential)]
public class StructAbiManagedTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_ComputedProperty_ReadsLikeNative()
    {
        var result = new int[4];

        ManagedKernelRun.Run(() => StructAbiKernels.LaunchReadComputedPropertyKernel(result));

        // A = 4, C = 7: Sum = A + 1, Twice = Sum * 2, Product = A * C, Difference = C - Sum
        Assert.Equal([5, 10, 28, 2], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_ComputedPropertyThroughRef_ReadsLikeNative()
    {
        var data = new AbiComputedPropertyStruct { A = 2, C = 9 };
        var result = new int[4];

        ManagedKernelRun.Run(() => StructAbiKernels.LaunchReadComputedPropertyThroughRefKernel(ref data, result));

        Assert.Equal([3, 6, 18, 6], result);
        Assert.Equal(2, data.A);
        Assert.Equal(9, data.C);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ComputedPropertyStruct_RoundtripKeepsTheMemberAfterIt()
    {
        var data = new AbiComputedPropertyStruct { A = 1, C = 7 };

        ManagedKernelRun.Run(() => StructAbiKernels.LaunchRoundtripComputedPropertyKernel(ref data));

        Assert.Equal(1, data.A);
        Assert.Equal(8, data.C);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_SetAndInitAccessorBodies_RunLikeNative()
    {
        var output = new int[3];

        ManagedKernelRun.Run(() => StructReproKernels.LaunchSetAndWithAccessorBodyKernel(output, blockDimX: 1));

        Assert.Equal([1006, 0, 810], output);
    }
}
#endif
