
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_90_93_CoreSyntaxTests
:  TranspilerTestBase{
    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-90")]
    [InlineData(5, 2, 10)]       // 5 <= 10 -> else branch 5 times: 5 * 2 = 10
    [InlineData(9, 2, 10)]       // 9 <= 10 -> else branch 5 times: 5 * 2 = 10
    [InlineData(10, 2, 10)]      // 10 <= 10 (Strict Boundary) -> else branch: 5 * 2 = 10
    [InlineData(11, 2, 55)]      // 11 > 10 (Strict Boundary) -> if branch: 5 * 11 = 55
    [InlineData(15, 2, 75)]      // 15 > 10 -> if branch: 5 * 15 = 75
    [InlineData(-5, 3, 15)]      // Negative -> else branch: 5 * 3 = 15
    [InlineData(0, 0, 0)]        // Zero case
    [InlineData(int.MinValue, 2, 10)] // Min extreme value -> else branch: 5 * 2 = 10
    [InlineData(int.MaxValue / 10, 2, (int.MaxValue / 10) * 5)] // Max reasonable value without overflow
    public void TestComplexLogicKernel_BoundaryDataDriven(int a, int b, int expectedC)
    {
        Kassyi.GSharp.Tests.Helpers.ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        int[] aArray = [a];
        int[] bArray = [b];
        int[] cArray = [0];

        SYN_90_93_CoreSyntaxKernelsWrapper.Launch_ComplexLogicKernel(aArray, bArray, cArray);
        Assert.Equal(expectedC, cArray[0]);
    }


    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-91")]
    public void TestControlFlowKernel()
    {
        Kassyi.GSharp.Tests.Helpers.ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        // Test with 8 threads across multiple warps/lanes
        int[] dataArray = new int[8];

        SYN_90_93_CoreSyntaxKernelsWrapper.Launch_ControlFlowKernel(dataArray, blockDimX: 8);

        // tid 0: 10 + 3 + 100 = 113
        Assert.Equal(113, dataArray[0]);
        // tid 1: 20 + 3 + 200 = 223
        Assert.Equal(223, dataArray[1]);
        // tid 2: 20 + 3 + 200 = 223
        Assert.Equal(223, dataArray[2]);
        // tid 3..7: default branch -> 30 + 3 = 33
        for (int tid = 3; tid < 8; tid++)
        {
            Assert.Equal(33, dataArray[tid]);
        }
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-92")]
    public void TestStructKernel()
    {
        Kassyi.GSharp.Tests.Helpers.ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        // Empty array edge case
        Vector2[] emptyVectors = [];
        SYN_90_93_CoreSyntaxKernelsWrapper.Launch_StructKernel(emptyVectors, 0, blockDimX: 1);
        Assert.Empty(emptyVectors);

        // Single-element edge case
        Vector2[] singleVector = [new Vector2 { X = 5.0f, Y = 10.0f }];
        SYN_90_93_CoreSyntaxKernelsWrapper.Launch_StructKernel(singleVector, 1, blockDimX: 1);
        Assert.Equal(10.0f, singleVector[0].X);
        Assert.Equal(11.0f, singleVector[0].Y);

        // Multi-element case
        Vector2[] vectors = [
            new Vector2 { X = 1.0f, Y = 2.0f },
            new Vector2 { X = 3.0f, Y = 4.0f }
        ];

        SYN_90_93_CoreSyntaxKernelsWrapper.Launch_StructKernel(vectors, 2, blockDimX: 2);

        Assert.Equal(2.0f, vectors[0].X);
        Assert.Equal(3.0f, vectors[0].Y);
        Assert.Equal(6.0f, vectors[1].X);
        Assert.Equal(5.0f, vectors[1].Y);
    }

    [SkippableFact]
    [Trait("Category", "Alignment")]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-92")]
    public void TestStruct_MixedPaddingAndAlignment()
    {
        Kassyi.GSharp.Tests.Helpers.ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        // Empty array edge case
        MixedPaddingStruct[] emptyItems = [];
        SYN_90_93_CoreSyntaxKernelsWrapper.Launch_PaddedStructKernel(emptyItems, 0, blockDimX: 1);
        Assert.Empty(emptyItems);

        // Single-element edge case
        MixedPaddingStruct[] singleItem = [
            new MixedPaddingStruct { Flag1 = 1, ValueD = 2.0, ValueI = 3, Flag2 = 4 }
        ];
        SYN_90_93_CoreSyntaxKernelsWrapper.Launch_PaddedStructKernel(singleItem, 1, blockDimX: 1);
        Assert.Equal((byte)2, singleItem[0].Flag1);
        Assert.Equal(3.0, singleItem[0].ValueD);
        Assert.Equal(103, singleItem[0].ValueI);
        Assert.Equal((short)-1, singleItem[0].Flag2);

        // Multi-element case
        MixedPaddingStruct[] items = [
            new MixedPaddingStruct { Flag1 = 10, ValueD = 4.0, ValueI = 50, Flag2 = 20 },
            new MixedPaddingStruct { Flag1 = 250, ValueD = 10.0, ValueI = 200, Flag2 = 100 }
        ];

        SYN_90_93_CoreSyntaxKernelsWrapper.Launch_PaddedStructKernel(items, 2, blockDimX: 2);

        // Item 0
        Assert.Equal((byte)11, items[0].Flag1);
        Assert.Equal(6.0, items[0].ValueD);
        Assert.Equal(150, items[0].ValueI);
        Assert.Equal((short)15, items[0].Flag2);

        // Item 1
        Assert.Equal((byte)251, items[1].Flag1);
        Assert.Equal(15.0, items[1].ValueD);
        Assert.Equal(300, items[1].ValueI);
        Assert.Equal((short)95, items[1].Flag2);
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-93")]
    public void TestDeviceMethodKernel()
    {
        Kassyi.GSharp.Tests.Helpers.ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        // Single-element edge case
        int[] singleData = [42];
        SYN_90_93_CoreSyntaxKernelsWrapper.Launch_DeviceMethodKernel(singleData, blockDimX: 1);
        Assert.Equal(142, singleData[0]);

        // Multi-element case
        int[] dataArray = [5, 10];
        SYN_90_93_CoreSyntaxKernelsWrapper.Launch_DeviceMethodKernel(dataArray, blockDimX: 2);

        Assert.Equal(105, dataArray[0]);
        Assert.Equal(110, dataArray[1]);
    }
}
