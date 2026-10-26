using System;
using System.IO;
using System.Linq;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 tests for which declarations <see cref="ContextHoister"/> moves into the context (#405): with a semantic
/// model, each declaration is hoisted on its own, so a local that shares its name with a hoisted one but does not live
/// across a barrier stays a local.
/// </summary>
[Category(TestCategory.Compiler)]
public class PreciseHoistingTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Hoist_SiblingLocalThatDoesNotCrossBarrier_StaysLocal()
    {
        var (lowered, model, method) = Lower(withModel: true);

        Assert.Equal(["t = ThreadIndex.X", "y = 2", "z = 3", "z = 4"], DescribeContextFields(lowered));

        var firstY = method.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .First(static d => d.Identifier.ValueText == "y");
        var firstYSymbol = model.GetDeclaredSymbol<ILocalSymbol>(firstY)
                           ?? throw new InvalidOperationException("The first 'y' has no symbol.");

        var localDeclaration = Assert.Single(
            lowered.LoweredBody.DescendantNodes().OfType<VariableDeclaratorSyntax>(),
            static d => d.Identifier.ValueText == "y");
        Assert.Equal("1", localDeclaration.Initializer?.Value.ToString());

        var plainReferences = lowered.LoweredBody.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(static name => name.Identifier.ValueText == "y" && !IsContextMemberName(name))
            .ToArray();
        var plainReference = Assert.Single(plainReferences);
        Assert.True(model.GetSymbol(plainReference).IsEqualTo(firstYSymbol));
    }

    // Without a semantic model declarations are keyed by name, so every declaration that shares a hoisted name moves
    // into the context and the name-keyed rewrite stays consistent
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Hoist_SiblingLocalWithoutModel_HoistsEveryDeclarationOfTheName()
    {
        var (lowered, _, _) = Lower(withModel: false);

        Assert.Equal(["t = ThreadIndex.X", "y = 1", "y = 2", "z = 3", "z = 4"], DescribeContextFields(lowered));
    }

    private static string[] DescribeContextFields(LoweredStateMachine lowered) =>
        lowered.ContextFields
            .Select(static f => $"{f.Identifier.ValueText} = {f.Initializer?.Value}")
            .ToArray();

    private static bool IsContextMemberName(IdentifierNameSyntax name) =>
        name.Parent is MemberAccessExpressionSyntax access &&
        ContextLocalRewriter.IsContextPointerAccess(access) &&
        access.Name == name;

    private static (LoweredStateMachine Lowered, SemanticModel Model, MethodDeclarationSyntax Method) Lower(
        bool withModel)
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(
            Path.Combine("StateMachines", "SiblingHoistDemo.cs"));
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();

        return (AstLoweringPipeline.Lower(method, withModel ? model : null), model, method);
    }
}
