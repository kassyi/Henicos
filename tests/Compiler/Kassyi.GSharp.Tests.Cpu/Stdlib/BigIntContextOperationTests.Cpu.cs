#nullable enable
using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

public partial class BigIntContextOperationTests
{
    // The device function reads the operand through Gpu.NotNull: the native CPU build names the place in the message
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ShiftLeft_NullLeftOperand_ThrowsNullReference()
    {
        var result = new uint[Words];

        var exception = Assert.Throws<NullReferenceException>(() =>
            BigIntContextOperationKernels.LaunchShiftLeftKernel(Context(null, null, result), 0, 1, blockDimX: 2));

        Assert.StartsWith(
            "G#: Gpu.NotNull was given a null array at GpuBigIntKernels.cu.cs(",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(new uint[Words], result);
    }
}
