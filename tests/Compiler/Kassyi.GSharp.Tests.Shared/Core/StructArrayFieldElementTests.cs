using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>A struct whose array field holds user structs: the element's shadow carries them to the device.</summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
public class StructArrayFieldElementTests
{
    // CPU passes the struct in its managed layout until L4
#if GSHARP_CPU
    [Fact(Skip = "L4")]
#else
    [Fact]
#endif
    [Scenario(TestScenario.HappyPath)]
    public void ArrayFieldOfUserStructs_ByRef_IsCopiedBothWays()
    {
        var h = new ElemArrayHolder
        {
            Tag = 3,
            Items = [new PlainElem { X = 1, Y = 2 }, new PlainElem { X = 3, Y = 4 }]
        };
        var o = new int[2];

        StructArrayFieldElementKernels.LaunchElemArrayKernel(ref h, o);

        Assert.Equal([3, 4], o);
        Assert.Equal(42, h.Items[0].X);
        Assert.Equal(3, h.Items[1].X);
    }
}
