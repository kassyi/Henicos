
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_73_78_ContractsAndPtxTests
{
    [Fact(Skip = "未実装: SYN-73 (Debug.Assert / Assert によるデバイス内アサーション)")]
    [Trait("FeatureId", "SYN-73")]
    [Trait("Category", "Syntax")]
    public void Test_SYN73_DebugAssert_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-74 ([CallerMemberName] 等の CallerInfo 属性)")]
    [Trait("FeatureId", "SYN-74")]
    [Trait("Category", "Syntax")]
    public void Test_SYN74_CallerInfoAttributes_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-75 ([Conditional(\"DEBUG\")] 属性メソッド除外)")]
    [Trait("FeatureId", "SYN-75")]
    [Trait("Category", "Syntax")]
    public void Test_SYN75_ConditionalAttribute_Transpilation()
    {
    }

    [Fact(Skip = "未実装: SYN-76 (const フィールド / const int 定数伝播 constexpr)")]
    [Trait("FeatureId", "SYN-76")]
    [Trait("Category", "Syntax")]
    public void Test_SYN76_ConstexprPropagation_Transpilation()
    {
    }
    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-77")]
    public void TestInlinePtxKernel()
    {
        uint[] data = [0];
        SYN_73_78_ContractsAndPtxKernelsWrapper.Launch_InlinePtxKernel(data);
        Assert.Equal(999u, data[0]);
    }

    [Fact(Skip = "未実装: SYN-78 (Wmma / Tensor Core 演算型マッピング)")]
    [Trait("FeatureId", "SYN-78")]
    [Trait("Category", "Syntax")]
    public void Test_SYN78_WmmaTensorCores_Transpilation()
    {
    }
}
