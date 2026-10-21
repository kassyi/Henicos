
namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class GeneratorSadPathTests
:  TranspilerTestBase{
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("SYN-13", "Analyzers/DynamicStackAllocSadPath.cs", "GSH0003")]
    [InlineData("SYN-17", "Analyzers/UnresolvedTargetTypedNew.cs", "CS8754")]
    [InlineData("SYN-03", "Analyzers/DuplicateOutVar.cs", "CS0128")]
    [InlineData("SYN-04", "Analyzers/GpuKernel/ReferenceNullCoalescing_InGpuKernel.cu.cs", "GSH0002")]
    [InlineData("GSH-01", "Analyzers/GpuKernel/StringTernary_InGpuKernel.cu.cs", "GSH0002")]
    [InlineData("GEN-52", "Analyzers/InvalidMappingSadPath.cs", "GSH0001")]
    public async Task Test_GeneratorSadPath_ReportsExpectedError(string featureId, string testCasePath, string expectedErrorId)
    {
        await RoslynTestUtility.TestGenerator(testCasePath)
                               .ReportsErrorsAsync(expectedErrorId);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("SYN-01", "Analyzers/GpuKernel/NonExhaustiveSwitch_InGpuKernel.cu.cs", "CS8509")]
    public async Task Test_GeneratorSadPath_ReportsExpectedDiagnostic(string featureId, string testCasePath, string expectedDiagnosticId)
    {
        await RoslynTestUtility.TestGenerator(testCasePath)
                               .ReportsDiagnosticsAsync(expectedDiagnosticId);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("SYN-08", "Analyzers/DelegateEscapeSadPath.cs", new[] { "GSH0001", "GSH0003" })]
    [InlineData("SYN-01", "Analyzers/GpuKernel/UnsupportedSwitch_InGpuKernel.cu.cs", new[] { "GSH0003", "GSH0003" })]
    public async Task Test_GeneratorSadPath_ReportsMultipleExpectedErrors(string featureId, string testCasePath, string[] expectedErrorIds)
    {
        await RoslynTestUtility.TestGenerator(testCasePath)
                               .ReportsErrorsAsync(expectedErrorIds);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("GEN-29", "Analyzers/DynamicSharedMemorySadPath.cs")]
    [InlineData("SYN-90", "Analyzers/UnsupportedMethodInStructInit.cs")]
    [InlineData("GEN-17", "Analyzers/RefOutReassignmentSadPath.cs")]
    public async Task Test_GeneratorSadPath_HasNoErrors(string featureId, string testCasePath)
    {
        await RoslynTestUtility.TestGenerator(testCasePath)
                               .HasNoErrorsAsync();
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("GEN-33", "Analyzers/UnsupportedStructFieldsSadPath.cs")]
    [InlineData("SYN-91_92", "Analyzers/InvalidParenthesesSadPath.cs")]
    public async Task Test_GeneratorSadPath_HasErrors(string featureId, string testCasePath)
    {
        await RoslynTestUtility.TestGenerator(testCasePath)
                               .HasErrorsAsync();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_LocalFunctionCapture_ReportsExpectedDiagnostic()
    {
        await RoslynTestUtility.TestGenerator("Analyzers/LocalFunctionCaptureSadPath.cs")
                               .ReportsErrorsAsync("CS8421");
    }
}
