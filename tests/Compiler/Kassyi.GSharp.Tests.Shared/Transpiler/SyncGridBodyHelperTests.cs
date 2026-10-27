#nullable enable
using System.Linq;
using Kassyi.GSharp.Compiler;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Verifies the statements of a <c>Gpu.SyncGrid(body)</c> and the gaps that get a grid sync (<see cref="SyncGridBody"/>),
/// the one definition that the CUDA emission and the CPU lowering share (#626). Each case runs with the semantic model
/// (the transpiler) and without it (the lowering tests of Tier 2, which classify by syntax).
/// </summary>
[Category(TestCategory.Compiler)]
public class SyncGridBodyHelperTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Transpiler/SyncGridBodyCases.cs")
    );

    // The sync points as a string: one character per flattened statement, 'S' when a grid sync follows it, '-' when not
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("TwoStatements", "S-")]
    [InlineData("NestedBody", "SSS-")]
    [InlineData("NestedExpressionBody", "SS-")]
    [InlineData("NestedFor", "SS-")]
    [InlineData("BlockBarrier", "SS-")]
    public void Test_IsSyncPoint_StatementsInARow_SyncBetweenEach(string method, string expected)
    {
        foreach (var withModel in new[] { true, false })
        {
            Assert.Equal(expected, GetSyncPoints(method, withModel));
        }
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Empty", "")]
    [InlineData("OneStatement", "-")]
    [InlineData("ExpressionBody", "-")]
    [InlineData("DirectNested", "-")]
    [InlineData("ExplicitNeighbour", "---")]
    [InlineData("ExplicitBetweenEmpties", "-----")]
    [InlineData("TrailingEmpty", "S--")]
    [InlineData("LocalFunction", "-S-")]
    [InlineData("LocalFunctionLast", "--")]
    public void Test_IsSyncPoint_EdgeGaps_NoSyncAtTheEndOrNextToExplicitSyncsEmptiesAndDeclarations(
        string method, string expected)
    {
        foreach (var withModel in new[] { true, false })
        {
            Assert.Equal(expected, GetSyncPoints(method, withModel));
        }
    }

    // The nested body's statements are flattened in, in source order, and remember which body they came from
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Flatten_NestedBody_RecordsTheBodyOfEachStatement()
    {
        foreach (var withModel in new[] { true, false })
        {
            var statements = Flatten("NestedBody", withModel);

            Assert.Equal(
                ["x += 1;", "x += 2;", "x += 3;", "x += 4;"],
                statements.Elements.Select(static e => e.Node.ToString()).ToArray());
            Assert.Equal([0, 1, 1, 0], statements.Elements.Select(static e => e.BodyIndex).ToArray());
            Assert.Equal([-1, 0], statements.BodyParents.ToArray());
        }
    }

    // An expression body is one statement: the expression itself
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Flatten_NestedExpressionBody_IsTheExpression()
    {
        foreach (var withModel in new[] { true, false })
        {
            var statements = Flatten("NestedExpressionBody", withModel);

            var nested = Assert.IsAssignableFrom<ExpressionSyntax>(statements.Elements[1].Node);
            Assert.Equal("x += 2", nested.ToString());
            Assert.Equal(1, statements.Elements[1].BodyIndex);
        }
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Empty", false)]
    [InlineData("ExplicitBetweenEmpties", true)]
    [InlineData("LocalFunctionLast", true)]
    public void Test_HasExecutableStatements_OnlyEmptiesCount(string method, bool expected)
    {
        foreach (var withModel in new[] { true, false })
        {
            Assert.Equal(expected, Flatten(method, withModel).HasExecutableStatements);
        }
    }

    // A user type named Gpu is not the macro host; only the syntactic classification (no model) cannot tell
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_TryGetBody_LookalikeGpuType_IsABodyOnlyWithoutAModel()
    {
        var (statement, model) = GetSyncGridStatement("Lookalike", withModel: true);
        Assert.False(SyncGridBody.TryGetBody(statement, model, out _));

        var (syntaxStatement, _) = GetSyncGridStatement("Lookalike", withModel: false);
        Assert.True(SyncGridBody.TryGetBody(syntaxStatement, null, out _));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("ExplicitNeighbour", 1, true)]
    [InlineData("ExplicitNeighbour", 0, false)]
    [InlineData("BlockBarrier", 1, false)]
    public void Test_IsExplicitSync_ParameterlessSyncGridOnly(string method, int index, bool expected)
    {
        foreach (var withModel in new[] { true, false })
        {
            var model = withModel ? GetModel() : null;
            var statements = Flatten(method, withModel);

            Assert.Equal(expected, SyncGridBody.IsExplicitSync(statements.Elements[index].Node, model));
        }
    }

    private static string GetSyncPoints(string method, bool withModel)
    {
        var model = withModel ? GetModel() : null;
        var elements = Flatten(method, withModel).Elements;
        return new string(Enumerable.Range(0, elements.Length)
            .Select(i => SyncGridBody.IsSyncPoint(elements, i, model) ? 'S' : '-')
            .ToArray());
    }

    private static SyncGridStatements Flatten(string method, bool withModel)
    {
        var (statement, model) = GetSyncGridStatement(method, withModel);
        Assert.True(SyncGridBody.TryGetBody(statement, model, out var body));
        return SyncGridBody.Flatten(body, model);
    }

    private static SemanticModel GetModel() => s_compilation.GetSemanticModel(s_compilation.SyntaxTrees.Single());

    // The first statement of the method: with a model from the compilation's tree, without one from a fresh parse
    private static (StatementSyntax Statement, SemanticModel? Model) GetSyncGridStatement(string method, bool withModel)
    {
        var tree = withModel
            ? s_compilation.SyntaxTrees.Single()
            : CSharpSyntaxTree.ParseText(s_compilation.SyntaxTrees.Single().GetText());
        var statement = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == method)
            .Body?.Statements[0];
        Assert.NotNull(statement);
        return (statement, withModel ? GetModel() : null);
    }
}
