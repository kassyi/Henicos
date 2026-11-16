#nullable enable
using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class SharedMemoryZeroSizeTests
{
    // The element 0 of an empty declaration is outside it. Only the managed path checks a SharedMemory<T> index today;
    // the native and CUDA paths do not check it for any count, which the bounds checks of #718 add
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void EmptyDeclaration_ReadElementZeroManaged_ThrowsIndexOutOfRangeException()
    {
        var output = new int[1];

        Assert.Throws<IndexOutOfRangeException>(() =>
            Run(true, () => SharedMemoryZeroSizeKernels.LaunchReadEmptyKernel(output, blockDimX: 1)));
    }
}
