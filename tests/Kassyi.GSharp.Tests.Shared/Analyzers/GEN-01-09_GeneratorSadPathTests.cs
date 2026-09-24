
namespace Kassyi.GSharp.Tests.Analyzers;

public class GEN_01_09_GeneratorSadPathTests
:  TranspilerTestBase{
    [Theory]
    [Trait("Category", "SadPath")]
    [InlineData("SYN-13", "Analyzers/SYN-13_DynamicStackAllocSadPath.cs", "GSH0003")]
    [InlineData("SYN-17", "Analyzers/SYN-17_UnresolvedTargetTypedNew.cs", "CS8754")]
    [InlineData("SYN-03", "Analyzers/SYN-03_DuplicateOutVar.cs", "CS0128")]
    [InlineData("SYN-04", "Analyzers/GpuKernel/SYN-04_ReferenceNullCoalescing_InGpuKernel.cu.cs", "GSH0002")]
    [InlineData("GSH-01", "Analyzers/GpuKernel/GSH-01_StringTernary_InGpuKernel.cu.cs", "GSH0002")]
    [InlineData("GEN-52", "Analyzers/GEN-39_52_InvalidMappingSadPath.cs", "GSH0001")]
    public async Task Test_GeneratorSadPath_ReportsExpectedError(string featureId, string testCasePath, string expectedErrorId)
    {
        await RoslynTestUtility.TestGenerator(testCasePath)
                               .ReportsErrorsAsync(expectedErrorId);
    }

    [Theory]
    [Trait("Category", "SadPath")]
    [InlineData("SYN-01", "Analyzers/GpuKernel/SYN-01_NonExhaustiveSwitch_InGpuKernel.cu.cs", "CS8509")]
    public async Task Test_GeneratorSadPath_ReportsExpectedDiagnostic(string featureId, string testCasePath, string expectedDiagnosticId)
    {
        await RoslynTestUtility.TestGenerator(testCasePath)
                               .ReportsDiagnosticsAsync(expectedDiagnosticId);
    }

    [Theory]
    [Trait("Category", "SadPath")]
    [InlineData("SYN-08", "Analyzers/SYN-08_DelegateEscapeSadPath.cs", new[] { "GSH0001", "GSH0003" })]
    [InlineData("SYN-01", "Analyzers/GpuKernel/SYN-01_UnsupportedSwitch_InGpuKernel.cu.cs", new[] { "GSH0003", "GSH0003" })]
    public async Task Test_GeneratorSadPath_ReportsMultipleExpectedErrors(string featureId, string testCasePath, string[] expectedErrorIds)
    {
        await RoslynTestUtility.TestGenerator(testCasePath)
                               .ReportsErrorsAsync(expectedErrorIds);
    }

    [Theory]
    [Trait("Category", "SadPath")]
    [InlineData("GEN-29", "Analyzers/GEN-29_DynamicSharedMemorySadPath.cs")]
    [InlineData("SYN-90", "Analyzers/SYN-90_UnsupportedMethodInStructInit.cs")]
    [InlineData("GEN-17", "Analyzers/GEN-17_RefOutReassignmentSadPath.cs")]
    public async Task Test_GeneratorSadPath_HasNoErrors(string featureId, string testCasePath)
    {
        await RoslynTestUtility.TestGenerator(testCasePath)
                               .HasNoErrorsAsync();
    }

    [Theory]
    [Trait("Category", "SadPath")]
    [InlineData("GEN-33", "Analyzers/GEN-33_UnsupportedStructFieldsSadPath.cs")]
    [InlineData("SYN-91_92", "Analyzers/SYN-91_92_InvalidParenthesesSadPath.cs")]
    public async Task Test_GeneratorSadPath_HasErrors(string featureId, string testCasePath)
    {
        await RoslynTestUtility.TestGenerator(testCasePath)
                               .HasErrorsAsync();
    }

    [Fact]
    [Trait("Category", "SadPath")]
    [Trait("FeatureId", "GSH-03")]
    public async Task Test_GSH03_LocalFunctionCapture_ReportsExpectedDiagnostic()
    {
        await RoslynTestUtility.TestGenerator("Analyzers/GSH-03_LocalFunctionCaptureSadPath.cs")
                               .ReportsErrorsAsync("CS8421");
    }
}
