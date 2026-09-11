
namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_73_78_ContractsAndPtxTests
:  TranspilerTestBase{
    [Fact(Skip="未実装")]
    [Trait("FeatureId", "SYN-73")]
    [Trait("Category", "Syntax")]
    public void Test_SYN73_DebugAssert_Transpilation()
    {
    }

    [Fact(Skip="未実装")]
    [Trait("FeatureId", "SYN-74")]
    [Trait("Category", "Syntax")]
    public void Test_SYN74_CallerInfoAttributes_Transpilation()
    {
    }

    [Fact(Skip="未実装")]
    [Trait("FeatureId", "SYN-75")]
    [Trait("Category", "Syntax")]
    public void Test_SYN75_ConditionalAttribute_Transpilation()
    {
    }

    [Fact(Skip="未実装")]
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
        Kassyi.GSharp.Tests.Helpers.ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        uint[] data = [0];
        SYN_73_78_ContractsAndPtxKernelsWrapper.Launch_InlinePtxKernel(data);
        Assert.Equal(999u, data[0]);
    }

    [SkippableFact(Skip="未実装")]
    [Trait("FeatureId", "SYN-78")]
    [Trait("Category", "Syntax")]
    public void Test_SYN78_WmmaTensorCores_Transpilation()
    {
    }
}
