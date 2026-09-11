
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

public partial class SYN_90_StructAggregateInitTests
    : TranspilerTestBase
{
    [GeneratedRegex(@"MyStruct_SYN90\s+_tmp\{\};\s*_tmp\.A\s*=\s*1;\s*_tmp\.B\s*=\s*2;")]
    private static partial Regex MyStructInitRegex();

    [GeneratedRegex(
        @"AStruct_SYN90\s+_tmp\{\};\s*_tmp\.B\s*=\s*\(\[\&\]\(\)\s*\{[\s\S]*?BStruct_SYN90\s+_tmp\{\};\s*_tmp\.X\s*=\s*1;\s*return\s*_tmp;\s*\}")]
    private static partial Regex NestedStructInitRegex();

    [SkippableFact]
    [Trait("FeatureId", "SYN-90")]
    [Trait("Category", "Syntax")]
    public void Test_SYN90_StructAggregateInitialization_HappyPath()
    {
        var (cudaSource, diagnostics) =
            RoslynTestUtility.TranspileMethod<TestClass_SYN90>(nameof(TestClass_SYN90.InitStruct));

        Assert.Empty(diagnostics);
        Assert.Matches(MyStructInitRegex(), cudaSource);
    }

    [SkippableFact]
    [Trait("FeatureId", "SYN-90")]
    [Trait("Category", "Syntax")]
    public void Test_SYN90_StructAggregateInitialization_EdgeCase_DeeplyNested()
    {
        var (cudaSource, diagnostics) =
            RoslynTestUtility.TranspileMethod<TestClass_SYN90>(nameof(TestClass_SYN90.InitNestedStruct));

        Assert.Empty(diagnostics);
        Assert.Matches(NestedStructInitRegex(), cudaSource);
    }
}

