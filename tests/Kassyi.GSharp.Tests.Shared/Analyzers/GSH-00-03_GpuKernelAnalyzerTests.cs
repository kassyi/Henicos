
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
    [InlineData("SYN-49", "Analyzers/SYN-49_UnsupportedInterlockedOverload.cs", "GSH0003")]
    [InlineData("SYN-68", "Analyzers/SYN-68_UnsupportedMathMethod.cs", "GSH0003")]
    [InlineData("SYN-155", "Analyzers/SYN-155_UnsupportedBitOperationsMethod.cs", "GSH0003")]
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
    [InlineData("GSH-32-Sad", "Analyzers/GSH-32_33_UnrollStaticDeterminism_SadPath.cs", new[] { "GSH0032", "GSH0032", "GSH0032", "GSH0033", "GSH0033" })]
    public async Task Test_GpuKernelAnalyzer_ReportsMultipleExpectedDiagnostics(string featureId, string testCasePath, string[] expectedDiagnosticIds)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .ReportsDiagnosticsAsync(expectedDiagnosticIds);
    }

    [Theory]
    [Trait("Category", "SadPath")]
    [InlineData("GSH-00", "Analyzers/GpuKernel/GSH-00_ValidSyntax_InGpuKernel.cu.cs")]
    [InlineData("GSH-32-Happy", "Analyzers/GSH-32_UnrollStaticDeterminism_HappyPath.cs")]
    public async Task Test_GpuKernelAnalyzer_HasNoErrors(string featureId, string testCasePath)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .HasNoErrorsAsync();
    }
}
