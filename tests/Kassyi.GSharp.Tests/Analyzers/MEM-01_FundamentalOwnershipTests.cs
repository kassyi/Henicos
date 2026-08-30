using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Analyzers;

public class MEM_01_FundamentalOwnershipTests
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void Register()
    {
        var testCasesDir = GpuAnalyzerTestRunner.GetTestCasesDirectory();
        var files = Directory.GetFiles(Path.Combine(testCasesDir, "Analyzers"), "MEM-01_*.cs");
        foreach (var f in files)
        {
            MegaCompilationCache.RegisterSource(File.ReadAllText(f));
        }
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public void Test_MEM01_DiscardedAllocation()
    {
        var (model, methodSymbol) = MegaCompilationCache.GetMethodContext("MEM_01_DiscardedAllocation", "Test");
        var method = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();

        var reportedDiagnostics = new System.Collections.Generic.List<Diagnostic>();
        var context =
            new Kassyi.GSharp.Compiler.Diagnostics.Rules.ValidationContext(model, methodSymbol,
                reportedDiagnostics.Add);
        Kassyi.GSharp.Compiler.Diagnostics.Rules.FundamentalOwnershipRule.Instance.Analyze(method, ref context);

        Assert.Contains(reportedDiagnostics, d => d.Id == "MEM0001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public void Test_MEM01_UndisposedLocal()
    {
        var (model, methodSymbol) = MegaCompilationCache.GetMethodContext("MEM_01_UndisposedLocal", "Test");
        var method = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();

        var reportedDiagnostics = new System.Collections.Generic.List<Diagnostic>();
        var context =
            new Kassyi.GSharp.Compiler.Diagnostics.Rules.ValidationContext(model, methodSymbol,
                reportedDiagnostics.Add);
        Kassyi.GSharp.Compiler.Diagnostics.Rules.FundamentalOwnershipRule.Instance.Analyze(method, ref context);

        Assert.Contains(reportedDiagnostics, d => d.Id == "MEM0001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public void Test_MEM01_ControlFlowBypass()
    {
        var (model, methodSymbol) = MegaCompilationCache.GetMethodContext("MEM_01_ControlFlowBypass", "Test");
        var method = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();

        var reportedDiagnostics = new System.Collections.Generic.List<Diagnostic>();
        var context =
            new Kassyi.GSharp.Compiler.Diagnostics.Rules.ValidationContext(model, methodSymbol,
                reportedDiagnostics.Add);
        Kassyi.GSharp.Compiler.Diagnostics.Rules.FundamentalOwnershipRule.Instance.Analyze(method, ref context);

        Assert.Contains(reportedDiagnostics, d => d.Id == "MEM0001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public void Test_MEM01_AsyncVoid()
    {
        var (model, methodSymbol) = MegaCompilationCache.GetMethodContext("MEM_01_AsyncVoid", "ProcessAndDispose");
        var method = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();

        var reportedDiagnostics = new System.Collections.Generic.List<Diagnostic>();
        var context =
            new Kassyi.GSharp.Compiler.Diagnostics.Rules.ValidationContext(model, methodSymbol,
                reportedDiagnostics.Add);
        Kassyi.GSharp.Compiler.Diagnostics.Rules.FundamentalOwnershipRule.Instance.Analyze(method, ref context);

        Assert.Contains(reportedDiagnostics, d => d.Id == "MEM0001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public void Test_MEM01_UnsafeNullableDisposal()
    {
        var (model, methodSymbol) = MegaCompilationCache.GetMethodContext("MEM_01_UnsafeNullableDisposal", "Test");
        var method = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();

        var reportedDiagnostics = new System.Collections.Generic.List<Diagnostic>();
        var context =
            new Kassyi.GSharp.Compiler.Diagnostics.Rules.ValidationContext(model, methodSymbol,
                reportedDiagnostics.Add);
        Kassyi.GSharp.Compiler.Diagnostics.Rules.FundamentalOwnershipRule.Instance.Analyze(method, ref context);

        Assert.Contains(reportedDiagnostics, d => d.Id == "MEM0001" && d.Severity == DiagnosticSeverity.Error);

        var compileDiagnostics = model.Compilation.GetDiagnostics();
        Assert.Contains(compileDiagnostics, d => d.Id == "CS8602");
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public void Test_MEM01_NullAssignmentWithoutDisposal()
    {
        var (model, methodSymbol) =
            MegaCompilationCache.GetMethodContext("MEM_01_NullAssignmentWithoutDisposal", "Test");
        var method = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();

        var reportedDiagnostics = new System.Collections.Generic.List<Diagnostic>();
        var context =
            new Kassyi.GSharp.Compiler.Diagnostics.Rules.ValidationContext(model, methodSymbol,
                reportedDiagnostics.Add);
        Kassyi.GSharp.Compiler.Diagnostics.Rules.FundamentalOwnershipRule.Instance.Analyze(method, ref context);

        Assert.Contains(reportedDiagnostics, d => d.Id == "MEM0001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "Syntax")]
    public void Test_MEM01_FundamentalOwnershipValid()
    {
        var typeSymbol = MegaCompilationCache.GetNamedTypeSymbol("MEM_01_FundamentalOwnershipValid");
        var model = MegaCompilationCache.BaseCompilation.GetSemanticModel(typeSymbol.DeclaringSyntaxReferences[0]
            .SyntaxTree);
        var root = typeSymbol.DeclaringSyntaxReferences[0].SyntaxTree.GetRoot();

        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        Assert.True(methods.Count >= 6, "All valid paths should be parsed");

        foreach (var method in methods)
        {
            var methodSymbol = model.GetDeclaredSymbol(method);
            if (methodSymbol == null) continue;

            var reportedDiagnostics = new System.Collections.Generic.List<Diagnostic>();
            var context =
                new Kassyi.GSharp.Compiler.Diagnostics.Rules.ValidationContext(model, methodSymbol,
                    reportedDiagnostics.Add);
            Kassyi.GSharp.Compiler.Diagnostics.Rules.FundamentalOwnershipRule.Instance.Analyze(method, ref context);

            Assert.Empty(reportedDiagnostics);
        }
    }
}
