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
        foreach(var f in files)
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
        
        var discardedAllocations = method.DescendantNodes()
            .OfType<ExpressionStatementSyntax>()
            .Where(e => e.Expression is ObjectCreationExpressionSyntax oce && 
                        oce.Type is IdentifierNameSyntax id && id.Identifier.Text == "GpuContext")
            .ToList();
            
        Assert.NotEmpty(discardedAllocations);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public void Test_MEM01_UndisposedLocal()
    {
        var (model, methodSymbol) = MegaCompilationCache.GetMethodContext("MEM_01_UndisposedLocal", "Test");
        var method = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();
        
        var localDecl = method.DescendantNodes().OfType<VariableDeclaratorSyntax>().First(v => v.Identifier.Text == "ctx");
        var allocations = localDecl.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().ToList();
        
        var disposes = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(inv => inv.Expression is MemberAccessExpressionSyntax mae && 
                          mae.Expression is IdentifierNameSyntax id && id.Identifier.Text == "ctx" && 
                          mae.Name.Identifier.Text == "Dispose")
            .ToList();
            
        Assert.NotEmpty(allocations);
        Assert.Empty(disposes);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public void Test_MEM01_ControlFlowBypass()
    {
        var (model, methodSymbol) = MegaCompilationCache.GetMethodContext("MEM_01_ControlFlowBypass", "Test");
        var method = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();
        
        var returns = method.DescendantNodes().OfType<ReturnStatementSyntax>().ToList();
        var disposes = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(inv => inv.Expression is MemberAccessExpressionSyntax mae && 
                          mae.Expression is IdentifierNameSyntax id && id.Identifier.Text == "ctx" && 
                          mae.Name.Identifier.Text == "Dispose")
            .ToList();
            
        Assert.NotEmpty(returns);
        Assert.NotEmpty(disposes);
        Assert.True(returns.First().SpanStart < disposes.First().SpanStart, "Return statement bypasses disposal");
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public void Test_MEM01_AsyncVoid()
    {
        var (model, methodSymbol) = MegaCompilationCache.GetMethodContext("MEM_01_AsyncVoid", "ProcessAndDispose");
        var method = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();
        
        var isAsync = method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword));
        var isVoid = method.ReturnType is PredefinedTypeSyntax pt && pt.Keyword.IsKind(SyntaxKind.VoidKeyword);
        var parameterTypes = method.ParameterList.Parameters
            .Select(p => p.Type)
            .OfType<IdentifierNameSyntax>()
            .Select(id => id.Identifier.Text)
            .ToList();
        
        Assert.True(isAsync);
        Assert.True(isVoid);
        Assert.Contains(parameterTypes, t => t == "GpuContext");
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public void Test_MEM01_UnsafeNullableDisposal()
    {
        var (model, methodSymbol) = MegaCompilationCache.GetMethodContext("MEM_01_UnsafeNullableDisposal", "Test");
        
        var compileDiagnostics = model.Compilation.GetDiagnostics();
        
        Assert.Contains(compileDiagnostics, d => d.Id == "CS8602");
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public void Test_MEM01_NullAssignmentWithoutDisposal()
    {
        var (model, methodSymbol) = MegaCompilationCache.GetMethodContext("MEM_01_NullAssignmentWithoutDisposal", "Test");
        var method = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();
        
        var assignments = method.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Where(a => a.Left is IdentifierNameSyntax id && id.Identifier.Text == "ctx" && 
                        a.Right.IsKind(SyntaxKind.NullLiteralExpression))
            .ToList();
            
        var disposes = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(inv => inv.Expression is MemberAccessExpressionSyntax mae && 
                          mae.Expression is IdentifierNameSyntax id && id.Identifier.Text == "ctx" && 
                          mae.Name.Identifier.Text == "Dispose")
            .ToList();
            
        Assert.NotEmpty(assignments);
        Assert.Empty(disposes);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "Syntax")]
    public void Test_MEM01_FundamentalOwnershipValid()
    {
        var typeSymbol = MegaCompilationCache.GetNamedTypeSymbol("MEM_01_FundamentalOwnershipValid");
        var model = MegaCompilationCache.BaseCompilation.GetSemanticModel(typeSymbol.DeclaringSyntaxReferences[0].SyntaxTree);
        var root = typeSymbol.DeclaringSyntaxReferences[0].SyntaxTree.GetRoot();

        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        Assert.True(methods.Count >= 6, "All valid paths should be parsed");
        
        var usingVar = methods.SelectMany(m => m.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
            .Any(ld => ld.UsingKeyword.IsKind(SyntaxKind.UsingKeyword));
            
        Assert.True(usingVar, "using var path should be present");
        
        var diagnostics = model.Compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && !d.Id.StartsWith("CS0246"))
            .ToList();
            
        Assert.Empty(diagnostics);
    }
}
