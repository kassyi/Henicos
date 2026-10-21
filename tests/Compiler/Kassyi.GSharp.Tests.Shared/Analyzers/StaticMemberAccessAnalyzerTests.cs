namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class StaticMemberAccessAnalyzerTests : TranspilerTestBase
{
    private const string StaticMemberAccessNotSupportedId = "GSH0067";
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_StaticMemberAccess_ReportsGSH0067ForNonConstStaticFieldsAndProperties()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/StaticMemberAccess_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var reported = diagnostics.Where(d => d.Id == StaticMemberAccessNotSupportedId).ToList();

        Assert.All(reported, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        var expectedPairs = new[]
        {
            ("KernelWithStaticAccess", "Cfg.M"),
            ("KernelWithStaticAccess", "Cfg.Mutable"),
            ("KernelWithStaticAccess", "Cfg.Prop"),
            ("KernelWithStaticAccess", "K.SameClassStaticField"),
            ("KernelWithStaticAccess", "K.SameClassStaticProp"),
            ("DeviceFuncWithStaticAccess", "Cfg.M")
        };

        var actualPairs = reported.Select(d =>
        {
            var enclosingMethod = GetEnclosingMethodName(d);
            var memberDisplay = GetMemberDisplay(d);
            return (enclosingMethod, memberDisplay);
        }).ToArray();

        Assert.Equal(expectedPairs.Order(), actualPairs.Order());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_StaticMemberAccess_AllowsConstFieldsEnumsAndIntrinsics()
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

    private static string GetMemberDisplay(Diagnostic diagnostic)
    {
        var message = diagnostic.GetMessage();
        var start = message.IndexOf('\'') + 1;
        var end = message.IndexOf('\'', start);
        return message.Substring(start, end - start);
    }
}
