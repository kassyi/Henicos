#nullable enable
using Kassyi.GSharp.Compiler.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class GSharpGeneratedTreesTests
{
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Kassyi.GSharp.Compiler/Kassyi.GSharp.Compiler.GpuKernelGenerator/K.g.cs", true)]
    [InlineData(@"C:\obj\Kassyi.GSharp.Compiler\Kassyi.GSharp.Compiler.TrustedKeysGenerator\K.g.cs", true)]
    [InlineData("/obj/Kassyi.GSharp.Compiler/Kassyi.GSharp.Compiler.NativeApi.NativeApiGenerator/K.g.cs", true)]
    [InlineData("Kassyi.GSharp.Compiler/K.g.cs", false)]
    [InlineData("Kassyi.GSharp.Compiler/Kassyi.GSharp.Compiler.GpuKernelGenerator", false)]
    [InlineData("Kassyi.GSharp.Compiler/Kassyi.GSharp.Compiler.Helpers/K.cs", false)]
    [InlineData("Kassyi.GSharp.Compiler/Kassyi.GSharp.Compiler.GpuKernelGenerator/Sub/K.g.cs", false)]
    [InlineData("MyKassyi.GSharp.Compiler/Kassyi.GSharp.Compiler.GpuKernelGenerator/K.g.cs", false)]
    [InlineData("Other/Other.Generator/K.g.cs", false)]
    [InlineData("", false)]
    public void IsOwnOutput_TreePath_IsTrueOnlyForTheGeneratorsOfTheCompiler(string path, bool expected)
    {
        var tree = CSharpSyntaxTree.Create(SyntaxFactory.CompilationUnit(), path: path);

        Assert.Equal(expected, GSharpGeneratedTrees.IsOwnOutput(tree));
    }

    // A generator added to the compiler must be added to the list of GSharpGeneratedTrees as well
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsOwnOutput_EveryGeneratorOfTheCompiler_IsOwnOutput()
    {
        var compilation = RoslynTestUtility.CompilerSourceCompilation;
        var generatorAttribute = compilation.GetTypeByMetadataName("Microsoft.CodeAnalysis.GeneratorAttribute");
        Assert.NotNull(generatorAttribute);
        // The full name of the generator type, as Roslyn writes it into the path of a generated tree
        var generators = compilation.SyntaxTrees
            .SelectMany(static tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Select(syntax => compilation.GetSemanticModel(syntax.SyntaxTree).GetDeclaredSymbol(syntax))
            .OfType<INamedTypeSymbol>()
            .Where(type => type.GetAttributes()
                .Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, generatorAttribute)))
            .Select(static type => type.ContainingNamespace.GetMetadataNamespaceName() + "." + type.Name)
            .ToArray();

        Assert.NotEmpty(generators);
        Assert.All(generators, static name =>
        {
            var tree = CSharpSyntaxTree.Create(
                SyntaxFactory.CompilationUnit(),
                path: $"obj/Kassyi.GSharp.Compiler/{name}/C.g.cs");
            Assert.True(GSharpGeneratedTrees.IsOwnOutput(tree), name);
        });
    }
}
