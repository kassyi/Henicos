using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

// GC.GetGCMemoryInfo counts the pinned objects of the whole process, so no other test may run beside these
[Collection(TestCollections.DeviceExclusive)]
public class StructLayoutPinTests
{
    // The CPU launcher pins the array fields of a converted struct; every launch must free the pins, also when the
    // native code reports an error after the marshaling (R4g). A launch that leaked would leave 2 pinned arrays
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ConvertedStructWithArrayField_RepeatedLaunches_LeaveNoPinnedArrays()
    {
        var o = new int[3];
        StructLayoutRegressionKernels.LaunchBagByValKernel(new LayoutReproBag { N = 3, Data = [10, 20, 30] }, o);
        var before = CountPinnedObjects();

        for (var i = 0; i < 20; i++)
        {
            StructLayoutRegressionKernels.LaunchBagByValKernel(new LayoutReproBag { N = 3, Data = [10, 20, 30] }, o);
        }

        for (var i = 0; i < 20; i++)
        {
            Assert.ThrowsAny<Exception>(
                () => StructLayoutRegressionKernels.LaunchBagNativeErrorKernel(new LayoutReproBag { N = 1, Data = [1] }, o));
        }

        var after = CountPinnedObjects();
        Assert.True(after <= before + 5, $"{before} pinned objects before the launches, {after} after");
    }

    private static long CountPinnedObjects()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetGCMemoryInfo().PinnedObjectsCount;
    }
}
