using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Analyzers.Rules;

internal static class TestHelpers
{
    public static (SemanticModel Model, IMethodSymbol MethodSymbol) CreateDummyContext()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() {} }");
        var compilation = CSharpCompilation.Create("TestComp")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(tree);

        var model = compilation.GetSemanticModel(tree);
        var methodSyntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var methodSymbol = model.GetDeclaredSymbol(methodSyntax)!;

        return (model, methodSymbol);
    }
}
