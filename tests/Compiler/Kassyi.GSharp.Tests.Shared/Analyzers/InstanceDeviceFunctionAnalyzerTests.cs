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
    private const string GSH0068 = "GSH0068";
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_InstanceDeviceFunction_ReportsGSH0068()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/InstanceDeviceFunction_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var reported = diagnostics.Where(d => d.Id == GSH0068).ToList();

        Assert.Equal(2, reported.Count);
        Assert.All(reported, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        var (compilation, tree, model) = RoslynTestUtility.GetTestCaseContext("Analyzers/InstanceDeviceFunction_SadPath.cs");
        var methodDecl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "Twice");
        var transpileDiagnostics = new List<Diagnostic>();
        MethodTranspiler.GenerateCudaSource(methodDecl, model, transpileDiagnostics.Add, isDevice: true, new GSharpCudaDialect(), out _);

        Assert.Contains(transpileDiagnostics, d => d.Id == GSH0068);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_InstanceDeviceFunction_AllowsStaticDeviceFunctionAndHostInstanceMethod()
    {
        var diagnostics = await RoslynTestUtility
            .TestAnalyzer("Analyzers/InstanceDeviceFunction_HappyPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, d => d.Id == GSH0068);
    }
}
