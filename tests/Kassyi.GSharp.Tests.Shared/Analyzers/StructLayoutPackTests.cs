using System.IO;
using System.Linq;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Tests.Infrastructure;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class StructLayoutPackTests
{
    private static INamedTypeSymbol GetStructSymbol(Compilation compilation, SyntaxTree tree, SemanticModel semanticModel, string structName)
    {
        var structDecl = tree.GetRoot().DescendantNodes()
            .OfType<StructDeclarationSyntax>()
            .First(s => s.Identifier.Text == structName);

        return semanticModel.GetDeclaredSymbol(structDecl)!;
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TryGetStructLayoutPack_AbsentPack_ReturnsFalse()
    {
        var (compilation, tree, semanticModel) = RoslynTestUtility.GetTestCaseContext(Path.Combine("Analyzers", "StructLayoutPackTestCase.cs"));
        var symbol = GetStructSymbol(compilation, tree, semanticModel, "StructWithoutPack");

        var hasPack = symbol.TryGetStructLayoutPack(out var pack);

        Assert.False(hasPack);
        Assert.Equal(0, pack);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public void Test_TryGetStructLayoutPack_PackZero_ReturnsFalse()
    {
        var (compilation, tree, semanticModel) = RoslynTestUtility.GetTestCaseContext(Path.Combine("Analyzers", "StructLayoutPackTestCase.cs"));
        var symbol = GetStructSymbol(compilation, tree, semanticModel, "StructWithPackZero");

        var hasPack = symbol.TryGetStructLayoutPack(out var pack);

        Assert.False(hasPack);
        Assert.Equal(0, pack);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TryGetStructLayoutPack_PackFour_ReturnsTrueAndPackValue()
    {
        var (compilation, tree, semanticModel) = RoslynTestUtility.GetTestCaseContext(Path.Combine("Analyzers", "StructLayoutPackTestCase.cs"));
        var symbol = GetStructSymbol(compilation, tree, semanticModel, "StructWithPackFour");

        var hasPack = symbol.TryGetStructLayoutPack(out var pack);

        Assert.True(hasPack);
        Assert.Equal(4, pack);
    }
}
