using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class ContractsAndPtxSyntaxTests
{
    [Fact(Skip = "未実装: SYN-73 (Debug.Assert / Trace.Assert)")]
    [Trait("RoadmapId", "SYN-73")]
    [Trait("Category", "Syntax")]
    public void Test_SYN73_DebugAssert_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-74 (CallerMemberName / CallerLineNumber)")]
    [Trait("RoadmapId", "SYN-74")]
    [Trait("Category", "Syntax")]
    public void Test_SYN74_CallerInfoAttributes_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-75 ([Conditional(\"DEBUG\")] 属性)")]
    [Trait("RoadmapId", "SYN-75")]
    [Trait("Category", "Syntax")]
    public void Test_SYN75_ConditionalAttribute_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-76 (コンパイル時定数評価 constexpr 伝播)")]
    [Trait("RoadmapId", "SYN-76")]
    [Trait("Category", "Syntax")]
    public void Test_SYN76_ConstexprPropagation_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-78 (テンソルコア・WMMA Warp Matrix Multiply)")]
    [Trait("RoadmapId", "SYN-78")]
    [Trait("Category", "Syntax")]
    public void Test_SYN78_WmmaTensorCores_Transpilation()
    {
    }
}
