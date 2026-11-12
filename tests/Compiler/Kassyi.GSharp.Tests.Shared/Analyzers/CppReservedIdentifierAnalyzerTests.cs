namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class CppReservedIdentifierAnalyzerTests : TranspilerTestBase
{
    private const string DoubleUnderscoreId = "GSH0055";
    private const string RetiredCppKeywordId = "GSH0056";
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task DoubleUnderscoreIdentifier_ReservedIdentifierDeclaration_ReportsEachDeclarationOnce()
    {
        var diagnostics = await GeneratorTestRunner
            .TestAnalyzer("Analyzers/DoubleUnderscoreIdentifier_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var reported = diagnostics.Where(d => d.Id == DoubleUnderscoreId).ToList();

        Assert.All(reported, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Equal(
            new[] { "__Helper", "__gsharp_state", "__tmp", "a__b", "items" },
            reported.Select(d => GetReportedIdentifier(d).ValueText).Order(StringComparer.Ordinal).ToArray());
        // The parameter items is reported for the reserved field of its element struct, which the message names
        Assert.Contains(
            "GSH_57_ReservedFieldStruct.__gsharp_value",
            reported.Single(d => GetReportedIdentifier(d).ValueText == "items").GetMessage());
        Assert.DoesNotContain(diagnostics, d => d.Id == RetiredCppKeywordId);
    }

    // Every declaration, method names included, is renamed by the transpiler, so a C++ keyword is an ordinary name.
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public async Task CppKeywordIdentifier_ValidCsharpIdentifiers_AllowsEveryDeclaration()
    {
        var diagnostics = await GeneratorTestRunner
            .TestAnalyzer("Analyzers/CppKeywordIdentifier_HappyPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, d => d.Id == RetiredCppKeywordId);
        Assert.DoesNotContain(diagnostics, d => d.Id == DoubleUnderscoreId);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public async Task CppReservedIdentifier_OrdinaryAndHostOnlyIdentifiers_IgnoresIdentifiers()
    {
        var diagnostics = await GeneratorTestRunner
            .TestAnalyzer("Analyzers/CppReservedIdentifier_HappyPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, d => d.Id is DoubleUnderscoreId or RetiredCppKeywordId);
    }

    // The identifier token of the declaration the diagnostic is reported on; its span must be the diagnostic's span.
    private static SyntaxToken GetReportedIdentifier(Diagnostic diagnostic)
    {
        var tree = diagnostic.Location.SourceTree
            ?? throw new InvalidOperationException($"{diagnostic.Id} has no source location.");
        var token = tree.GetRoot().FindToken(diagnostic.Location.SourceSpan.Start);
        Assert.Equal(SyntaxKind.IdentifierToken, token.Kind());
        Assert.Equal(diagnostic.Location.SourceSpan, token.Span);
        return token;
    }
}
