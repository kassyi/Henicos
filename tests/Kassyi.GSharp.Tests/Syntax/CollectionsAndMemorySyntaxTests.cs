using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class CollectionsAndMemorySyntaxTests
{
    [Fact(Skip = "未実装: SYN-11 (Index & Range 構文 ^1, [1..5])")]
    [Trait("RoadmapId", "SYN-11")]
    [Trait("Category", "Syntax")]
    public void Test_SYN11_IndexAndRange_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-12 (コレクション式 [1, 2, 3] / スプレッド ..)")]
    [Trait("RoadmapId", "SYN-12")]
    [Trait("Category", "Syntax")]
    public void Test_SYN12_CollectionExpression_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-13 (stackalloc と固定長スタック配列)")]
    [Trait("RoadmapId", "SYN-13")]
    [Trait("Category", "Syntax")]
    public void Test_SYN13_Stackalloc_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-15 (Span<T> / ReadOnlySpan<T> のカーネル内使用)")]
    [Trait("RoadmapId", "SYN-15")]
    [Trait("Category", "Syntax")]
    public void Test_SYN15_SpanUsage_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-16 (2次元インデクサ / 多次元配列)")]
    [Trait("RoadmapId", "SYN-16")]
    [Trait("Category", "Syntax")]
    public void Test_SYN16_MultidimensionalIndexer_Transpilation()
    {
    }
}
