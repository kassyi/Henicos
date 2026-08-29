using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class MEM_02_ObjectLifecycleTests
{
    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-02")]
    [InlineData(10, 2, 20, 0)]
    [InlineData(60, 2, 120, 1)]
    [InlineData(0, 5, 0, 0)]
    [InlineData(-10, 3, -30, 0)]
    [InlineData(100, 1, 100, 0)]
    [InlineData(101, 1, 101, 1)]
    public void Test_MEM02_LifecycleUpdateKernel_BoundaryDataDriven(int initialVal, int multiplier, int expectedVal, int expectedStatus)
    {
        LifecycleObjectState[] states = [
            new LifecycleObjectState { Id = 1, Value = initialVal, Status = 0 }
        ];

        MEM_02_ObjectLifecycleKernelsWrapper.Launch_Kernel_LifecycleUpdate(states, 1, multiplier, blockDimX: 1);

        Assert.Single(states);
        Assert.Equal(expectedVal, states[0].Value);
        Assert.Equal(expectedStatus, states[0].Status);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-02")]
    public void Test_MEM02_LifecycleUpdateKernel_EmptyArrayEdgeCase()
    {
        LifecycleObjectState[] emptyStates = [];
        MEM_02_ObjectLifecycleKernelsWrapper.Launch_Kernel_LifecycleUpdate(emptyStates, 0, 2, blockDimX: 1);
        Assert.Empty(emptyStates);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-02")]
    public void Test_MEM02_FactoryInitializationKernel_MultiElement()
    {
        int[] inputs = [-5, 0, 42, 150];
        LifecycleObjectState[] outputs = new LifecycleObjectState[4];

        MEM_02_ObjectLifecycleKernelsWrapper.Launch_Kernel_FactoryInitialization(inputs, outputs, 4, blockDimX: 4);

        Assert.Equal(1, outputs[0].Id);
        Assert.Equal(-5, outputs[0].Value);
        Assert.Equal(-1, outputs[0].Status);

        Assert.Equal(2, outputs[1].Id);
        Assert.Equal(0, outputs[1].Value);
        Assert.Equal(0, outputs[1].Status);

        Assert.Equal(3, outputs[2].Id);
        Assert.Equal(42, outputs[2].Value);
        Assert.Equal(1, outputs[2].Status);

        Assert.Equal(4, outputs[3].Id);
        Assert.Equal(150, outputs[3].Value);
        Assert.Equal(1, outputs[3].Status);
    }
}
