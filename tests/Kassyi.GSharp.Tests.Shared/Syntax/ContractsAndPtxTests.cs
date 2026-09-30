
namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class ContractsAndPtxTests
:  TranspilerTestBase{
    [Fact()]
    [Trait("Category", "Syntax")]
    public void Test_DebugAssert_Transpilation()
    {
    }

    [Fact()]
    [Trait("Category", "Syntax")]
    public void Test_CallerInfoAttributes_Transpilation()
    {
    }

    [Fact()]
    [Trait("Category", "Syntax")]
    public void Test_ConditionalAttribute_Transpilation()
    {
    }

    [Fact()]
    [Trait("Category", "Syntax")]
    public void Test_ConstexprPropagation_Transpilation()
    {
    }
    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    public void TestInlinePtxKernel()
    {
        uint[] data = [0];
        ContractsAndPtxKernels.LaunchInlinePtxKernel(data);
        Assert.Equal(999u, data[0]);
    }

    [SkippableFact()]
    [Trait("Category", "Syntax")]
    public void Test_WmmaTensorCores_Transpilation()
    {
    }
}
