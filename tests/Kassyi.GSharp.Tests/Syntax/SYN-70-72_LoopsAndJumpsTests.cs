
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_70_72_LoopsAndJumpsTests
:  TranspilerTestBase{
    [MemberData(nameof(TranspilerTestBase.Dialects))]
    [Theory]
    [Trait("FeatureId", "SYN-70")]
    [Trait("Category", "Syntax")]
    public void Test_SYN70_ForeachSpan_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-71")]
    [Trait("Category", "Syntax")]
    public void Test_SYN71_SwitchGotoCase_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-72")]
    [Trait("Category", "Syntax")]
    public void Test_SYN72_NamedBreakGoto_Transpilation(ITranspilerDialect dialect)
    {
    }
}
