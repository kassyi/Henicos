
namespace Kassyi.GSharp.Tests.Shared.Syntax;

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

public partial class StructAggregateInitTests
    : TranspilerTestBase
{
    [GeneratedRegex(@"MyStruct_SYN90\s+__gsharp_tmp\{\};\s*__gsharp_tmp\.A\s*=\s*1;\s*__gsharp_tmp\.B\s*=\s*2;")]
    private static partial Regex MyStructInitRegex();

    [GeneratedRegex(
        @"AStruct_SYN90\s+__gsharp_tmp\{\};\s*__gsharp_tmp\.B\s*=\s*\(\[\&\]\(\)\s*\{[\s\S]*?BStruct_SYN90\s+__gsharp_tmp\{\};\s*__gsharp_tmp\.X\s*=\s*1;\s*return\s*__gsharp_tmp;\s*\}")]
    private static partial Regex NestedStructInitRegex();

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    public void Test_StructAggregateInitialization_HappyPath()
    {
        var (cudaSource, diagnostics) =
            RoslynTestUtility.TranspileMethod<TestClass_SYN90>(nameof(TestClass_SYN90.InitStruct));

        Assert.Empty(diagnostics);
        Assert.Matches(MyStructInitRegex(), cudaSource);
    }

    [SkippableFact]
    [Category(TestCategory.Syntax)]
    public void Test_StructAggregateInitialization_EdgeCase_DeeplyNested()
    {
        var (cudaSource, diagnostics) =
            RoslynTestUtility.TranspileMethod<TestClass_SYN90>(nameof(TestClass_SYN90.InitNestedStruct));

        Assert.Empty(diagnostics);
        Assert.Matches(NestedStructInitRegex(), cudaSource);
    }
}

