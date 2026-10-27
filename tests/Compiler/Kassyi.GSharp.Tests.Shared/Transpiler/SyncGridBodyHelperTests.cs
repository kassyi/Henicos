#nullable enable
using System.Collections.Immutable;
using System.Linq;
using Kassyi.GSharp.Compiler;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Verifies the statements of a <c>Gpu.SyncGrid(body)</c> and the gaps that get a grid sync (<see cref="SyncGridBody"/>),
/// the one definition that the CUDA emission and the CPU lowering share (#626). Each case runs with the semantic model
/// (the transpiler, <c>withModel: true</c>) and without it (the lowering tests of Tier 2, which classify by syntax).
/// </summary>
[Category(TestCategory.Compiler)]
public class SyncGridBodyHelperTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Transpiler/SyncGridBodyCases.cs")
    );

    public static TheoryData<string, string, bool> StatementsInARow => WithAndWithoutModel(
        ("TwoStatements", "S-"),
        ("NestedBody", "SSS-"),
        ("NestedExpressionBody", "SS-"),
        ("NestedFor", "SS-"),
        ("BlockBarrier", "SS-"),
        ("ExpressionBodyIsBody", "S-")
    );

    public static TheoryData<string, string, bool> EdgeGaps => WithAndWithoutModel(
        ("Empty", ""),
        ("OneStatement", "-"),
        ("ExpressionBody", "-"),
        ("DirectNested", "-"),
        ("ExplicitNeighbour", "---"),
        ("ExplicitBetweenEmpties", "-----"),
        ("TrailingEmpty", "S--"),
        ("LabeledEmpty", "S--"),
        ("LocalFunction", "-S-"),
        ("LocalFunctionLast", "--")
    );

    public static TheoryData<string, bool, bool> ExecutableStatements => new()
    {
        { "Empty", false, true },
        { "Empty", false, false },
        { "ExplicitBetweenEmpties", true, true },
        { "ExplicitBetweenEmpties", true, false },
        { "LocalFunctionLast", true, true },
        { "LocalFunctionLast", true, false }
    };

    public static TheoryData<string, int, bool, bool> ExplicitSyncs => new()
    {
        { "ExplicitNeighbour", 1, true, true },
        { "ExplicitNeighbour", 1, true, false },
        { "ExplicitNeighbour", 0, false, true },
        { "ExplicitNeighbour", 0, false, false },
        { "BlockBarrier", 1, false, true },
        { "BlockBarrier", 1, false, false }
    };

    // The sync points as a string: one character per flattened statement, 'S' when a grid sync follows it, '-' when not
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(StatementsInARow))]
    public void Test_IsSyncPoint_StatementsInARow_SyncBetweenEach(string method, string expected, bool withModel) =>
        Assert.Equal(expected, GetSyncPoints(method, withModel));

    // An empty statement counts as one also with a label (L: ;)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(EdgeGaps))]
    public void Test_IsSyncPoint_EdgeGaps_NoSyncAtTheEndOrNextToExplicitSyncsEmptiesAndDeclarations(
        string method, string expected, bool withModel) =>
        Assert.Equal(expected, GetSyncPoints(method, withModel));

    // The nested body's statements are flattened in, in source order, and remember which body they came from
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(true)]
    [InlineData(false)]
    public void Test_Flatten_NestedBody_RecordsTheBodyOfEachStatement(bool withModel)
    {
        var statements = Flatten("NestedBody", withModel);

        Assert.Equal(
            ["x += 1;", "x += 2;", "x += 3;", "x += 4;"],
            statements.Elements.Select(static e => e.Node.ToString()).ToArray());
        Assert.Equal([0, 1, 1, 0], statements.Elements.Select(static e => e.BodyIndex).ToArray());
        Assert.Equal([-1, 0], statements.BodyParents.ToArray());
    }

    // An expression body is one statement: the expression itself
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(true)]
    [InlineData(false)]
    public void Test_Flatten_NestedExpressionBody_IsTheExpression(bool withModel)
    {
        var statements = Flatten("NestedExpressionBody", withModel);

        var nested = Assert.IsAssignableFrom<ExpressionSyntax>(statements.Elements[1].Node);
        Assert.Equal("x += 2", nested.ToString());
        Assert.Equal(1, statements.Elements[1].BodyIndex);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ExecutableStatements))]
    public void Test_HasExecutableStatements_OnlyEmptiesCount(string method, bool expected, bool withModel) =>
        Assert.Equal(expected, Flatten(method, withModel).HasExecutableStatements);

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
    [MemberData(nameof(ExplicitSyncs))]
    public void Test_IsExplicitSync_ParameterlessSyncGridOnly(string method, int index, bool expected, bool withModel)
    {
        var model = withModel ? GetModel() : null;
        var statements = Flatten(method, withModel);

        Assert.Equal(expected, SyncGridBody.IsExplicitSync(statements.Elements[index].Node, model));
    }

    // An expression body that is itself a Gpu.SyncGrid(body) is that body, nested in the outer one
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(true)]
    [InlineData(false)]
    public void Test_Flatten_ExpressionBodyIsABody_FlattensItsStatements(bool withModel)
    {
        var statements = Flatten("ExpressionBodyIsBody", withModel);

        Assert.Equal(["x += 1;", "x += 2;"], statements.Elements.Select(static e => e.Node.ToString()).ToArray());
        Assert.Equal([-1, 0], statements.BodyParents.ToArray());
    }

    // Which statements hold a return of their own body, and which flags guard each statement: '-' for none, else the
    // guarding bodies joined by '.'
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(true)]
    [InlineData(false)]
    public void Test_Guards_ReturnsInOuterAndNestedBodies_GuardTheLaterStatementsOfTheirBodies(bool withModel)
    {
        var model = withModel ? GetModel() : null;
        var statements = Flatten("ReturnGuards", withModel);

        Assert.Equal(
            [false, true, false, true, false, false, false, false],
            statements.Elements.Select(static e => e.HasBodyReturn).ToArray());
        Assert.Equal(
            ["-", "-", "0", "0", "0.1", "-", "-", "0"],
            Enumerable.Range(0, statements.Elements.Length)
                .Select(i => Describe(statements.GetGuardBodies(i, model)))
                .ToArray());
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(true)]
    [InlineData(false)]
    public void Test_ContainsBodyReturn_ReturnsOfLambdasAndLocalFunctions_AreNotTheBodys(bool withModel)
    {
        var statements = Flatten("ReturnsOfNestedFunctions", withModel);

        Assert.All(statements.Elements, static e => Assert.False(e.HasBodyReturn));
    }

    private static TheoryData<string, string, bool> WithAndWithoutModel(params (string Method, string Expected)[] rows)
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var (method, expected) in rows)
        {
            data.Add(method, expected, true);
            data.Add(method, expected, false);
        }

        return data;
    }

    private static string Describe(ImmutableArray<int> bodies) =>
        bodies.IsEmpty ? "-" : string.Join(".", bodies);

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
