using Kassyi.GSharp.Tests.Generators;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class TestMethodNamingAnalyzerTests : AnalyzerTestBase
{
    // HNT0009: Test methods following <Unit>_<Condition>[_<Expected>] produce no diagnostics.
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Analyze_ValidNamingConventions_NoDiagnostics()
    {
        await VerifyAnalyzerFromFileAsync(
            "TestMethodNaming/ValidTestNames.cs",
            new TestMethodNamingAnalyzer(),
            nameof(Analyze_ValidNamingConventions_NoDiagnostics));
    }

    // HNT0009: Test methods with forbidden Test/Test_ prefix or invalid shapes report HNT0009 diagnostics.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Analyze_ForbiddenPrefixesAndInvalidShapes_ReportsDiagnostics()
    {
        await VerifyAnalyzerFromFileAsync(
            "TestMethodNaming/InvalidTestNames.cs",
            new TestMethodNamingAnalyzer(),
            nameof(Analyze_ForbiddenPrefixesAndInvalidShapes_ReportsDiagnostics));
    }

    // HNT0009: Non-test assemblies are ignored even if methods have invalid names.
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Analyze_NonTestAssembly_NoDiagnostics()
    {
        await VerifyAnalyzerFromFileAsync(
            "TestMethodNaming/InvalidTestNames.cs",
            new TestMethodNamingAnalyzer(),
            nameof(Analyze_NonTestAssembly_NoDiagnostics),
            assemblyName: "ProductAssembly");
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Add_TwoNegativeOperands_ReturnsNegativeSum")]
    [InlineData("Lower_WhileLoop")]
    [InlineData("Kernel_Launch_Grid1D_WithStream")]
    [InlineData("Int128_DivRem256_Quotient384")]
    [InlineData("Testing_ValidCondition_ExpectedBehavior")]
    [InlineData("Tester_BoundaryCondition_Passes")]
    public void IsValidTestMethodName_ValidPatterns_ReturnsTrue(string name)
    {
        Assert.True(TestMethodNamingAnalyzer.IsValidTestMethodName(name));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Test")]
    [InlineData("Test_")]
    [InlineData("Test_Add_TwoNumbers")]
    [InlineData("TestAdd_TwoNumbers")]
    [InlineData("TestMethod")]
    [InlineData("SimpleExecution")]
    [InlineData("_LeadingUnderscore_Invalid")]
    [InlineData("TrailingUnderscore_")]
    [InlineData("Two__Underscores_Invalid")]
    [InlineData("lowercaseFirst_Condition_Expected")]
    [InlineData("Add_lowercaseSecond_Expected")]
    [InlineData("One_Two_Three_Four_Five")]
    public void IsValidTestMethodName_InvalidPatterns_ReturnsFalse(string? name)
    {
        Assert.False(TestMethodNamingAnalyzer.IsValidTestMethodName(name));
    }
}
