using System.Threading.Tasks;
using Xunit;
using Kassyi.GSharp.Tests.Helpers;
using Kassyi.GSharp.Compiler.Diagnostics;

namespace Kassyi.GSharp.Tests.Analyzers;

public class MEM_02_ObjectLifecycleSadPathTests : TranspilerTestBase
{
    private static readonly DisposableOwnershipAnalyzer s_ownershipAnalyzer = new();
    private static readonly AllocationAnalyzer s_allocationAnalyzer = new();
    private static readonly SideEffectAnalyzer s_sideEffectAnalyzer = new();
    private const string TestCasePath = "Analyzers/MEM-02_ObjectLifecycleSadPath.cs";

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_1_MissingIDisposableOnClass_Detected()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_ownershipAnalyzer)
                               .ReportsErrorsAsync("GSH0020");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_1_UndisposedFieldInDispose_Detected()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_ownershipAnalyzer)
                               .ReportsErrorsAsync("GSH0021");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_1_UnsafeFieldOverwrite_Detected()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_allocationAnalyzer)
                               .ReportsErrorsAsync("GSH0022");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_2_PropertyGetterSideEffects_Detected()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_sideEffectAnalyzer)
                               .ReportsErrorsAsync("GSH0023");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_2_ImplicitCastOperators_Detected()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_sideEffectAnalyzer)
                               .ReportsErrorsAsync("GSH0024");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_3_UnguardedMultiAllocation_Detected()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_allocationAnalyzer)
                               .ReportsErrorsAsync("GSH0025");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_3_StaticLifecycle_Detected()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_allocationAnalyzer)
                               .ReportsErrorsAsync("GSH0026");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_4_StandardInjectionBorrowing_HazardDetected()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_ownershipAnalyzer)
                               .ReportsErrorsAsync("GSH0027");
    }

    [Fact]
    [Trait("FeatureId", "MEM-02")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM02_11_3_5_DefaultInterfaceMethods_Detected()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_ownershipAnalyzer)
                               .ReportsErrorsAsync("GSH0028");
    }
}
