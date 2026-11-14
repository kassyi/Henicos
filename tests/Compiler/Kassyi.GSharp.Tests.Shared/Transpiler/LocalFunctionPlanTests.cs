#nullable enable
using System;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Locals;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// The plan of the local functions of a statement list (<see cref="LocalFunctionPlan"/>): which statement uses which
/// local function, and which ones form a group. The writer only follows it, so these checks need no snapshot.
/// </summary>
[Category(TestCategory.Compiler)]
public class LocalFunctionPlanTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Transpiler/LocalFunctionPlanCases.cs")
    );

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_ListWithoutLocalFunction_HasNoPlan()
    {
        var (statements, model) = GetStatements("NoLocalFunction");

        Assert.Null(LocalFunctionPlan.Create(statements, model));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_StatementUsingLaterFunctions_ListsThemInOrder()
    {
        var (statements, model) = GetStatements("UsedAhead");

        var plan = LocalFunctionPlan.Create(statements, model);

        Assert.NotNull(plan);
        // 0: int total = ...; 1: return; 2: First; 3: Second
        Assert.Equal([2, 3], plan.Uses(0));
        Assert.Empty(plan.Uses(1));
        Assert.Equal([2], plan.Uses(3));
        Assert.False(plan.IsRecursive(2));
        Assert.False(plan.IsRecursive(3));
        Assert.Equal([3], plan.Group(3));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_FunctionCallingItself_IsRecursiveWithItselfAsGroup()
    {
        var (statements, model) = GetStatements("SelfRecursive");

        var plan = LocalFunctionPlan.Create(statements, model);

        Assert.NotNull(plan);
        Assert.Equal([1], plan.Uses(0));
        Assert.True(plan.IsRecursive(1));
        Assert.Equal([1], plan.Group(1));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_FunctionsCallingEachOther_FormOneGroupWithoutTheCaller()
    {
        var (statements, model) = GetStatements("MutualRecursive");

        var plan = LocalFunctionPlan.Create(statements, model);

        Assert.NotNull(plan);
        // 0: return; 1: A; 2: B; 3: C, which uses A but is not reached by it
        Assert.Equal([1, 2], plan.Group(1));
        Assert.Equal([1, 2], plan.Group(2));
        Assert.True(plan.IsRecursive(1));
        Assert.True(plan.IsRecursive(2));
        Assert.False(plan.IsRecursive(3));
        Assert.Equal([3], plan.Group(3));
        Assert.Equal([1, 3], plan.Uses(0));
    }

    private static (SyntaxList<StatementSyntax> Statements, SemanticModel Model) GetStatements(string name)
    {
        var type = s_compilation.GetTypeByMetadataName("LocalFunctionPlanCases.Shapes") ??
                   throw new InvalidOperationException("Type 'LocalFunctionPlanCases.Shapes' not found.");
        var method = (IMethodSymbol)type.GetMembers(name)[0];
        var declaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();
        var body = declaration.Body ?? throw new InvalidOperationException($"{name} has no body.");
        return (body.Statements, s_compilation.GetSemanticModel(declaration.SyntaxTree));
    }
}
