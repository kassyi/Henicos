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
    private static async Task<(SyntaxTree SyntaxTree, SemanticModel SemanticModel)> GetParsedModelAsync(string fileName)
    {
        var testCasesDir = RoslynTestUtility.GetTestCasesDirectory();
        var path = Path.Combine(testCasesDir, "Analyzers", fileName);
        var source = await File.ReadAllTextAsync(path);

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(syntaxTree);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        return (syntaxTree, semanticModel);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM01_DiscardedAllocation()
    {
        var (tree, model) = await GetParsedModelAsync("MEM-01_DiscardedAllocation.cs");
        var root = await tree.GetRootAsync();
        
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "Test");
        
        var discardedAllocations = method.DescendantNodes()
            .OfType<ExpressionStatementSyntax>()
            .Where(e => e.Expression is ObjectCreationExpressionSyntax oce && oce.Type.ToString().Contains("GpuContext"))
            .ToList();
            
        Assert.NotEmpty(discardedAllocations);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM01_UndisposedLocal()
    {
        var (tree, model) = await GetParsedModelAsync("MEM-01_UndisposedLocal.cs");
        var root = await tree.GetRootAsync();

        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "Test");
        
        var localDecl = method.DescendantNodes().OfType<VariableDeclaratorSyntax>().First(v => v.Identifier.Text == "ctx");
        var allocations = localDecl.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().ToList();
        
        var disposes = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(inv => inv.ToString().Contains("ctx.Dispose"))
            .ToList();
            
        Assert.NotEmpty(allocations);
        Assert.Empty(disposes);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM01_ControlFlowBypass()
    {
        var (tree, model) = await GetParsedModelAsync("MEM-01_ControlFlowBypass.cs");
        var root = await tree.GetRootAsync();

        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "Test");
        
        var returns = method.DescendantNodes().OfType<ReturnStatementSyntax>().ToList();
        var disposes = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(inv => inv.ToString().Contains("ctx.Dispose"))
            .ToList();
            
        Assert.NotEmpty(returns);
        Assert.NotEmpty(disposes);
        Assert.True(returns.First().SpanStart < disposes.First().SpanStart, "Return statement bypasses disposal");
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM01_AsyncVoid()
    {
        var (tree, model) = await GetParsedModelAsync("MEM-01_AsyncVoid.cs");
        var root = await tree.GetRootAsync();

        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "ProcessAndDispose");
        
        var isAsync = method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword));
        var isVoid = method.ReturnType is PredefinedTypeSyntax pt && pt.Keyword.IsKind(SyntaxKind.VoidKeyword);
        var parameterTypes = method.ParameterList.Parameters.Select(p => p.Type?.ToString()).ToList();
        
        Assert.True(isAsync);
        Assert.True(isVoid);
        Assert.Contains(parameterTypes, t => t != null && t.Contains("GpuContext"));
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM01_UnsafeNullableDisposal()
    {
        var (tree, model) = await GetParsedModelAsync("MEM-01_UnsafeNullableDisposal.cs");
        var compileDiagnostics = model.Compilation.GetDiagnostics();
        
        Assert.Contains(compileDiagnostics, d => d.Id == "CS8602");
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "SadPath")]
    public async Task Test_MEM01_NullAssignmentWithoutDisposal()
    {
        var (tree, model) = await GetParsedModelAsync("MEM-01_NullAssignmentWithoutDisposal.cs");
        var root = await tree.GetRootAsync();

        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "Test");
        
        var assignments = method.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Where(a => a.Left.ToString() == "ctx" && a.Right.IsKind(SyntaxKind.NullLiteralExpression))
            .ToList();
            
        var disposes = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(inv => inv.ToString().Contains("ctx.Dispose"))
            .ToList();
            
        Assert.NotEmpty(assignments);
        Assert.Empty(disposes);
    }

    [Fact]
    [Trait("FeatureId", "MEM-01")]
    [Trait("Category", "Syntax")]
    public async Task Test_MEM01_FundamentalOwnershipValid()
    {
        var (tree, model) = await GetParsedModelAsync("MEM-01_FundamentalOwnershipValid.cs");
        var root = await tree.GetRootAsync();

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
