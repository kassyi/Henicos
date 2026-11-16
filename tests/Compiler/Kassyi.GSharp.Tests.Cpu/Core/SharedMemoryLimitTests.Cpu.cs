using System;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class SharedMemoryLimitTests
{
    // A static declaration of 2^32 + 128 bytes: the managed path computed the size in int, got 128 bytes and threw
    // IndexOutOfRangeException at an element inside the declared count. It now fails as the native path does when it
    // cannot allocate the buffer, before allocating anything (#1280 review)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StaticDeclaration_MoreThanIntMaxValueBytesManaged_ThrowsInvalidOperationException()
    {
        var output = new double[1];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Run(true, () => SharedMemoryHugeStaticKernels.LaunchHugeStaticKernel(output, blockDimX: 1)));

        Assert.Equal("G#: the static shared memory of a declaration could not be allocated", exception.Message);
    }
}
