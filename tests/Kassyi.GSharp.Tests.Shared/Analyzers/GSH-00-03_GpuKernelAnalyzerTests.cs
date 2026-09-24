
namespace Kassyi.GSharp.Tests.Analyzers;

public class GSH_00_03_GpuKernelAnalyzerTests
:  TranspilerTestBase{
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Theory]
    [Trait("Category", "SadPath")]
    [InlineData("GSH-01", "Analyzers/GpuKernel/GSH-01_Foreach_InGpuKernel.cu.cs", "GSH0007")]
    [InlineData("GSH-02", "Analyzers/GpuKernel/GSH-02_LinqQuery_InGpuKernel.cu.cs", "GSH0008")]
    [InlineData("GSH-03", "Analyzers/GpuKernel/GSH-03_ImplicitVar_WithoutSuffix_InGpuKernel.cu.cs", "GSH0009")]
    [InlineData("GSH-19", "Analyzers/GSH-19_ManualEnumeratorSadPath.cs", "GSH0019")]
    public async Task Test_GpuKernelAnalyzer_ReportsExpectedError(string featureId, string testCasePath, string expectedErrorId)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .ReportsErrorsAsync(expectedErrorId);
    }

    [Theory]
    [Trait("Category", "SadPath")]
    // Note: GSH-02b uses .cu.txt instead of .cu.cs to prevent SnapshotTestCaseGenerator from picking it up.
    // If it were .cu.cs, the G# transpiler would attempt to compile the LINQ method syntax (which is invalid in C++),
    // causing a fatal MSVC/nvcc compiler error during the E2E backend tests (Tests.Cpu / Tests.Cuda).
    [InlineData("GSH-02b", "Analyzers/GpuKernel/GSH-02b_LinqMethod_InGpuKernel.cu.txt", new[] { "GSH0008", "GSH0008", "GSH0008" })]
    [InlineData("GEN-36", "Analyzers/GEN-36_MacroArgumentSadPath.cs", new[] { "GSH0012", "GSH0006" })]
    [InlineData("GSH-16", "Analyzers/GSH-16_17_UnrollHintSadPath.cs", new[] { "GSH0016", "GSH0017" })]
    public async Task Test_GpuKernelAnalyzer_ReportsMultipleExpectedErrors(string featureId, string testCasePath, string[] expectedErrorIds)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .ReportsErrorsAsync(expectedErrorIds);
    }

    [Theory]
    [Trait("Category", "SadPath")]
    [InlineData("GSH-00", "Analyzers/GpuKernel/GSH-00_ValidSyntax_InGpuKernel.cu.cs")]
    public async Task Test_GpuKernelAnalyzer_HasNoErrors(string featureId, string testCasePath)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .HasNoErrorsAsync();
    }
}
