using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class ConcurrencyAndWarpSyntaxTests
{
    [Fact(Skip = "未実装: SYN-46 (ワープシャッフル・同期プリミティブ)")]
    [Trait("RoadmapId", "SYN-46")]
    [Trait("Category", "Syntax")]
    public void Test_SYN46_WarpPrimitives_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-48 (グリッド協調グループ同期 CooperativeGroups)")]
    [Trait("RoadmapId", "SYN-48")]
    [Trait("Category", "Syntax")]
    public void Test_SYN48_CooperativeGroups_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-50 (非同期コピー / TMA Tensor Memory Accelerator)")]
    [Trait("RoadmapId", "SYN-50")]
    [Trait("Category", "Syntax")]
    public void Test_SYN50_AsyncCopyTma_Transpilation()
    {
    }
}
