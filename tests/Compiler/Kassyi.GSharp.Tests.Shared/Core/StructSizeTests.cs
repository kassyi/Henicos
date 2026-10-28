using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Structs with <c>[StructLayout(Size = n)]</c> (#621): the launcher reads the elements at the CLR stride, which is
/// <c>max(Size, end of the last field)</c>, and the device struct is padded up to it.
/// </summary>
public class StructSizeTests
{
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void SizeMultipleOfAlignment_Array_ReadsElementsAtTheHostStride()
    {
        var data = new StructSizePaddedInt[] { new() { A = 10 }, new() { A = 20 } };
        var o = new int[2];

        StructSizeKernels.LaunchReadSecondPaddedKernel(data, o);

        Assert.Equal([20, 16], o);
        Assert.Equal(21, data[0].A);
        Assert.Equal(20, data[1].A);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void PackedWithTailPadding_Array_ReadsElementsAtTheHostStride()
    {
        var data = new StructSizePacked[] { new() { B = 1, I = 10 }, new() { B = 2, I = 20 } };
        var o = new int[2];

        StructSizeKernels.LaunchReadSecondPackedKernel(data, o);

        Assert.Equal([20, 7], o);
        Assert.Equal(21, data[0].I);
        Assert.Equal(1, data[0].B);
        Assert.Equal(2, data[1].B);
        Assert.Equal(20, data[1].I);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void SizeMultipleOfAlignment_ByValue_PassesTheField()
    {
        var o = new int[1];

        StructSizeKernels.LaunchReadPaddedByValueKernel(new StructSizePaddedInt { A = 42 }, o);

        Assert.Equal(42, o[0]);
    }

    // The device rounds the 6-byte struct up to 8 bytes; CUDA converts the array element by element (GSH0080). The CPU
    // launcher does not convert struct arrays yet (GSH0082, #511)
#if GSHARP_CPU
    [Fact(Skip = "The CPU launcher does not convert struct arrays yet (#511)")]
#else
    [Fact]
#endif
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void SizeNotMultipleOfAlignment_Array_ConvertsElementsToTheDeviceStride()
    {
        var data = new StructSizeOddInt[] { new() { A = 10 }, new() { A = 20 } };
        var o = new int[2];

        StructSizeKernels.LaunchReadSecondOddKernel(data, o);

        Assert.Equal([20, 8], o);
        Assert.Equal(21, data[0].A);
        Assert.Equal(20, data[1].A);
    }
}
