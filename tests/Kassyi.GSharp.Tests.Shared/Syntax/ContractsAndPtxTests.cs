
namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class ContractsAndPtxTests
:  TranspilerTestBase{
    [Fact()]
    [Category(TestCategory.Syntax)]
    public void Test_DebugAssert_Transpilation()
    {
    }

    [Fact()]
    [Category(TestCategory.Syntax)]
    public void Test_CallerInfoAttributes_Transpilation()
    {
    }

    [Fact()]
    [Category(TestCategory.Syntax)]
    public void Test_ConditionalAttribute_Transpilation()
    {
    }

    [Fact()]
    [Category(TestCategory.Syntax)]
    public void Test_ConstexprPropagation_Transpilation()
    {
    }
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void TestInlinePtxKernel()
    {
        SkipIfCpuDialect("Inline PTX is only executable on CUDA devices");

        uint[] data = [0];
        ContractsAndPtxKernels.LaunchInlinePtxKernel(data);
        Assert.Equal(999u, data[0]);
    }

    [SkippableFact()]
    [Category(TestCategory.Syntax)]
    public void Test_WmmaTensorCores_Transpilation()
    {
    }
}
