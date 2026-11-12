#nullable enable
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
        var tree = CSharpSyntaxTree.ParseText("class C { }", path: path);

        Assert.Equal(expected, GSharpGeneratedTrees.IsOwnOutput(tree));
    }

    // A generator added to the compiler must be added to the list of GSharpGeneratedTrees as well
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsOwnOutput_EveryGeneratorOfTheCompiler_IsOwnOutput()
    {
        var compilerDirectory = RepositoryPaths.Combine("src", "Compiler", "Kassyi.GSharp.Compiler");
        var generators = Directory.EnumerateFiles(compilerDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(static path => CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot())
            .SelectMany(static root => root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(static type => type.AttributeLists
                .SelectMany(static list => list.Attributes)
                .Any(static attribute => attribute.Name.ToString() == "Generator"))
            .Select(static type => GetNamespace(type) + "." + type.Identifier.Text)
            .ToArray();

        Assert.NotEmpty(generators);
        Assert.All(generators, static name =>
        {
            var tree = CSharpSyntaxTree.ParseText("class C { }", path: $"obj/Kassyi.GSharp.Compiler/{name}/C.g.cs");
            Assert.True(GSharpGeneratedTrees.IsOwnOutput(tree), name);
        });
    }

    private static string GetNamespace(SyntaxNode type) =>
        type.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(static ns => ns.Name.ToString())
            .FirstOrDefault() ?? string.Empty;
}
