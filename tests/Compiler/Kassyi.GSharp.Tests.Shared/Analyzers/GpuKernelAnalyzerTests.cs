namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class GpuKernelAnalyzerTests
:  TranspilerTestBase{
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("GSH-01", "Analyzers/GpuKernel/Foreach_InGpuKernel.cu.cs", "GSH0007")]
    [InlineData("GSH-02", "Analyzers/GpuKernel/LinqQuery_InGpuKernel.cu.cs", "GSH0008")]
    [InlineData("GSH-03", "Analyzers/GpuKernel/ImplicitVar_WithoutSuffix_InGpuKernel.cu.cs", "GSH0009")]
    [InlineData("GSH-19", "Analyzers/ManualEnumeratorSadPath.cs", "GSH0019")]
    [InlineData("SYN-49", "Analyzers/UnsupportedInterlockedOverload.cs", "GSH0003")]
    [InlineData("SYN-68", "Analyzers/UnsupportedMathMethod.cs", "GSH0003")]
    [InlineData("SYN-155", "Analyzers/UnsupportedBitOperationsMethod.cs", "GSH0003")]
    [InlineData("GSH-36", "Analyzers/KernelClassMustBePartial_SadPath.cs", "GSH0036")]
    public async Task Test_GpuKernelAnalyzer_ReportsExpectedError(string featureId, string testCasePath, string expectedErrorId)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .ReportsErrorsAsync(expectedErrorId);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    // Note: GSH-02b uses .cu.txt instead of .cu.cs to prevent SnapshotTestCaseGenerator from picking it up.
    // If it were .cu.cs, the G# transpiler would attempt to compile the LINQ method syntax (which is invalid in C++),
    // causing a fatal MSVC/nvcc compiler error during the E2E backend tests (Tests.Cpu / Tests.Cuda).
    [InlineData("GSH-02b", "Analyzers/GpuKernel/LinqMethod_InGpuKernel.cu.txt", new[] { "GSH0008", "GSH0008", "GSH0008" })]
    [InlineData("GEN-36", "Analyzers/MacroArgumentSadPath.cs", new[] { "GSH0012", "GSH0006", "GSH0003" })]
    [InlineData("GSH-16", "Analyzers/UnrollHintSadPath.cs", new[] { "GSH0016", "GSH0017" })]
    [InlineData("GSH-32-Sad", "Analyzers/UnrollStaticDeterminism_SadPath.cs", new[] { "GSH0032", "GSH0032", "GSH0032", "GSH0033", "GSH0033" })]
    [InlineData("GSH-30", "Analyzers/SyncGridCooperativeSadPath.cs", new[] { "GSH0030", "GSH0030", "GSH0030", "GSH0030", "GSH0030", "GSH0030", "GSH0030", "GSH0030" })]
    [InlineData("GSH-34-Sad", "Analyzers/ParamsProhibited_SadPath.cs", new[] { "GSH0034", "GSH0034" })]
    public async Task Test_GpuKernelAnalyzer_ReportsMultipleExpectedDiagnostics(string featureId, string testCasePath, string[] expectedDiagnosticIds)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .ReportsDiagnosticsAsync(expectedDiagnosticIds);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("GSH-00", "Analyzers/GpuKernel/ValidSyntax_InGpuKernel.cu.cs")]
    [InlineData("GSH-32-Happy", "Analyzers/UnrollStaticDeterminism_HappyPath.cs")]
    [InlineData("GSH-34-Happy", "Analyzers/ParamsProhibited_HappyPath.cs")]
    public async Task Test_GpuKernelAnalyzer_HasNoErrors(string featureId, string testCasePath)
    {
        await RoslynTestUtility.TestAnalyzer(testCasePath, s_analyzer)
                               .HasNoErrorsAsync();
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UserDefinedGpuMacros_AreNotValidatedAsGpuMacros()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/GpuKernel/LookalikeGpuMacros_InGpuKernel.cu.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, d => d.Id == GSharpDiagnostics.MacroArgumentMustBeConstant.Id);
        Assert.DoesNotContain(diagnostics, d => d.Id == GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral.Id);
        Assert.DoesNotContain(diagnostics, d => d.Id == GSharpDiagnostics.SyncGridRequiresCooperativeKernel.Id);
        Assert.DoesNotContain(diagnostics, d => d.Id == GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant.Id);
        Assert.DoesNotContain(diagnostics, d => d.Id == GSharpDiagnostics.UnrollHintMustPrecedeLoop.Id);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_EnclosingTypesMustBePartial_ReportsGsh0036AtIdentifierSpan()
    {
        const string ExpectedMessageFormat =
            "The type '{0}' that contains or encloses GPU code must be declared partial to support source generation of contexts and launch methods.";

        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/EnclosingTypeMustBePartial_SadPath.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();
        var actual = diagnostics
            .Where(d => d.Id == GSharpDiagnostics.KernelClassMustBePartial.Id)
            .OrderBy(d => d.Location.GetLineSpan().StartLinePosition.Line)
            .Select(d =>
            {
                var span = d.Location.GetLineSpan();
                return (
                    span.StartLinePosition.Line,
                    span.StartLinePosition.Character,
                    span.EndLinePosition.Line,
                    span.EndLinePosition.Character,
                    d.GetMessage());
            })
            .ToArray();

        // DeviceOnlyStruct is not reported, and OuterOfTwoClasses only once
        Assert.Equal(
        [
            (4, 13, 4, 33, string.Format(ExpectedMessageFormat, "OuterNonPartialClass")),
            (6, 18, 6, 39, string.Format(ExpectedMessageFormat, "OuterNonPartialStruct")),
            (18, 14, 18, 35, string.Format(ExpectedMessageFormat, "OuterNonPartialRecord")),
            (35, 13, 35, 30, string.Format(ExpectedMessageFormat, "OuterOfTwoClasses")),
        ], actual);
    }
}
