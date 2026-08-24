using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class MicroModifiersSyntaxTests
{
    [Fact(Skip = "未実装: SYN-79 (ファイルローカル型 file struct)")]
    [Trait("RoadmapId", "SYN-79")]
    [Trait("Category", "Syntax")]
    public void Test_SYN79_FileLocalTypes_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-80 (params Span<T> / params ReadOnlySpan<T>)")]
    [Trait("RoadmapId", "SYN-80")]
    [Trait("Category", "Syntax")]
    public void Test_SYN80_ParamsSpan_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-81 (ref readonly 戻り値)")]
    [Trait("RoadmapId", "SYN-81")]
    [Trait("Category", "Syntax")]
    public void Test_SYN81_RefReadonlyReturns_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-82 (volatile フィールド修飾子)")]
    [Trait("RoadmapId", "SYN-82")]
    [Trait("Category", "Syntax")]
    public void Test_SYN82_VolatileModifier_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-83 ([module: SkipLocalsInit] 属性)")]
    [Trait("RoadmapId", "SYN-83")]
    [Trait("Category", "Syntax")]
    public void Test_SYN83_SkipLocalsInitAttribute_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-84 (タプル宣言分解の foreach ループ)")]
    [Trait("RoadmapId", "SYN-84")]
    [Trait("Category", "Syntax")]
    public void Test_SYN84_ForeachTupleDeconstruction_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-86 (関数ポインタの呼出規約・GC遷移修飾)")]
    [Trait("RoadmapId", "SYN-86")]
    [Trait("Category", "Syntax")]
    public void Test_SYN86_FunctionPointerCallingConventions_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-87 (#line hidden / #line default ディレクティブ)")]
    [Trait("RoadmapId", "SYN-87")]
    [Trait("Category", "Syntax")]
    public void Test_SYN87_LineDirectives_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-88 (暗黙のスタック配列 stackalloc[])")]
    [Trait("RoadmapId", "SYN-88")]
    [Trait("Category", "Syntax")]
    public void Test_SYN88_ImplicitStackallocArray_Transpilation()
    {
    }
}
