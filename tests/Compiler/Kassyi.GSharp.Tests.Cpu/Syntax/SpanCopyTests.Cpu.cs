#nullable enable
using System;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Syntax.SpanWriteTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class SpanCopyTests
{
    // CopyToShort on CPU: throws ArgumentException and leaves destination untouched
    [Theory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public void CopyTo_DestinationTooShort_ThrowsArgumentExceptionAndPreservesDestination(bool managed)
    {
        int[] src = [1, 2, 3, 4];
        int[] dst = [90, 91, 92];
        var before = (int[])dst.Clone();

        Assert.Throws<ArgumentException>(() => Run(
            managed,
            () => SpanWriteKernels.LaunchCopyToDirectKernel(src, dst, gridDimX: GridDimX, blockDimX: BlockDimX)
        ));
        Assert.Equal(before, dst);
    }
}
