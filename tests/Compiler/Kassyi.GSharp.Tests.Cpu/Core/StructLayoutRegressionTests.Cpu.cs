using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class StructLayoutRegressionSequentialTests
{
    // The CPU launcher converts an [Out] array through a shadow rented from ArrayPool, which still holds the bytes of an
    // earlier launch of the same size (R7 first leaves 1 to 6 in it); the shadow is cleared, so what the kernel does not
    // write reads as 0. CUDA leaves an unwritten [Out] element undefined (device memory is not cleared)
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void R7b_OutArrayOfConvertedStructs_AfterAnotherLaunch_ReadsUnwrittenFieldsAsZero()
    {
        var o = new int[4];
        StructLayoutRegressionKernels.LaunchAutoArrayKernel(
            [new() { A = 1, B = 2, C = 3 }, new() { A = 4, B = 5, C = 6 }],
            o);
        var outs = new LayoutReproAutoLayout[]
        {
            new() { A = 11, B = 12, C = 13 },
            new() { A = 14, B = 15, C = 16 }
        };

        StructLayoutRegressionKernels.LaunchOutAutoArrayKernel(outs);

        Assert.Equal(((byte)7, 0L, (byte)0), (outs[0].A, outs[0].B, outs[0].C));
        Assert.Equal(((byte)0, 0L, (byte)0), (outs[1].A, outs[1].B, outs[1].C));
    }
}
