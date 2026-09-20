
namespace Kassyi.GSharp.Tests.Analyzers;

public class GSH_00_03_GpuKernelAnalyzerTests
:  TranspilerTestBase{
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [SkippableTheory]
    [Trait("Category", "SadPath")]
    [InlineData("GSH-01", "Analyzers/GpuKernel/GSH-01_Foreach_InGpuKernel.cu.cs", "GSH0007")]
    [InlineData("GSH-02", "Analyzers/GpuKernel/GSH-02_LinqQuery_InGpuKernel.cu.cs", "GSH0008")]
    [InlineData("GSH-03", "Analyzers/GpuKernel/GSH-03_ImplicitVar_WithoutSuffix_InGpuKernel.cu.cs", "GSH0009")]
    public async Task Test_GpuKernelAnalyzer_ReportsExpectedError(string featureId, string testCasePath, string expectedErrorId)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .ReportsErrorsAsync(expectedErrorId);
    }

    [SkippableTheory]
    [Trait("Category", "SadPath")]
    [InlineData("GEN-36", "Analyzers/GEN-36_MacroArgumentSadPath.cs", new[] { "GSH0012", "GSH0006" })]
    public async Task Test_GpuKernelAnalyzer_ReportsMultipleExpectedErrors(string featureId, string testCasePath, string[] expectedErrorIds)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .ReportsErrorsAsync(expectedErrorIds);
    }

    [SkippableTheory]
    [Trait("Category", "SadPath")]
    [InlineData("GSH-00", "Analyzers/GpuKernel/GSH-00_ValidSyntax_InGpuKernel.cu.cs")]
    public async Task Test_GpuKernelAnalyzer_HasNoErrors(string featureId, string testCasePath)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .HasNoErrorsAsync();
    }
}
