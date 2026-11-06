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
    private const string TestCase = "MutantSwitchCases.cs";

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Expression")]
    [InlineData("ChainedExpressions")]
    [InlineData("NestedExpressions")]
    [InlineData("Statement")]
    [InlineData("ChainedStatements")]
    [InlineData("EmbeddedStatement")]
    [InlineData("BodyBlock")]
    [InlineData("SingleStatementBlock")]
    [InlineData("ChainedBlock")]
    [InlineData("Lambda")]
    public void Remove_InstrumentedBody_ReturnsOriginalBody(string shape)
    {
        var (model, methods) = GetMethods();
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
        var (model, methods) = GetMethods();
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
        var (model, methods) = GetMethods();
        var instrumented = GetMethod(methods, "Expression_Instrumented").Body
                           ?? throw new InvalidOperationException("Expression_Instrumented has no block body.");
        var conditional = instrumented.DescendantNodes().OfType<ConditionalExpressionSyntax>().Single();

        var removed = MutantSwitchRemover.Remove(instrumented, model);
        var kept = removed.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression
                   ?? throw new InvalidOperationException("The return statement lost its expression.");

        Assert.Same(conditional.WhenFalse, model.GetOriginalNode(kept));
        Assert.Equal("a + b", kept.ToString());
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("NoSwitch")]
    [InlineData("ForeignSwitches")]
    public void Remove_BodyWithoutStrykerSwitch_ReturnsSameInstance(string methodName)
    {
        var (model, methods) = GetMethods();
        var body = GetMethod(methods, methodName).Body
                   ?? throw new InvalidOperationException($"{methodName} has no block body.");

        Assert.Same(body, MutantSwitchRemover.Remove(body, model));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Remove_WithoutSemanticModel_ReturnsSameInstance()
    {
        var (_, methods) = GetMethods();
        var body = GetMethod(methods, "Expression_Instrumented").Body
                   ?? throw new InvalidOperationException("Expression_Instrumented has no block body.");

        Assert.Same(body, MutantSwitchRemover.Remove(body, null));
    }

    // A compilation without Stryker's MutantControl was not mutated: nothing is a switch, whatever it looks like
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Remove_CompilationWithoutStrykerHelper_ReturnsSameInstance()
    {
        var source = RoslynTestUtility.ReadTestCase(Path.Combine("StateMachines", TestCase))
            .Replace("namespace StrykerAbCdEfGhIjKlMnO", "namespace ContosoAbCdEfGhIjKlMnO", StringComparison.Ordinal)
            .Replace("StrykerAbCdEfGhIjKlMnO.MutantControl", "ContosoAbCdEfGhIjKlMnO.MutantControl", StringComparison.Ordinal);
        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var body = tree.GetRoot()
                       .DescendantNodes()
                       .OfType<MethodDeclarationSyntax>()
                       .Single(static m => m.Identifier.ValueText == "Expression_Instrumented")
                       .Body
                   ?? throw new InvalidOperationException("Expression_Instrumented has no block body.");

        Assert.Same(body, MutantSwitchRemover.Remove(body, model));
    }

    private static (SemanticModel Model, MethodDeclarationSyntax[] Methods) GetMethods()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(Path.Combine("StateMachines", TestCase));
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
