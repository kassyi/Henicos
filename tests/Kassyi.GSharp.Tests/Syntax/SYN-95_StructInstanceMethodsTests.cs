namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_95_StructInstanceMethodsTests
{
    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-95")]
    [InlineData(0, 0, 1, 2)]
    [InlineData(10, -5, 1, 2)]
    [InlineData(int.MinValue, int.MaxValue, 1, 2)]
    public void Test_SYN95_ImplicitFieldAccessKernel_BoundaryDataDriven(int startX, int startY, int expectedX, int expectedY)
    {
        Point2D[] points = [new Point2D { X = startX, Y = startY }];
        SYN_95_StructInstanceMethodsKernelsWrapper.Launch_ImplicitFieldAccessKernel(points, blockDimX: 1);

        Assert.Equal(expectedX, points[0].X);
        Assert.Equal(expectedY, points[0].Y);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-95")]
    public void Test_SYN95_ExplicitThisMethodCallKernel()
    {
        Point2D[] points = [
            new Point2D { X = 5, Y = 10 },
            new Point2D { X = 20, Y = 30 }
        ];

        SYN_95_StructInstanceMethodsKernelsWrapper.Launch_ExplicitThisMethodCallKernel(points, blockDimX: 2);

        Assert.Equal(100, points[0].X);
        Assert.Equal(20, points[0].Y);

        Assert.Equal(100, points[1].X);
        Assert.Equal(40, points[1].Y);
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-95")]
    [InlineData(5, 10, 15)]
    [InlineData(-10, 20, 10)]
    [InlineData(0, 0, 0)]
    [InlineData(int.MaxValue - 100, 100, int.MaxValue)]
    public void Test_SYN95_MethodWithArgsAndReturnKernel(int startX, int val, int expectedResult)
    {
        Point2D[] points = [new Point2D { X = startX, Y = 0 }];
        int[] results = [0];

        SYN_95_StructInstanceMethodsKernelsWrapper.Launch_MethodWithArgsAndReturnKernel(points, results, val, blockDimX: 1);

        Assert.Equal(expectedResult, results[0]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-95")]
    public void Test_SYN95_IntBoxMethodKernel()
    {
        IntBox[] boxes = [
            new IntBox { Value = 10 },
            new IntBox { Value = 20 }
        ];

        SYN_95_StructInstanceMethodsKernelsWrapper.Launch_IntBoxMethodKernel(boxes, 42, blockDimX: 2);

        Assert.Equal(42, boxes[0].Value);
        Assert.Equal(42, boxes[1].Value);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-95")]
    public void Test_SYN95_DirectCallKernel()
    {
        int[] results = new int[4];

        SYN_95_StructInstanceMethodsKernelsWrapper.Launch_DirectCallKernel(results, blockDimX: 4);

        // tid 0: (5 + 0) * 3 = 15
        Assert.Equal(15, results[0]);
        // tid 1: (5 + 1) * 3 = 18
        Assert.Equal(18, results[1]);
        // tid 2: (5 + 2) * 3 = 21
        Assert.Equal(21, results[2]);
        // tid 3: (5 + 3) * 3 = 24
        Assert.Equal(24, results[3]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-95")]
    public void Test_SYN95_SameTypeParameterKernel()
    {
        MergeableStruct[] items = [
            new MergeableStruct { Value = 100 },
            new MergeableStruct { Value = 20 },
            new MergeableStruct { Value = 30 }
        ];

        MergeableStruct baseValue = new MergeableStruct { Value = 100 };

        SYN_95_StructInstanceMethodsKernelsWrapper.Launch_SameTypeParameterKernel(items, baseValue, blockDimX: 3);

        // tid 0: 100 + baseValue (100) = 200
        Assert.Equal(200, items[0].Value);
        // tid 1: 20 + baseValue (100) = 120
        Assert.Equal(120, items[1].Value);
        // tid 2: 30 + baseValue (100) = 130
        Assert.Equal(130, items[2].Value);
    }
}
