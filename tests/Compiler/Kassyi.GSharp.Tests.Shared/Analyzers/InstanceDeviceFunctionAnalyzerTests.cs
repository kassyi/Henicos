using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Transpiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class InstanceDeviceFunctionAnalyzerTests : TranspilerTestBase
{
    private const string GSH0066 = "GSH0066";
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_InstanceDeviceFunction_ReportsGSH0066()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/InstanceDeviceFunction_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var reported = diagnostics.Where(d => d.Id == GSH0066).ToList();

        Assert.Equal(2, reported.Count);
        Assert.All(reported, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Contains(reported, d => d.GetMessage().Contains("'S.Twice'", StringComparison.Ordinal));
        Assert.Contains(reported, d => d.GetMessage().Contains("'C.Add'", StringComparison.Ordinal));

        var (compilation, tree, model) = RoslynTestUtility.GetTestCaseContext("Analyzers/InstanceDeviceFunction_SadPath.cs");
        Assert.DoesNotContain(compilation.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);

        var methodDecl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "Twice");
        var transpileDiagnostics = new List<Diagnostic>();
        MethodTranspiler.GenerateCudaSource(methodDecl, model, transpileDiagnostics.Add, isDevice: true, new GSharpCudaDialect(), out _);

        Assert.Contains(transpileDiagnostics, d => d.Id == GSH0066 && d.GetMessage().Contains("'S.Twice'", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_InstanceDeviceFunction_AllowsStaticDeviceFunctionAndHostInstanceMethod()
    {
        var (compilation, _, _) = RoslynTestUtility.GetTestCaseContext("Analyzers/InstanceDeviceFunction_HappyPath.cs");
        Assert.DoesNotContain(compilation.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);

        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/InstanceDeviceFunction_HappyPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, d => d.Id == GSH0066);
    }
}
