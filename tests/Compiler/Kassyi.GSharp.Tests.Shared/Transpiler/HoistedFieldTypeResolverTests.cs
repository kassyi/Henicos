using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

[Category(TestCategory.Compiler)]
public class HoistedFieldTypeResolverTests
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Resolve_UnmappedDeclaratorWithoutInitializer_ThrowsInvalidOperationException()
    {
        var declarator = SyntaxFactory.VariableDeclarator("x");
        var (model, _) = RoslynTestUtility.CreateDummyContext();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            HoistedFieldTypeResolver.Resolve(declarator, model, "TestKernel"));

        Assert.Contains("x", ex.Message);
        Assert.Contains("TestKernel", ex.Message);
        Assert.Contains("not mapped", ex.Message);
        Assert.Contains("Lookup 1, declared local symbol: not found", ex.Message);
        Assert.Contains("Initializer: <none>", ex.Message);
        Assert.Contains("https://github.com/kassyi/Henicos/issues", ex.Message);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Resolve_TypeWithGenericTypeParameter_ThrowsInvalidOperationException()
    {
        const string code = """
            public class TestClass
            {
                public static void K<T>()
                {
                    T x = default;
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(code);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);

        var declarator = tree.GetRoot().DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .First(v => v.Identifier.ValueText == "x");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            HoistedFieldTypeResolver.Resolve(declarator, model, "K"));

        Assert.Contains("type parameter 'T'", ex.Message);
        var lineSpan = declarator.GetLocation().GetLineSpan();
        Assert.Contains($"({lineSpan.StartLinePosition.Line + 1},", ex.Message);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Resolve_ExplicitOrInferredLocal_ResolvesToCorrectTypeSymbol()
    {
        const string code = """
            public class TestClass
            {
                public static void K()
                {
                    var y = 1u;
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(code);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);

        var declarator = tree.GetRoot().DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .First(v => v.Identifier.ValueText == "y");

        var typeSymbol = HoistedFieldTypeResolver.Resolve(declarator, model, "K");

        Assert.NotNull(typeSymbol);
        Assert.Equal("uint", typeSymbol.ToDisplayString());
    }
}
