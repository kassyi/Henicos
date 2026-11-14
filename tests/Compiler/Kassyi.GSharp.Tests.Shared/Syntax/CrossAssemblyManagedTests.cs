#if GSHARP_CPU
using Kassyi.GSharp.Runtime;
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
    public void Mu_ManagedExecutionThroughUsingAndQualified_ComputesModularInverse()
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

    // A library function whose only managed rewrite is the division: the managed fallback gives the GPU result of
    // MinValue / -1, and the C# execution of the consumer runs the library's C# (it does not need the generated code)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Quotient_ManagedExecutionMinValueByMinusOne_GivesTheGpuResult()
    {
        int[] a = [int.MinValue, 7];
        int[] d = [-1, 2];
        var result = new int[2];

        ManagedKernelRun.Run(() => CrossAssemblyKernels.LaunchQuotientKernel(a, d, result, blockDimX: 2));

        Assert.Equal([int.MinValue, 3], result);
    }

    // mutant: the export record leaves ReachesGeneratedMeaning out, so a reader takes it for a record of an older G#
    // and reads ManagedVersion, which is true here: the C# execution then refuses the kernel
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Quotient_CSharpExecution_RunsTheLibraryFunction()
    {
        int[] a = [20, 7];
        int[] d = [4, 2];
        var result = new int[2];

        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            CrossAssemblyKernels.LaunchQuotientKernel(a, d, result, blockDimX: 2);
        }

        Assert.Equal([5, 3], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SumPair_ManagedExecutionLibraryStruct_CalculatesSum()
    {
        Pair[] pairs = [new() { A = 1, B = 2 }, new() { A = 10, B = 20 }, new() { A = -5, B = 5 }, new() { A = 7, B = 0 }];
        var result = new int[4];

        ManagedKernelRun.Run(() => CrossAssemblyKernels.LaunchSumPairKernel(pairs, result, blockDimX: 4));

        Assert.Equal([3, 30, 0, 7], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Twice_ManagedExecutionInternalFunction_DoublesElements()
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        ManagedKernelRun.Run(() => CrossAssemblyKernels.LaunchTwiceKernel(values, result, blockDimX: 4));

        Assert.Equal([2, 4, 6, 8], result);
    }

    // The library emits the managed version of AddPair and records it; the managed fallback calls it (#711)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AddPair_ManagedExecutionOperatorMacro_CalculatesSumAndRecordsCalls()
    {
        Pair[] pairs = [new() { A = 1, B = 2 }, new() { A = 10, B = 20 }, new() { A = -5, B = 5 }, new() { A = 7, B = 0 }];
        var result = new int[4];
        var calls = new int[4];

        ManagedKernelRun.Run(() => CrossAssemblyKernels.LaunchAddPairKernel(pairs, result, calls, blockDimX: 4));

        Assert.Equal([3, 30, 0, 7], result);
        Assert.Equal([1, 1, 1, 1], calls);
    }

    // The library's managed version of Difference calls the private target through an [UnsafeAccessor] stub (#711)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Difference_ManagedExecutionPrivateOperatorMacroTarget_CalculatesDifference()
    {
        Pair[] pairs = [new() { A = 1, B = 2 }, new() { A = 10, B = 20 }, new() { A = -5, B = 5 }, new() { A = 7, B = 0 }];
        var result = new int[4];

        ManagedKernelRun.Run(() => CrossAssemblyKernels.LaunchDifferenceKernel(pairs, result, blockDimX: 4));

        Assert.Equal([-1, -10, -10, 7], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Vec2_ManagedExecutionConstructorAndProperty_ComputesExpectedSum()
    {
        float[] xs = [1f, 2f, 3f, 4f];
        var result = new float[4];

        ManagedKernelRun.Run(() => CrossAssemblyKernels.LaunchVec2Kernel(xs, result, blockDimX: 4));

        Assert.Equal([5f, 7f, 9f, 11f], result);
    }

    // The library's managed state machine suspends at both barriers (C3)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Exchange_ManagedExecutionSuspendingLibraryFunction_ExchangesBufferValues()
    {
        var buffer = new int[4];
        var result = new int[4];

        ManagedKernelRun.Run(() => CrossAssemblyKernels.LaunchExchangeKernel(buffer, result, blockDimX: 4));

        Assert.Equal([10, 20, 30, 0], result);
        Assert.Equal([0, 10, 20, 30], buffer);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Float3_ManagedExecutionLibraryFunction_ComputesVectorLengthAndDotProduct()
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
    public void LibraryStructs_ManagedExecutionMultipleKernels_ComputesExpectedResults()
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

    // The library structs whose layout lives in the metadata tables (#736): Explicit, Pack = 1, Size = 16, a union
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void LibraryStructLayouts_ManagedExecutionVariousStructs_MatchesNativeLayoutSemantics()
    {
        var swapped = new int[] { -1, -1 };
        var packed = new int[] { -1, -1 };
        Sized[] sized = [new() { A = 5 }, new() { A = -6 }, new() { A = 7 }, new() { A = int.MaxValue }];
        var sizedResult = new int[4];
        float[] values = [1f, -2.5f, 0f, float.MaxValue];
        var unions = values.Select(static (v, i) => new IntFloatUnion { Value = v, Tag = 100 + i }).ToArray();
        var bits = new int[4];

        ManagedKernelRun.Run(() =>
        {
            CrossAssemblyKernels.LaunchSwapPairKernel(new SwapPair { A = 11, B = 22 }, swapped);
            CrossAssemblyKernels.LaunchPackedPairKernel(new PackedPair { Tag = 7, Value = 0x01020304 }, packed);
            CrossAssemblyKernels.LaunchSizedKernel(sized, sizedResult, blockDimX: 4);
            CrossAssemblyKernels.LaunchIntFloatUnionKernel(unions, bits, blockDimX: 4);
        });

        Assert.Equal([11, 22], swapped);
        Assert.Equal([7, 0x01020304], packed);
        Assert.Equal([5, -6, 7, int.MaxValue], sizedResult);
        Assert.Equal([0, 10, 20, 30], sized.Select(static d => d.A));
        Assert.Equal(values.Select(BitConverter.SingleToInt32Bits), bits);
        Assert.Equal(values.Select(static v => BitConverter.SingleToInt32Bits(-v)), unions.Select(static d => d.Bits));
        Assert.Equal([100, 101, 102, 103], unions.Select(static d => d.Tag));
    }
}
#endif
