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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UsingStaticInGridStrideLoop_MatchesCSharp(bool throughDeviceFunction)
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        if (throughDeviceFunction)
        {
            ManagedQualificationKernels.LaunchGridStrideDeviceFunctionKernel(values, result, values.Length, blockDimX: 4);
        }
        else
        {
            ManagedQualificationKernels.LaunchGridStrideKernel(values, result, values.Length, blockDimX: 4);
        }

        Assert.Equal(ExpectedGridStride(values), result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UsingStaticInSingleThread_MatchesCSharp(bool throughDeviceFunction)
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        if (throughDeviceFunction)
        {
            ManagedQualificationKernels.LaunchSingleThreadDeviceFunctionKernel(values, result, blockDimX: 4);
        }
        else
        {
            ManagedQualificationKernels.LaunchSingleThreadKernel(values, result, blockDimX: 4);
        }

        Assert.Equal(ExpectedSingleThread(values), result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UsingStaticInForUnroll_MatchesCSharp(bool throughDeviceFunction)
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        if (throughDeviceFunction)
        {
            ManagedQualificationKernels.LaunchForUnrollDeviceFunctionKernel(values, result, blockDimX: 4);
        }
        else
        {
            ManagedQualificationKernels.LaunchForUnrollKernel(values, result, blockDimX: 4);
        }

        Assert.Equal(ExpectedForUnroll(values), result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UsingStaticInStackLinqLambda_MatchesCSharp(bool throughDeviceFunction)
    {
        int[] values = [1, 2, 3, 4];
        var result = new int[4];

        if (throughDeviceFunction)
        {
            ManagedQualificationKernels.LaunchStackLinqDeviceFunctionKernel(values, result, blockDimX: 4);
        }
        else
        {
            ManagedQualificationKernels.LaunchStackLinqKernel(values, result, blockDimX: 4);
        }

        Assert.Equal(ExpectedStackLinq(values), result);
    }

    // The C# results of the loop and query kernels, written without the macros: Thrice(x) = 3x, Offset = 5, Count = 4

    // result[i] = Thrice(values[i]) + Offset + Thrice(Offset), then + Count
    internal static int[] ExpectedGridStride(int[] values)
    {
        var expected = new int[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            expected[i] = 3 * values[i] + 5 + 3 * 5 + 4;
        }

        return expected;
    }

    // result[0] = Thrice(values[0]) + Offset, by one thread; the others stay 0
    internal static int[] ExpectedSingleThread(int[] values)
    {
        var expected = new int[values.Length];
        expected[0] = 3 * values[0] + 5;
        return expected;
    }

    // Thrice(Offset) + the sum over k < Count of Thrice(values[tid] + k) + Offset
    internal static int[] ExpectedForUnroll(int[] values)
    {
        var expected = new int[values.Length];
        for (var tid = 0; tid < values.Length; tid++)
        {
            var sum = 3 * 5;
            for (var k = 0; k < 4; k++)
            {
                sum += 3 * (values[tid] + k) + 5;
            }

            expected[tid] = sum;
        }

        return expected;
    }

    // The sum over the values with Thrice(x) > Offset of x + Count, plus tid and Thrice(Count)
    internal static int[] ExpectedStackLinq(int[] values)
    {
        var total = 0;
        foreach (var x in values)
        {
            if (3 * x > 5)
            {
                total += x + 4;
            }
        }

        var expected = new int[values.Length];
        for (var tid = 0; tid < values.Length; tid++)
        {
            expected[tid] = total + tid + 3 * 4;
        }

        return expected;
    }
}
