#nullable enable
using Kassyi.GSharp.Compiler.Transpiler.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Verifies what <see cref="ExpressionEffects"/> says an expression reads and writes, and which operands
/// <see cref="EvaluationOrder.LastSpilledOperand"/> moves into temporaries to keep the C# order (#608).
/// </summary>
[Category(TestCategory.Compiler)]
public class EvaluationOrderAnalysisTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Transpiler/EvaluationOrderCases.cs")
    );

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("TwoNextCalls", 1)]
    [InlineData("LocalThenRefWrite", 1)]
    [InlineData("MemoryReadThenWrite", 1)]
    [InlineData("IncrementThenRead", 1)]
    [InlineData("InterferenceBeforeLastArgument", 1)]
    [InlineData("BinaryOperands", 1)]
    [InlineData("CapturingLambdaCall", 1)]
    public void Test_LastSpilledOperand_InterferingOperands_IsTheLastOneThatInterferes(string method, int expected) =>
        Assert.Equal(expected, EvaluationOrder.LastSpilledOperand(GetOperandEffects(method)));

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("PureAndElement")]
    [InlineData("TwoElements")]
    [InlineData("OneSideEffect")]
    [InlineData("MathWithOneSideEffect")]
    [InlineData("LambdaIsNotEvaluated")]
    [InlineData("RefArgumentsOnly")]
    [InlineData("PureLambdaCall")]
    public void Test_LastSpilledOperand_IndependentOperands_IsNone(string method) =>
        Assert.Equal(-1, EvaluationOrder.LastSpilledOperand(GetOperandEffects(method)));

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Constant")]
    [InlineData("ThreadIndexRead")]
    public void Test_Of_ConstantsAndIntrinsics_AreEmpty(string method)
    {
        var effects = GetEffects(method);

        Assert.True(effects.IsEmpty);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Of_LocalRead_ReadsTheLocal()
    {
        var (effects, i) = GetEffects("LocalRead", "i");

        Assert.True(effects.Reads.Contains(i));
        Assert.False(effects.Reads.Memory);
        Assert.True(effects.Writes.IsEmpty);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Of_Increment_ReadsAndWritesTheLocal()
    {
        var (effects, i) = GetEffects("Increment", "i");

        Assert.True(effects.Reads.Contains(i));
        Assert.True(effects.Writes.Contains(i));
        Assert.False(effects.Writes.Memory);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Of_ElementRead_ReadsMemoryAndTheIndex()
    {
        var (effects, i) = GetEffects("ElementRead", "i");

        Assert.True(effects.Reads.Memory);
        Assert.True(effects.Reads.Contains(i));
        Assert.True(effects.Writes.IsEmpty);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Of_CallWithRefArgument_WritesTheArgumentOnly()
    {
        var (effects, i) = GetEffects("RefCall", "i");

        Assert.True(effects.Writes.Contains(i));
        Assert.False(effects.Writes.Memory);
        // The location of a ref argument is evaluated; the call reads the variable afterwards
        Assert.False(effects.Reads.Contains(i));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("MemoryCall", true)]
    [InlineData("PureCall", false)]
    public void Test_Of_Call_WritesWhatTheCalleeWrites(string method, bool writesMemory)
    {
        var effects = GetEffects(method);

        Assert.Equal(writesMemory, effects.Writes.Memory);
        Assert.True(effects.Reads.Memory);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Of_RefParameterRead_ReadsMemory()
    {
        var effects = GetEffects("RefParameterRead");

        Assert.True(effects.Reads.Memory);
        Assert.True(effects.Writes.IsEmpty);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Of_Conditional_IsTheUnionOfItsOperands()
    {
        var (effects, i) = GetEffects("ConditionalUnion", "i");

        Assert.True(effects.Writes.Contains(i));
        Assert.True(effects.Reads.Memory);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Of_LocalFunctionCall_MayTouchEveryLocal()
    {
        var effects = GetEffects("LocalFunctionCall");

        Assert.True(effects.Writes.AllLocals);
        Assert.True(effects.Writes.Memory);
    }

    // The operands of the result expression in the C# order: the arguments of a call (the location of a ref argument),
    // or the two sides of a binary operator
    private static ExpressionEffects[] GetOperandEffects(string method)
    {
        var (expression, model) = GetResult(method);
        return expression switch
        {
            InvocationExpressionSyntax invocation => invocation.ArgumentList.Arguments
                .Select(a => a.RefKindKeyword.IsKind(SyntaxKind.None)
                    ? ExpressionEffects.Of(a.Expression, model)
                    : ExpressionEffects.OfLocation(a.Expression, model))
                .ToArray(),
            BinaryExpressionSyntax binary =>
                [ExpressionEffects.Of(binary.Left, model), ExpressionEffects.Of(binary.Right, model)],
            _ => throw new InvalidOperationException($"'{method}' has no call or binary operator.")
        };
    }

    private static ExpressionEffects GetEffects(string method)
    {
        var (expression, model) = GetResult(method);
        return ExpressionEffects.Of(expression, model);
    }

    private static (ExpressionEffects Effects, ISymbol Local) GetEffects(string method, string local)
    {
        var (expression, model) = GetResult(method);
        var declaration = expression.FirstAncestorOrSelf<MethodDeclarationSyntax>() ??
                          throw new InvalidOperationException($"'{method}' is not in a method.");
        var symbol = declaration.DescendantNodes()
                         .Where(n => n is VariableDeclaratorSyntax or ParameterSyntax)
                         .Select(n => model.GetDeclaredSymbol(n))
                         .FirstOrDefault(s => s?.Name == local) ??
                     throw new InvalidOperationException($"'{method}' declares no local or parameter '{local}'.");
        return (ExpressionEffects.Of(expression, model), symbol);
    }

    private static (ExpressionSyntax Expression, SemanticModel Model) GetResult(string method)
    {
        var tree = s_compilation.SyntaxTrees.First(t => t.ToString().Contains("namespace OrderCases"));
        var model = s_compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.ValueText == method);
        var result = declaration.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Last(v => v.Identifier.ValueText == "result");
        return (result.Initializer?.Value ?? throw new InvalidOperationException(method), model);
    }
}
