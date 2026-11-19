#nullable enable
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Layout;

/// <summary>
/// A generic struct that names its own construction in a property (no cycle) nested in itself by value keeps its full
/// layout, so the launcher copies both levels.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Compiler)]
public class NestedGenericConstructionTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void NestedConstructionKernel_VectorOfVectors_ReadsTheInnerFields()
    {
        SwappableVector<SwappableVector<int>>[] values =
        [
            new SwappableVector<SwappableVector<int>>
            {
                X = new SwappableVector<int> { X = 1, Y = 2 },
                Y = new SwappableVector<int> { X = 3, Y = 4 }
            }
        ];
        int[] output = [0];

        NestedGenericConstructionKernels.LaunchNestedConstructionKernel(values, output, blockDimX: 1);

        Assert.Equal(34, output[0]);
    }
}
