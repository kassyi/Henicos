namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_95_StructInstanceMethodsTests
{
    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-95")]
    [InlineData(1, 1, 1, 2, 1)]
    [InlineData(-1, -1, 1, 2, 1)]
    [InlineData(unchecked((int)0xAAAAAAAA), unchecked((int)0x55555555), 1, 2, 1)]
    [InlineData(0, 0, 1, 2, 1)]
    [InlineData(10, -5, 1, 2, 1)]
    [InlineData(int.MinValue, int.MaxValue, 1, 2, 1)]
    [InlineData(0, 0, 1, 2, 0)]
    public void Test_SYN95_ImplicitFieldAccessKernel_BoundaryDataDriven(int startX, int startY, int expectedX, int expectedY, int length)
    {
        Point2D[] points = length > 0 ? [new Point2D { X = startX, Y = startY }] : [];
        SYN_95_StructInstanceMethodsKernelsWrapper.Launch_ImplicitFieldAccessKernel(points, blockDimX: length == 0 ? 1u : (uint)length);

        if (length == 0)
        {
            Assert.Empty(points);
        }
        else
        {
            Assert.Equal(expectedX, points[0].X);
            Assert.Equal(expectedY, points[0].Y);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-95")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Test_SYN95_ExplicitThisMethodCallKernel(int length)
    {
        Point2D[] points = new Point2D[length];
        if (length > 0) points[0] = new Point2D { X = 5, Y = 10 };
        if (length > 1) points[1] = new Point2D { X = 20, Y = 30 };

        SYN_95_StructInstanceMethodsKernelsWrapper.Launch_ExplicitThisMethodCallKernel(points, blockDimX: length == 0 ? 1u : (uint)length);

        if (length == 0)
        {
            Assert.Empty(points);
        }
        if (length > 0)
        {
            Assert.Equal(100, points[0].X);
            Assert.Equal(20, points[0].Y);
        }
        if (length > 1)
        {
            Assert.Equal(100, points[1].X);
            Assert.Equal(40, points[1].Y);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-95")]
    [InlineData(1, 1, 2, 1)]
    [InlineData(-1, -1, -2, 1)]
    [InlineData(unchecked((int)0xAAAAAAAA), unchecked((int)0x55555555), -1, 1)]
    [InlineData(5, 10, 15, 1)]
    [InlineData(-10, 20, 10, 1)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(int.MaxValue - 100, 100, int.MaxValue, 1)]
    [InlineData(int.MinValue, 1, int.MinValue + 1, 1)]
    [InlineData(0, 0, 0, 0)]
    public void Test_SYN95_MethodWithArgsAndReturnKernel(int startX, int val, int expectedResult, int length)
    {
        Point2D[] points = length > 0 ? [new Point2D { X = startX, Y = 0 }] : [];
        int[] results = new int[length];

        SYN_95_StructInstanceMethodsKernelsWrapper.Launch_MethodWithArgsAndReturnKernel(points, results, val, blockDimX: length == 0 ? 1u : (uint)length);

        if (length == 0)
        {
            Assert.Empty(results);
        }
        else
        {
            Assert.Equal(expectedResult, results[0]);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-95")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Test_SYN95_IntBoxMethodKernel(int length)
    {
        IntBox[] boxes = new IntBox[length];
        if (length > 0) boxes[0] = new IntBox { Value = 10 };
        if (length > 1) boxes[1] = new IntBox { Value = 20 };

        SYN_95_StructInstanceMethodsKernelsWrapper.Launch_IntBoxMethodKernel(boxes, 42, blockDimX: length == 0 ? 1u : (uint)length);

        if (length == 0)
        {
            Assert.Empty(boxes);
        }
        if (length > 0)
        {
            Assert.Equal(42, boxes[0].Value);
        }
        if (length > 1)
        {
            Assert.Equal(42, boxes[1].Value);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-95")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Test_SYN95_DirectCallKernel(int length)
    {
        int[] results = new int[length];

        SYN_95_StructInstanceMethodsKernelsWrapper.Launch_DirectCallKernel(results, blockDimX: length == 0 ? 1u : (uint)length);

        if (length == 0)
        {
            Assert.Empty(results);
        }
        if (length > 0)
        {
            Assert.Equal(15, results[0]);
        }
        if (length > 1)
        {
            Assert.Equal(18, results[1]);
            Assert.Equal(21, results[2]);
            Assert.Equal(24, results[3]);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-95")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Test_SYN95_SameTypeParameterKernel(int length)
    {
        MergeableStruct[] items = new MergeableStruct[length];
        if (length > 0) items[0] = new MergeableStruct { Value = 100 };
        if (length > 1) 
        {
            items[1] = new MergeableStruct { Value = 20 };
            items[2] = new MergeableStruct { Value = 30 };
        }

        MergeableStruct baseValue = new MergeableStruct { Value = 100 };

        SYN_95_StructInstanceMethodsKernelsWrapper.Launch_SameTypeParameterKernel(items, baseValue, blockDimX: length == 0 ? 1u : (uint)length);

        if (length == 0)
        {
            Assert.Empty(items);
        }
        if (length > 0)
        {
            Assert.Equal(200, items[0].Value);
        }
        if (length > 1)
        {
            Assert.Equal(120, items[1].Value);
            Assert.Equal(130, items[2].Value);
        }
    }
}
