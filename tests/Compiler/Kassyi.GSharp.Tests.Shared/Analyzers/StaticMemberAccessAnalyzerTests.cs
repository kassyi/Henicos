namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class StaticMemberAccessAnalyzerTests : TranspilerTestBase
{
    private const string StaticMemberAccessNotSupportedId = "GSH0065";
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task StaticMemberAccess_NonConstStaticFieldsAndProperties_ReportsGSH0065()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/StaticMemberAccess_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var reported = diagnostics.Where(d => d.Id == StaticMemberAccessNotSupportedId).ToList();

        Assert.All(reported, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        // (enclosing method, qualifier as written or null, member): each access is reported on the whole expression
        var expected = new (string, string?, string)[]
        {
            ("KernelWithStaticAccess", "Cfg", "M"),
            ("KernelWithStaticAccess", "Cfg", "Mutable"),
            ("KernelWithStaticAccess", "Cfg", "Prop"),
            ("KernelWithStaticAccess", null, "SameClassStaticField"),
            ("KernelWithStaticAccess", null, "SameClassStaticProp"),
            ("DeviceFuncWithStaticAccess", "Cfg", "M")
        };

        var actual = reported.Select(d =>
        {
            var (qualifier, member) = GetReportedAccess(d);
            return (GetEnclosingMethodName(d), qualifier, member);
        }).ToArray();

        Assert.Equal(expected.Order(), actual.Order());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task StaticMemberAccess_ConstFieldsEnumsAndIntrinsics_ProducesNoDiagnostics()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/StaticMemberAccess_HappyPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, d => d.Id == StaticMemberAccessNotSupportedId);
    }

    private static string GetEnclosingMethodName(Diagnostic diagnostic)
    {
        var node = diagnostic.Location.SourceTree?.GetRoot().FindNode(diagnostic.Location.SourceSpan);
        while (node != null)
        {
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax method)
            {
                return method.Identifier.ValueText;
            }
            node = node.Parent;
        }
        return string.Empty;
    }

    // The access the diagnostic is reported on: Cfg.M as (Cfg, M), a bare name N as (null, N).
    private static (string? Qualifier, string Member) GetReportedAccess(Diagnostic diagnostic)
    {
        var tree = diagnostic.Location.SourceTree
            ?? throw new InvalidOperationException($"{diagnostic.Id} has no source location.");
        var node = tree.GetRoot().FindNode(diagnostic.Location.SourceSpan);
        Assert.Equal(diagnostic.Location.SourceSpan, node.Span);
        return node switch
        {
            MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax qualifier } access =>
                (qualifier.Identifier.ValueText, access.Name.Identifier.ValueText),
            IdentifierNameSyntax name => (null, name.Identifier.ValueText),
            _ => throw new InvalidOperationException($"{diagnostic.Id} is reported on a {node.Kind()} node.")
        };
    }
}
