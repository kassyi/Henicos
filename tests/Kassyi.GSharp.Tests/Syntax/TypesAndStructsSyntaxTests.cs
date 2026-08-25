using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class TypesAndStructsSyntaxTests
{
    [Fact(Skip = "未実装: SYN-17 (Target-typed new() のネスト完全対応)")]
    [Trait("RoadmapId", "SYN-17")]
    [Trait("Category", "Syntax")]
    public void Test_SYN17_TargetTypedNew_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-18 (構造化束縛・タプル宣言分解)")]
    [Trait("RoadmapId", "SYN-18")]
    [Trait("Category", "Syntax")]
    public void Test_SYN18_TupleDeconstruction_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-19 (構造体のプライマリコンストラクタ)")]
    [Trait("RoadmapId", "SYN-19")]
    [Trait("Category", "Syntax")]
    public void Test_SYN19_PrimaryConstructor_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-20 (record struct と with 式)")]
    [Trait("RoadmapId", "SYN-20")]
    [Trait("Category", "Syntax")]
    public void Test_SYN20_RecordStructWithExpression_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-21 (構造体内インデクサ this[int index])")]
    [Trait("RoadmapId", "SYN-21")]
    [Trait("Category", "Syntax")]
    public void Test_SYN21_StructIndexer_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-22 (構造体内演算子オーバーロード)")]
    [Trait("RoadmapId", "SYN-22")]
    [Trait("Category", "Syntax")]
    public void Test_SYN22_StructOperatorOverload_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-23 (readonly struct / readonly メソッド)")]
    [Trait("RoadmapId", "SYN-23")]
    [Trait("Category", "Syntax")]
    public void Test_SYN23_ReadonlyStruct_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-24 (enum の基底型指定とビットフラグ [Flags])")]
    [Trait("RoadmapId", "SYN-24")]
    [Trait("Category", "Syntax")]
    public void Test_SYN24_EnumBaseTypeAndFlags_Transpilation()
    {
    }
}
