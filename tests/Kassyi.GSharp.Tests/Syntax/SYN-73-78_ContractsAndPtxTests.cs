
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_73_78_ContractsAndPtxTests
:  TranspilerTestBase{
    [MemberData(nameof(TranspilerTestBase.Dialects))]
    [Theory]
    [Trait("FeatureId", "SYN-73")]
    [Trait("Category", "Syntax")]
    public void Test_SYN73_DebugAssert_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-74")]
    [Trait("Category", "Syntax")]
    public void Test_SYN74_CallerInfoAttributes_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-75")]
    [Trait("Category", "Syntax")]
    public void Test_SYN75_ConditionalAttribute_Transpilation(ITranspilerDialect dialect)
    {
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-76")]
    [Trait("Category", "Syntax")]
    public void Test_SYN76_ConstexprPropagation_Transpilation(ITranspilerDialect dialect)
    {
    }
    [MemberData(nameof(TranspilerTestBase.Dialects))]
    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-77")]
    public void TestInlinePtxKernel(ITranspilerDialect dialect)
    {
        uint[] data = [0];
        SYN_73_78_ContractsAndPtxKernelsWrapper.Launch_InlinePtxKernel(data);
        Assert.Equal(999u, data[0]);
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("FeatureId", "SYN-78")]
    [Trait("Category", "Syntax")]
    public void Test_SYN78_WmmaTensorCores_Transpilation(ITranspilerDialect dialect)
    {
    }
}
