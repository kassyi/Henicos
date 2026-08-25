using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class StringsAndAttributesSyntaxTests
{
    [Fact(Skip = "未実装: SYN-25 (文字列補間 $\"\" のデバッグ出力化)")]
    [Trait("RoadmapId", "SYN-25")]
    [Trait("Category", "Syntax")]
    public void Test_SYN25_StringInterpolationPrintf_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-26 (UTF-8 文字列リテラル \"...\"u8)")]
    [Trait("RoadmapId", "SYN-26")]
    [Trait("Category", "Syntax")]
    public void Test_SYN26_Utf8StringLiteral_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-27 (nameof(...) 式の静的文字列解決)")]
    [Trait("RoadmapId", "SYN-27")]
    [Trait("Category", "Syntax")]
    public void Test_SYN27_NameOfExpression_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-28 ([MethodImpl(AggressiveInlining)] マッピング)")]
    [Trait("RoadmapId", "SYN-28")]
    [Trait("Category", "Syntax")]
    public void Test_SYN28_AggressiveInlining_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-29 ([LoopUnroll(N)] 属性によるアンロール制御)")]
    [Trait("RoadmapId", "SYN-29")]
    [Trait("Category", "Syntax")]
    public void Test_SYN29_LoopUnrollAttribute_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-30 (ジェネリック構造体 struct Vector<T>)")]
    [Trait("RoadmapId", "SYN-30")]
    [Trait("Category", "Syntax")]
    public void Test_SYN30_GenericStruct_Transpilation()
    {
    }
}
