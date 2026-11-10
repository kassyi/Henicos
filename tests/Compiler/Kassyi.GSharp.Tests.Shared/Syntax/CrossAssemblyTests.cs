using Kassyi.GSharp.Tests.DeviceLibrary;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The kernels of <see cref="CrossAssemblyKernels"/> on the native target (CPU or CUDA): they call the device functions
/// and structs of another assembly, whose recorded text the native module imports (#433). The managed fallback runs the
/// same kernels in <c>CrossAssemblyManagedTests</c>.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Compiler)]
public class CrossAssemblyTests : TranspilerTestBase
{
    // n0 * mu == -1 (mod 2^32): for n0 = 3, mu = 0x55555555
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Mu_ThroughUsingAndQualifiedCalls_ComputesModularInverse()
    {
        uint[] values = [3, 5, 7, 0xFFFF_FFFF];
        var result = new uint[4];
        var qualified = new uint[4];

        CrossAssemblyKernels.LaunchMuKernel(values, result, blockDimX: 4);
        CrossAssemblyKernels.LaunchQualifiedMuKernel(values, qualified, blockDimX: 4);

        Assert.Equal(1431655765u, result[0]);
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(uint.MaxValue, unchecked(values[i] * result[i])));
        Assert.Equal(result, qualified);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SumPair_LibraryStructParameter_CalculatesSum()
    {
        Pair[] pairs = [new() { A = 1, B = 2 }, new() { A = 10, B = 20 }, new() { A = -5, B = 5 }, new() { A = 7, B = 0 }];
        var result = new int[4];

        CrossAssemblyKernels.LaunchSumPairKernel(pairs, result, blockDimX: 4);

        Assert.Equal([3, 30, 0, 7], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Twice_ThroughInternalFunction_DoublesElements()
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        CrossAssemblyKernels.LaunchTwiceKernel(values, result, blockDimX: 4);

        Assert.Equal([2, 4, 6, 8], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AddPair_OperatorMacroOfTheLibrary_CalculatesSumAndRecordsCalls()
    {
        Pair[] pairs = [new() { A = 1, B = 2 }, new() { A = 10, B = 20 }, new() { A = -5, B = 5 }, new() { A = 7, B = 0 }];
        var result = new int[4];
        var calls = new int[4];

        CrossAssemblyKernels.LaunchAddPairKernel(pairs, result, calls, blockDimX: 4);

        Assert.Equal([3, 30, 0, 7], result);
        Assert.Equal([1, 1, 1, 1], calls);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Difference_PrivateOperatorMacroTarget_CalculatesDifference()
    {
        Pair[] pairs = [new() { A = 1, B = 2 }, new() { A = 10, B = 20 }, new() { A = -5, B = 5 }, new() { A = 7, B = 0 }];
        var result = new int[4];

        CrossAssemblyKernels.LaunchDifferenceKernel(pairs, result, blockDimX: 4);

        Assert.Equal([-1, -10, -10, 7], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Vec2_ConstructorAndComputedProperty_ComputesExpectedSum()
    {
        float[] xs = [1f, 2f, 3f, 4f];
        var result = new float[4];

        CrossAssemblyKernels.LaunchVec2Kernel(xs, result, blockDimX: 4);

        // Vec2(x, 2) holds (2x, 3): Sum = 2x + 3; an aggregate initialization would give x + 2
        Assert.Equal([5f, 7f, 9f, 11f], result);
    }

    // Each thread writes tid * 10 and reads the next thread's value after the barrier
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Exchange_SuspendingLibraryFunction_ExchangesBufferValues()
    {
        var buffer = new int[4];
        var result = new int[4];

        CrossAssemblyKernels.LaunchExchangeKernel(buffer, result, blockDimX: 4);

        Assert.Equal([10, 20, 30, 0], result);
        Assert.Equal([0, 10, 20, 30], buffer);
    }

    // x^2 + 1 + 4, with the Float3 inside the library or in the kernel
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Float3_ParameterOfLibraryFunction_ComputesVectorLengthAndDotProduct()
    {
        float[] xs = [1f, 2f, 3f, 4f];
        var inside = new float[4];
        var kernel = new float[4];

        CrossAssemblyKernels.LaunchLength3Kernel(xs, inside, blockDimX: 4);
        CrossAssemblyKernels.LaunchDot3Kernel(xs, kernel, blockDimX: 4);

        Assert.Equal([6f, 9f, 14f, 21f], inside);
        Assert.Equal(inside, kernel);
    }

    // Mass = 3 * 2, Weight = Mass + x
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Particle_Float3FieldOfLibraryStruct_ComputesParticleValues()
    {
        float[] xs = [1f, 2f, 3f, 4f];
        var result = new float[4];

        CrossAssemblyKernels.LaunchParticleKernel(xs, result, blockDimX: 4);

        Assert.Equal([7f, 8f, 9f, 10f], result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Scaled_ConstructorCallingLibraryFunction_ScalesValues()
    {
        int[] values = [1, 2, 3, -4];
        var result = new int[4];

        CrossAssemblyKernels.LaunchScaledKernel(values, result, blockDimX: 4);

        Assert.Equal([2, 4, 6, -8], result);
    }

    // Inner.V = v + 1, Probe = 3 (v + 1): 301 (v + 1)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Alpha_StructsWithFieldsAndConstructors_ComputesCombinedValues()
    {
        int[] values = [0, 1, 2, -1];
        var result = new int[4];

        CrossAssemblyKernels.LaunchAlphaKernel(values, result, blockDimX: 4);

        Assert.Equal([301, 602, 903, 0], result);
    }

    // The offsets of a LayoutKind.Explicit struct from metadata come from its FieldLayout table, so A and B reach the
    // kernel in place, and the launch guard (which compares them with the runtime's) passes (#736)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SwapPair_ExplicitLayoutStruct_SwapsFields()
    {
        var result = new int[] { -1, -1 };

        CrossAssemblyKernels.LaunchSwapPairKernel(new SwapPair { A = 11, B = 22 }, result);
        CrossAssemblyKernels.LaunchSwapPairKernel(new SwapPair { A = 33, B = 44 }, result);

        Assert.Equal([33, 44], result);
    }

    // Pack = 1 from the ClassLayout table: 5 bytes, the int right after the byte (#736)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PackedPair_PackedLayoutStruct_ReadsTagAndValue()
    {
        var result = new int[] { -1, -1 };

        CrossAssemblyKernels.LaunchPackedPairKernel(new PackedPair { Tag = 7, Value = 0x01020304 }, result);

        Assert.Equal([7, 0x01020304], result);
    }

    // StructLayout.Size = 16 from the ClassLayout table: each element is read and written at a 16-byte stride, so the
    // device neither reads the neighbours' padding nor writes into it (#736)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sized_ArrayOfSizedStruct_CopiesAndZerosField()
    {
        Sized[] data = [new() { A = 5 }, new() { A = -6 }, new() { A = 7 }, new() { A = int.MaxValue }];
        var result = new int[4];

        CrossAssemblyKernels.LaunchSizedKernel(data, result, blockDimX: 4);

        Assert.Equal([5, -6, 7, int.MaxValue], result);
        Assert.Equal([0, 10, 20, 30], data.Select(static d => d.A));
    }

    // Bits and Value share offset 0 and Tag sits at 4: the device reads the float's bits and negates the float, which
    // flips only the sign bit and leaves the tag (#736)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IntFloatUnion_ArrayOfExplicitUnionStruct_ExtractsBitsAndNegatesFloat()
    {
        float[] values = [1f, -2.5f, 0f, float.MaxValue];
        var data = values.Select(static (v, i) => new IntFloatUnion { Value = v, Tag = 100 + i }).ToArray();
        var result = new int[4];

        CrossAssemblyKernels.LaunchIntFloatUnionKernel(data, result, blockDimX: 4);

        Assert.Equal(values.Select(BitConverter.SingleToInt32Bits), result);
        Assert.Equal(values.Select(static v => BitConverter.SingleToInt32Bits(-v)), data.Select(static d => d.Bits));
        Assert.Equal([100, 101, 102, 103], data.Select(static d => d.Tag));
    }
}
