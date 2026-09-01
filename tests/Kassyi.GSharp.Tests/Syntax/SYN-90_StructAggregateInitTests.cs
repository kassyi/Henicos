using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Syntax;

public struct MyStruct_SYN90
{
    public int A;
    public int B;
}

public struct BStruct_SYN90
{
    public int X;
}

public struct AStruct_SYN90
{
    public BStruct_SYN90 B;
}

public class TestClass_SYN90
{
    public static void InitStruct()
    {
        MyStruct_SYN90 s = new MyStruct_SYN90 { A = 1, B = 2 };
    }

    public static void InitNestedStruct()
    {
        AStruct_SYN90 a = new AStruct_SYN90 { B = new BStruct_SYN90 { X = 1 } };
    }
}

public class SYN_90_StructAggregateInitTests
{
    [Fact]
    [Trait("FeatureId", "SYN-90")]
    [Trait("Category", "Syntax")]
    public void Test_SYN90_StructAggregateInitialization_HappyPath()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TestClass_SYN90>(nameof(TestClass_SYN90.InitStruct));

        Assert.Empty(diagnostics);
        Assert.Contains("MyStruct_SYN90 _tmp", cudaSource);
        Assert.Contains("_tmp.A = 1;", cudaSource);
        Assert.Contains("_tmp.B = 2;", cudaSource);
    }

    [Fact]
    [Trait("FeatureId", "SYN-90")]
    [Trait("Category", "Syntax")]
    public void Test_SYN90_StructAggregateInitialization_EdgeCase_DeeplyNested()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<TestClass_SYN90>(nameof(TestClass_SYN90.InitNestedStruct));

        Assert.Empty(diagnostics);
        Assert.Contains("AStruct_SYN90 _tmp", cudaSource);
        Assert.Contains("_tmp.B =", cudaSource);
        Assert.Contains("BStruct_SYN90 _tmp", cudaSource);
        Assert.Contains("_tmp.X = 1;", cudaSource);
    }
}

