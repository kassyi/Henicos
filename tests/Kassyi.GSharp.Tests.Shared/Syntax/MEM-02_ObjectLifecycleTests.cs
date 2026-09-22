using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

[Trait("Category", "GpuExecution")]
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
    [InlineData(99, 1, 99, 0)]
    [InlineData(int.MaxValue, 1, int.MaxValue, 1)]
    [InlineData(int.MinValue, 1, int.MinValue, 0)]
    public void Test_MEM02_LifecycleUpdateKernel_BoundaryDataDriven(int initialVal, int multiplier, int expectedVal, int expectedStatus)
    {
        LifecycleObjectState[] states = [
            new LifecycleObjectState { Id = 1, Value = initialVal, Status = 0 }
        ];

        MEM_02_ObjectLifecycleKernelsWrapper.LaunchKernel_LifecycleUpdate(states, 1, multiplier, blockDimX: 1);

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
        MEM_02_ObjectLifecycleKernelsWrapper.LaunchKernel_LifecycleUpdate(emptyStates, 0, 2, blockDimX: 1);
        Assert.Empty(emptyStates);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-02")]
    public void Test_MEM02_FactoryInitializationKernel_EmptyArrayEdgeCase()
    {
        int[] inputs = [];
        LifecycleObjectState[] outputs = [];
        MEM_02_ObjectLifecycleKernelsWrapper.LaunchKernel_FactoryInitialization(inputs, outputs, 0, blockDimX: 1);
        Assert.Empty(outputs);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-02")]
    public void Test_MEM02_FactoryInitializationKernel_SingleElementEdgeCase()
    {
        int[] inputs = [42];
        LifecycleObjectState[] outputs = new LifecycleObjectState[1];
        MEM_02_ObjectLifecycleKernelsWrapper.LaunchKernel_FactoryInitialization(inputs, outputs, 1, blockDimX: 1);
        Assert.Single(outputs);
        Assert.Equal(1, outputs[0].Id);
        Assert.Equal(42, outputs[0].Value);
        Assert.Equal(1, outputs[0].Status);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "MEM-02")]
    public void Test_MEM02_FactoryInitializationKernel_MultiElement()
    {
        int[] inputs = [-5, 0, 42, 150];
        LifecycleObjectState[] outputs = new LifecycleObjectState[4];

        MEM_02_ObjectLifecycleKernelsWrapper.LaunchKernel_FactoryInitialization(inputs, outputs, 4, blockDimX: 4);

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
