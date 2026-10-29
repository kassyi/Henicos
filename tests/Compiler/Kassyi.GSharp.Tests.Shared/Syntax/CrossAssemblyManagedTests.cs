#if GSHARP_CPU
using Kassyi.GSharp.Tests.DeviceLibrary;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The kernels of <see cref="CrossAssemblyKernels"/> on the managed fallback, which calls the library's C# methods by
/// their full names (#433). The values are those of <see cref="CrossAssemblyTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
[Collection(TestCollections.GlobalStateSequential)]
public class CrossAssemblyManagedTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_Mu_ThroughUsingAndInFull()
    {
        uint[] values = [3, 5, 7, 0xFFFF_FFFF];
        var result = new uint[4];
        var qualified = new uint[4];

        ManagedKernelRun.Run(() =>
        {
            CrossAssemblyKernels.LaunchMuKernel(values, result, blockDimX: 4);
            CrossAssemblyKernels.LaunchQualifiedMuKernel(values, qualified, blockDimX: 4);
        });

        Assert.Equal(1431655765u, result[0]);
        Assert.Equal(result, qualified);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_SumPair_StructOfTheLibrary()
    {
        Pair[] pairs = [new() { A = 1, B = 2 }, new() { A = 10, B = 20 }, new() { A = -5, B = 5 }, new() { A = 7, B = 0 }];
        var result = new int[4];

        ManagedKernelRun.Run(() => CrossAssemblyKernels.LaunchSumPairKernel(pairs, result, blockDimX: 4));

        Assert.Equal([3, 30, 0, 7], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_Twice_ThroughAnInternalFunction()
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        ManagedKernelRun.Run(() => CrossAssemblyKernels.LaunchTwiceKernel(values, result, blockDimX: 4));

        Assert.Equal([2, 4, 6, 8], result);
    }

    // The library emits the managed version of AddPair and records it; the managed fallback calls it (#711)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_AddPair_OperatorMacroOfTheLibrary()
    {
        Pair[] pairs = [new() { A = 1, B = 2 }, new() { A = 10, B = 20 }, new() { A = -5, B = 5 }, new() { A = 7, B = 0 }];
        var result = new int[4];
        var calls = new int[4];

        ManagedKernelRun.Run(() => CrossAssemblyKernels.LaunchAddPairKernel(pairs, result, calls, blockDimX: 4));

        Assert.Equal([3, 30, 0, 7], result);
        Assert.Equal([1, 1, 1, 1], calls);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_Vec2_ConstructorAndComputedProperty()
    {
        float[] xs = [1f, 2f, 3f, 4f];
        var result = new float[4];

        ManagedKernelRun.Run(() => CrossAssemblyKernels.LaunchVec2Kernel(xs, result, blockDimX: 4));

        Assert.Equal([5f, 7f, 9f, 11f], result);
    }

    // The library's managed state machine suspends at both barriers (C3)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_Exchange_SuspendingLibraryFunction()
    {
        var buffer = new int[4];
        var result = new int[4];

        ManagedKernelRun.Run(() => CrossAssemblyKernels.LaunchExchangeKernel(buffer, result, blockDimX: 4));

        Assert.Equal([10, 20, 30, 0], result);
        Assert.Equal([0, 10, 20, 30], buffer);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_Float3_ParameterOfALibraryFunction()
    {
        float[] xs = [1f, 2f, 3f, 4f];
        var inside = new float[4];
        var kernel = new float[4];

        ManagedKernelRun.Run(() =>
        {
            CrossAssemblyKernels.LaunchLength3Kernel(xs, inside, blockDimX: 4);
            CrossAssemblyKernels.LaunchDot3Kernel(xs, kernel, blockDimX: 4);
        });

        Assert.Equal([6f, 9f, 14f, 21f], inside);
        Assert.Equal(inside, kernel);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_StructsOfTheLibrary()
    {
        float[] xs = [1f, 2f, 3f, 4f];
        int[] values = [0, 1, 2, -1];
        var particles = new float[4];
        var scaled = new int[4];
        var alpha = new int[4];

        ManagedKernelRun.Run(() =>
        {
            CrossAssemblyKernels.LaunchParticleKernel(xs, particles, blockDimX: 4);
            CrossAssemblyKernels.LaunchScaledKernel(values, scaled, blockDimX: 4);
            CrossAssemblyKernels.LaunchAlphaKernel(values, alpha, blockDimX: 4);
        });

        Assert.Equal([7f, 8f, 9f, 10f], particles);
        Assert.Equal([0, 2, 4, -2], scaled);
        Assert.Equal([301, 602, 903, 0], alpha);
    }
}
#endif
