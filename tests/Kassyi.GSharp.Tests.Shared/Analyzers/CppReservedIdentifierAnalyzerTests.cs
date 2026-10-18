namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class CppReservedIdentifierAnalyzerTests : TranspilerTestBase
{
    private const string DoubleUnderscoreId = "GSH0057";
    private const string CppKeywordId = "GSH0058";
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DoubleUnderscoreIdentifier_ReportsEachDeclarationOnce()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/DoubleUnderscoreIdentifier_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var reported = diagnostics.Where(d => d.Id == DoubleUnderscoreId).ToList();

        Assert.All(reported, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Equal(
            new[] { "GSH_57_ReservedFieldStruct.__gsharp_value", "__Helper", "__gsharp_state", "__tmp", "a__b" },
            reported.Select(GetIdentifier).Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(diagnostics, d => d.Id == CppKeywordId);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CppKeywordIdentifier_ReportsEachDeclarationOnce()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/CppKeywordIdentifier_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var reported = diagnostics.Where(d => d.Id == CppKeywordId).ToList();

        Assert.All(reported, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Equal(
            new[]
            {
                "GSH_58_OuterStruct.Inner.register", "GSH_58_ReservedFieldStruct.register", "and", "class", "friend", "mutable", "not", "register",
                "template", "typename", "union"
            },
            reported.Select(GetIdentifier).Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(diagnostics, d => d.Id == DoubleUnderscoreId);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_CppKeywordIdentifier_ReportsNestedStructMemberAtDeclaration()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/CppKeywordIdentifier_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var nested = Assert.Single(diagnostics, d =>
            d.Id == CppKeywordId && GetIdentifier(d) == "GSH_58_OuterStruct.Inner.register");
        Assert.Contains("outer", nested.Location.SourceTree?.GetText().ToString(nested.Location.SourceSpan));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_CppReservedIdentifier_IgnoresOrdinaryAndHostOnlyIdentifiers()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/CppReservedIdentifier_HappyPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, d => d.Id is DoubleUnderscoreId or CppKeywordId);
    }

    private static string GetIdentifier(Diagnostic diagnostic)
    {
        var message = diagnostic.GetMessage();
        var start = message.IndexOf('\'') + 1;
        return message.Substring(start, message.IndexOf('\'', start) - start);
    }
}
