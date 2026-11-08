#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Analysis;

/// <summary>
/// Verifies which expressions <see cref="ExpressionTraps"/> says may raise a run-time error (#1275 (A fused StackLinq
/// Skip after Select skips the selector calls, so an exception C# throws is lost)).
/// </summary>
[Category(TestCategory.Compiler)]
public class ExpressionTrapsTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Analysis/ExpressionTrapCases.cs")
    );

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Arithmetic")]
    [InlineData("ConstantDivisor")]
    [InlineData("ConstantLongDivisor")]
    [InlineData("FloatDivision")]
    [InlineData("FloatMath")]
    [InlineData("Conversion")]
    [InlineData("Choice")]
    [InlineData("Field")]
    [InlineData("Length")]
    public void Test_MayTrap_TrapFreeExpression_IsFalse(string name) =>
        Assert.False(MayTrap(name));

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("VariableDivisor")]
    [InlineData("VariableRemainder")]
    [InlineData("MinusOneDivisor")]
    [InlineData("ComputedDivisor")]
    [InlineData("Element")]
    [InlineData("CheckedAdd")]
    [InlineData("CheckedConversion")]
    [InlineData("Call")]
    [InlineData("IntegerMath")]
    [InlineData("ComputedProperty")]
    [InlineData("Throw")]
    public void Test_MayTrap_ExpressionThatMayRaise_IsTrue(string name) =>
        Assert.True(MayTrap(name));

    // The Skip after a selector that may trap stays in the loop, so the selector runs for the element it drops
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("DividingSelectThenSkip")]
    [InlineData("IndexingSelectThenSkip")]
    public void Test_CountRangeStages_StopsAfterATrappingSelector(string name)
    {
        // mutant: CountRangeStages without the trap check folds the Skip as for the pure selector
        Assert.Equal(CountRangeStages("PureSelectThenSkip") - 1, CountRangeStages(name));
        Assert.Equal(1, CountRangeStages(name));
    }

    private static int CountRangeStages(string name)
    {
        var body = GetBody("ExpressionTrapCases.Chains", name);
        var model = s_compilation.GetSemanticModel(body.SyntaxTree);
        var terminal = (InvocationExpressionSyntax)body;
        var symbol = model.GetSymbolInfo(terminal).Symbol as IMethodSymbol ??
                     throw new InvalidOperationException($"The chain of '{name}' does not bind.");
        Assert.True(StackLinqChainExtractor.TryExtract(terminal, model, symbol, out var chain));
        return StackLinqChainExtractor.CountRangeStages(in chain, model);
    }

    private static bool MayTrap(string name)
    {
        var body = GetBody("ExpressionTrapCases.Traps", name);
        return ExpressionTraps.MayTrap(body, s_compilation.GetSemanticModel(body.SyntaxTree));
    }

    private static ExpressionSyntax GetBody(string typeName, string name)
    {
        var type = s_compilation.GetTypeByMetadataName(typeName) ??
                   throw new InvalidOperationException($"Type '{typeName}' not found.");
        var method = type.GetMembers(name).OfType<IMethodSymbol>().First();
        var declaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();
        return declaration.ExpressionBody?.Expression ??
               throw new InvalidOperationException($"Method '{name}' has no expression body.");
    }
}
