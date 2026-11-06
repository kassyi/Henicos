using System;
using System.IO;
using System.Linq;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 tests for <see cref="MutantSwitchRemover"/>: the device code G# copies out of a compilation that Stryker.NET
/// mutated is the code before the mutation (#1191).
/// </summary>
[Category(TestCategory.Compiler)]
public class MutantSwitchRemoverTests
{
    private const string CasesFile = "MutantSwitchCases.cs";
    private const string StrykerHelperFile = "StrykerMutantControl.cs";

    // The cases as Stryker compiles them, with its helper class, and as a normal build compiles them, without it
    private static readonly Lazy<Compilation> s_mutatedCompilation = new(() => CreateCompilation(withStrykerHelper: true));
    private static readonly Lazy<Compilation> s_unmutatedCompilation = new(() => CreateCompilation(withStrykerHelper: false));

    public static TheoryData<string> Shapes =>
    [
        "Expression",
        "ChainedExpressions",
        "NestedExpressions",
        "Statement",
        "ChainedStatements",
        "EmbeddedStatement",
        "BodyBlock",
        "SingleStatementBlock",
        "ChainedBlock",
        "Lambda"
    ];

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(Shapes))]
    public void Remove_InstrumentedBody_ReturnsOriginalBody(string shape)
    {
        var (model, methods) = GetMethods(s_mutatedCompilation.Value);
        var instrumented = GetMethod(methods, shape + "_Instrumented").Body
                           ?? throw new InvalidOperationException($"{shape}_Instrumented has no block body.");
        var original = GetMethod(methods, shape + "_Original").Body
                       ?? throw new InvalidOperationException($"{shape}_Original has no block body.");

        var removed = MutantSwitchRemover.Remove(instrumented, model);

        Assert.Equal(Normalize(original), Normalize(removed));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Remove_InstrumentedExpressionBody_ReturnsOriginalExpressionBody()
    {
        var (model, methods) = GetMethods(s_mutatedCompilation.Value);
        var instrumented = GetMethod(methods, "ExpressionBody_Instrumented").ExpressionBody
                           ?? throw new InvalidOperationException("ExpressionBody_Instrumented has no expression body.");
        var original = GetMethod(methods, "ExpressionBody_Original").ExpressionBody
                       ?? throw new InvalidOperationException("ExpressionBody_Original has no expression body.");

        var removed = MutantSwitchRemover.Remove(instrumented, model);

        Assert.Equal(Normalize(original), Normalize(removed));
    }

    // The copy maps back to the user's tree (generator-invariants §2): the kept branch is the node Stryker left in place
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Remove_InstrumentedBody_KeptBranchMapsToItsNodeInTheCompilation()
    {
        var (model, methods) = GetMethods(s_mutatedCompilation.Value);
        var instrumented = GetMethod(methods, "Expression_Instrumented").Body
                           ?? throw new InvalidOperationException("Expression_Instrumented has no block body.");
        var conditional = instrumented.DescendantNodes().OfType<ConditionalExpressionSyntax>().Single();

        var removed = MutantSwitchRemover.Remove(instrumented, model);
        var kept = removed.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression
                   ?? throw new InvalidOperationException("The return statement lost its expression.");

        Assert.Same(conditional.WhenFalse, model.GetOriginalNode(kept));
        Assert.Equal("a + b", kept.ToString());
    }

    // Every node of the copy maps back to the user's tree (generator-invariants §2), every statement to a statement of
    // the same kind, so the passes after the removal find its symbol and report it at its place
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(Shapes))]
    public void Remove_InstrumentedBody_EveryNodeMapsToTheCompilation(string shape)
    {
        var (model, methods) = GetMethods(s_mutatedCompilation.Value);
        var instrumented = GetMethod(methods, shape + "_Instrumented").Body
                           ?? throw new InvalidOperationException($"{shape}_Instrumented has no block body.");

        var removed = MutantSwitchRemover.Remove(instrumented, model);

        Assert.NotSame(instrumented, removed);
        Assert.All(removed.DescendantNodesAndSelf(), node =>
        {
            var original = model.GetOriginalNode(node);
            Assert.True(original != null, $"{node.Kind()} '{node}' maps to no node of the compilation.");
            // An argument shares its span with its expression, so only a statement's kind identifies its original
            if (node is StatementSyntax)
            {
                Assert.Equal(node.Kind(), original.Kind());
            }
        });
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("NoSwitch")]
    [InlineData("ForeignSwitches")]
    public void Remove_BodyWithoutStrykerSwitch_ReturnsSameInstance(string methodName)
    {
        var (model, methods) = GetMethods(s_mutatedCompilation.Value);
        var body = GetMethod(methods, methodName).Body
                   ?? throw new InvalidOperationException($"{methodName} has no block body.");

        Assert.Same(body, MutantSwitchRemover.Remove(body, model));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Remove_WithoutSemanticModel_ReturnsSameInstance()
    {
        var (_, methods) = GetMethods(s_mutatedCompilation.Value);
        var body = GetMethod(methods, "Expression_Instrumented").Body
                   ?? throw new InvalidOperationException("Expression_Instrumented has no block body.");

        Assert.Same(body, MutantSwitchRemover.Remove(body, null));
    }

    // A compilation without Stryker's MutantControl was not mutated: nothing is a switch, whatever it looks like
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Remove_CompilationWithoutStrykerHelper_ReturnsSameInstance()
    {
        var (model, methods) = GetMethods(s_unmutatedCompilation.Value);
        var body = GetMethod(methods, "Expression_Instrumented").Body
                   ?? throw new InvalidOperationException("Expression_Instrumented has no block body.");

        Assert.Same(body, MutantSwitchRemover.Remove(body, model));
    }

    // The cases file is the last tree, so it is found the same way with and without the helper
    private static Compilation CreateCompilation(bool withStrykerHelper)
    {
        var cases = RoslynTestUtility.ReadTestCase(Path.Combine("StateMachines", CasesFile));
        return withStrykerHelper
            ? RoslynTestUtility.CreateCompilation(
                RoslynTestUtility.ReadTestCase(Path.Combine("StateMachines", StrykerHelperFile)),
                cases)
            : RoslynTestUtility.CreateCompilation(cases);
    }

    private static (SemanticModel Model, MethodDeclarationSyntax[] Methods) GetMethods(Compilation compilation)
    {
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var methods = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .ToArray();
        return (model, methods);
    }

    private static MethodDeclarationSyntax GetMethod(MethodDeclarationSyntax[] methods, string name) =>
        methods.Single(m => m.Identifier.ValueText == name);

    private static string Normalize(SyntaxNode node) => node.NormalizeWhitespace().ToFullString();
}
