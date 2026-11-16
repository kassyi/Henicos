#nullable enable
using System;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanWriteTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class SpanCopyTests
{
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void CopyTo_DestinationTooShortInCuda_ThrowsArgumentExceptionWithoutInnerException()
    {
        int[] src = [1, 2, 3, 4];
        int[] dst = [90, 91, 92];
        // The failing thread records the error and exits; the launcher throws the exception the CPU paths throw
        var ex = Assert.Throws<ArgumentException>(() =>
            SpanWriteKernels.LaunchCopyToDirectKernel(src, dst, gridDimX: GridDimX, blockDimX: BlockDimX));
        Assert.Null(ex.InnerException);
        Assert.Equal([90, 91, 92], dst);
    }
}
