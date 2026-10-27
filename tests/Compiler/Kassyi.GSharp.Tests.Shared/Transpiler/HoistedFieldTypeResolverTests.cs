using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Lowering;
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
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Transpiler/HoistedFieldTypeResolver/GenericTypeParameterLocal.cs"));
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

    // A type parameter inside a type argument or inside the containing type of a nested type is rejected as well
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("GenericTypeArgumentLocal.cs")]
    [InlineData("NestedInGenericLocal.cs")]
    public void Resolve_TypeParameterInsideTheType_ThrowsInvalidOperationException(string testCase)
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Transpiler/HoistedFieldTypeResolver/" + testCase));
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var declarator = FindDeclarator(tree, "x");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            HoistedFieldTypeResolver.Resolve(declarator, model, "K"));

        Assert.Contains("the type contains the type parameter 'T'; generic kernels are not supported", ex.Message);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Resolve_ConstructedTypeWithoutTypeParameter_ResolvesToThatType()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Transpiler/HoistedFieldTypeResolver/GenericTypeArgumentLocal.cs"));
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);

        var typeSymbol = HoistedFieldTypeResolver.Resolve(FindDeclarator(tree, "ok"), model, "K");

        Assert.Equal("Wrapper<int>", typeSymbol.ToDisplayString());
    }

    private static VariableDeclaratorSyntax FindDeclarator(SyntaxTree tree, string name) =>
        tree.GetRoot().DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .First(v => v.Identifier.ValueText == name);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Resolve_DeclaratorMappedToParameter_ResolvesToParameterType()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Transpiler/HoistedFieldTypeResolver/ParameterLocal.cs"));
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var parameter = tree.GetRoot().DescendantNodes()
            .OfType<ParameterSyntax>()
            .First(p => p.Identifier.ValueText == "p");

        // An expansion turns a parameter into a local: `Pair p;`, whose declarator carries the parameter's span
        var declarator = SyntaxFactory.VariableDeclarator("p")
            .WithAdditionalAnnotations(new SyntaxAnnotation(
                LoweringAnnotations.OriginalSpanAnnotationKind,
                LoweringAnnotations.FormatOriginalSpanData(parameter)));
        var declaration = SyntaxFactory.VariableDeclaration(
            SyntaxFactory.IdentifierName("Pair"),
            SyntaxFactory.SingletonSeparatedList(declarator));
        var lowered = declaration.Variables[0];

        var typeSymbol = HoistedFieldTypeResolver.Resolve(lowered, model, "K");

        Assert.Equal("Pair", typeSymbol.ToDisplayString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Resolve_ExplicitOrInferredLocal_ResolvesToCorrectTypeSymbol()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Transpiler/HoistedFieldTypeResolver/InferredLocal.cs"));
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
