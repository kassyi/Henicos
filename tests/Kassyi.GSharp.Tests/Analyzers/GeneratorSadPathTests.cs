using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class GeneratorSadPathTests
{
    [Theory]
    [Trait("Category", "SadPath")]
    [InlineData("SYN-13", "Analyzers/SYN-13_DynamicStackAllocSadPath.cs", "GSH0003")]
    [InlineData("SYN-49", "Analyzers/SYN-49_UnsupportedInterlockedOverload.cs", "GSH0003")]
    [InlineData("SYN-68", "Analyzers/SYN-68_UnsupportedMathMethod.cs", "GSH0003")]
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
    public async Task Test_GeneratorSadPath_ReportsMultipleExpectedErrors(string featureId, string testCasePath, string[] expectedErrorIds)
    {
        await RoslynTestUtility.TestGenerator(testCasePath)
                               .ReportsErrorsAsync(expectedErrorIds);
    }

    [Theory]
    [Trait("Category", "SadPath")]
    [InlineData("GEN-29", "Analyzers/GEN-29_DynamicSharedMemorySadPath.cs")]
    public async Task Test_GeneratorSadPath_HasNoErrors(string featureId, string testCasePath)
    {
        await RoslynTestUtility.TestGenerator(testCasePath)
                               .HasNoErrorsAsync();
    }

    [Theory]
    [Trait("Category", "SadPath")]
    [InlineData("GEN-33", "Analyzers/GEN-33_UnsupportedStructFieldsSadPath.cs")]
    public async Task Test_GeneratorSadPath_HasErrors(string featureId, string testCasePath)
    {
        await RoslynTestUtility.TestGenerator(testCasePath)
                               .HasErrorsAsync();
    }
}
