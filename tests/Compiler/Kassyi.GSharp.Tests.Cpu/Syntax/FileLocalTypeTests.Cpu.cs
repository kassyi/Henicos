#nullable enable
using System;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class FileLocalTypeTests
{
    // The managed copy of the kernel would hold the local across the barrier, which a generated file cannot name
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void LaunchBarrierScaleKernel_ManagedPathOfAFileLocalStructAcrossBarrier_Throws()
    {
        int[] values = [0, 1, 2, 3, 4];

        using (GpuExecution.Use(KernelExecution.Managed))
        {
            var exception = Assert.Throws<NotSupportedException>(
                () => FileLocalTypeKernels.LaunchBarrierScaleKernel(values, 3, blockDimX: 5));

            Assert.Contains(
                "BarrierScaleKernel cannot run on the managed path of the CPU dialect",
                exception.Message,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "'Kassyi.GSharp.Tests.Shared.Syntax.FileLocalScale'",
                exception.Message,
                StringComparison.Ordinal
            );
        }

        Assert.Equal([0, 1, 2, 3, 4], values);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void LaunchDivideKernel_ManagedPathOfAFileLocalStructAndDivision_ThrowsNamingTheDivision()
    {
        int[] values = [7, 8, 9, 10, 11];

        using (GpuExecution.Use(KernelExecution.Managed))
        {
            var exception = Assert.Throws<NotSupportedException>(
                () => FileLocalTypeKernels.LaunchDivideKernel(values, 3, blockDimX: 5));

            Assert.Contains(
                "DivideKernel cannot run on the managed path of the CPU dialect",
                exception.Message,
                StringComparison.Ordinal
            );
            Assert.Contains("an integer division or remainder", exception.Message, StringComparison.Ordinal);
            Assert.Contains(
                "'Kassyi.GSharp.Tests.Shared.Syntax.FileLocalScale'",
                exception.Message,
                StringComparison.Ordinal
            );
        }

        Assert.Equal([7, 8, 9, 10, 11], values);
    }
}
