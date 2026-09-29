
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_73_78_ContractsAndPtxTests
:  TranspilerTestBase{
    [Fact()]
    [Trait("FeatureId", "SYN-73")]
    [Trait("Category", "Syntax")]
    public void Test_SYN73_DebugAssert_Transpilation()
    {
    }

    [Fact()]
    [Trait("FeatureId", "SYN-74")]
    [Trait("Category", "Syntax")]
    public void Test_SYN74_CallerInfoAttributes_Transpilation()
    {
    }

    [Fact()]
    [Trait("FeatureId", "SYN-75")]
    [Trait("Category", "Syntax")]
    public void Test_SYN75_ConditionalAttribute_Transpilation()
    {
    }

    [Fact()]
    [Trait("FeatureId", "SYN-76")]
    [Trait("Category", "Syntax")]
    public void Test_SYN76_ConstexprPropagation_Transpilation()
    {
    }
    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-77")]
    public void TestInlinePtxKernel()
    {
        uint[] data = [0];
        SYN_73_78_ContractsAndPtxKernels.LaunchInlinePtxKernel(data);
        Assert.Equal(999u, data[0]);
    }

    [SkippableFact()]
    [Trait("FeatureId", "SYN-78")]
    [Trait("Category", "Syntax")]
    public void Test_SYN78_WmmaTensorCores_Transpilation()
    {
    }
}
