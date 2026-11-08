#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The overload of an operator macro's target that <see cref="OperatorMacroOverloadResolver.ResolveOverload"/>
/// chooses, as C# overload resolution would choose it for a call with the operator's types (#478), on the operators of
/// <c>TestCases/Transpiler/OperatorMacroPlanCases.cs</c>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Compiler)]
public class OperatorMacroOverloadResolverTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Transpiler/OperatorMacroPlanCases.cs")
    );

    // Add(ref Value, in Value, in Value) is declared first; the value parameters are better for the same types
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Resolve_SameTypes_PrefersValueParametersOverIn()
    {
        var (method, hasContext) = Resolve("op_Addition", "Add");

        Assert.False(hasContext);
        Assert.Equal(
            new[] { RefKind.Ref, RefKind.None, RefKind.None },
            method.Parameters.Select(static p => p.RefKind)
        );
    }

    // Mul(.., long) is declared first; int is the operand's own type
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Resolve_ExactOperandType_IsBetterThanAConversion() =>
        Assert.Equal(SpecialType.System_Int32, Resolve("op_Multiply", "Mul").Method.Parameters[2].Type.SpecialType);

    // byte converts to uint and to int, and neither of those to the other: the signed one wins (C# spec §12.6.4.7)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Resolve_NeitherConvertsToTheOther_PrefersTheSignedType() =>
        Assert.Equal(SpecialType.System_Int32, Resolve("op_Division", "Scale").Method.Parameters[2].Type.SpecialType);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Resolve_ContextOfAValueType_IsTakenBeforeTheResult()
    {
        var (method, hasContext) = Resolve("op_Subtraction", "Sub");

        Assert.True(hasContext);
        Assert.Equal("Ctx", method.Parameters[0].Type.Name);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Resolve_NoMethodOfThatName_ReportsIt()
    {
        var resolved = OperatorMacroOverloadResolver.ResolveOverload(GetType("Ops"), "Missing",
            GetOperator("op_Addition"), s_compilation, out var failure);

        Assert.Null(resolved);
        Assert.Equal("has no overload that takes (Value result, Value left, Value right): no method has that name",
            failure);
    }

    private static (IMethodSymbol Method, bool HasContext) Resolve(string operatorName, string targetName)
    {
        var resolved = OperatorMacroOverloadResolver.ResolveOverload(GetType("Ops"), targetName,
            GetOperator(operatorName), s_compilation, out var failure);
        Assert.True(resolved.HasValue, failure);
        Assert.Equal(string.Empty, failure);
        return resolved.Value;
    }

    private static IMethodSymbol GetOperator(string name) =>
        GetType("Value").GetMembers(name).OfType<IMethodSymbol>().Single();

    private static INamedTypeSymbol GetType(string name) =>
        s_compilation.GetTypeByMetadataName("OperatorMacroPlanCases." + name) ??
        throw new InvalidOperationException($"The test case declares no type {name}.");
}
