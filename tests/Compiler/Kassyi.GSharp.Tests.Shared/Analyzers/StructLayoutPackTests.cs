using System.IO;
using System.Linq;
using Kassyi.GSharp.Compiler.Layout;
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

        return Assert.IsAssignableFrom<INamedTypeSymbol>(semanticModel.GetDeclaredSymbol(structDecl));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TryGetStructLayoutPack_AbsentPack_ReturnsFalse()
    {
        var (compilation, tree, semanticModel) = RoslynTestUtility.GetTestCaseContext(Path.Combine("Analyzers", "StructLayoutPackTestCase.cs"));
        var symbol = GetStructSymbol(compilation, tree, semanticModel, "StructWithoutPack");

        var hasPack = StructLayoutReader.TryGetPack(symbol, out var pack);

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

        var hasPack = StructLayoutReader.TryGetPack(symbol, out var pack);

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

        var hasPack = StructLayoutReader.TryGetPack(symbol, out var pack);

        Assert.True(hasPack);
        Assert.Equal(4, pack);
    }

    // From a metadata reference, Pack comes from the ClassLayout table, which Roslyn does not show as an attribute (#736)
    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("StructWithoutPack", false, 0)]
    [InlineData("StructWithPackZero", false, 0)]
    [InlineData("StructWithPackFour", true, 4)]
    public void Test_TryGetPack_StructFromMetadata_ReadsTheClassLayoutTable(string structName, bool expectedHasPack, int expectedPack)
    {
        var library = DeviceLibraryBuilder.Build(
            "StructLayoutPackLibrary",
            null,
            RoslynTestUtility.ReadTestCase(Path.Combine("Analyzers", "StructLayoutPackTestCase.cs")));
        var consumer = DeviceLibraryBuilder.CreateConsumer("class Consumer { }", library);
        var assembly = Assert.IsAssignableFrom<IAssemblySymbol>(consumer.GetAssemblyOrModuleSymbol(library));
        var symbol = assembly.GetTypeByMetadataName($"Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.{structName}");
        Assert.NotNull(symbol);

        var hasPack = StructLayoutReader.TryGetPack(symbol, out var pack);

        Assert.True(symbol.DeclaringSyntaxReferences.IsEmpty);
        Assert.Equal(expectedHasPack, hasPack);
        Assert.Equal(expectedPack, pack);
    }
}
