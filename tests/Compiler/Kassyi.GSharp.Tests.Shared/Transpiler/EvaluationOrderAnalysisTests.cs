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
    [InlineData("WriteThenRefRead", 1)]
    [InlineData("RefLocalAlias", 1)]
    [InlineData("PointerAlias", 1)]
    [InlineData("CapturedAlias", 1)]
    [InlineData("RefReturnAlias", 1)]
    [InlineData("MutatingStructGetterRead", 1)]
    [InlineData("ReceiverMutatingGetterRead", 1)]
    [InlineData("ReceiverMutatingIndexerRead", 1)]
    [InlineData("ReceiverMutatingGetterCall", 1)]
    [InlineData("ReceiverMemoryWritingGetterRead", 1)]
    [InlineData("ImplicitThisMutatingGetterRead", 1)]
    [InlineData("ImplicitThisReceiverGetterRead", 1)]
    [InlineData("CompoundMemoryWritingGetter", 1)]
    [InlineData("SetterReadingMemory", 1)]
    [InlineData("InitializerSetterWritingMemory", 1)]
    [InlineData("WithSetterWritingMemory", 1)]
    [InlineData("FieldThenWrite", 1)]
    public void LastSpilledOperand_InterferingOperands_IsTheLastOneThatInterferes(string method, int expected) =>
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
    [InlineData("ArrayLengthAndElement")]
    [InlineData("StructPureGetterAndConstant")]
    [InlineData("PureStructGetterRead")]
    [InlineData("ReceiverPureGetterRead")]
    [InlineData("ImplicitThisPureGetterRead")]
    public void LastSpilledOperand_IndependentOperands_IsNone(string method) =>
        Assert.Equal(-1, EvaluationOrder.LastSpilledOperand(GetOperandEffects(method)));

    // A store through the array and the index in temporaries: an array that may be null is checked at the store; a
    // stack array is never null and is bound by reference, since a copy of a C array would decay to a pointer
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ElementTarget", false, true)]
    [InlineData("StackArrayElementTarget", true, false)]
    public void PlanElementStore_ArrayKind_BindsAndChecksAsTheArrayNeeds(string method, bool byReference,
        bool checks)
    {
        var (element, model) = GetResult(method);

        var plan = ArrayNullCheck.PlanElementStore((ElementAccessExpressionSyntax)element, model, table: null,
            currentMemberName: null);

        Assert.Equal(byReference, plan.BindsArrayByReference);
        Assert.Equal(checks, plan.Site is not null);
    }

    // A check that may throw in one operand and a write to memory in the other: C# decides whether the write is made
    // before the exception (#1786 (a throwing check in a later operand runs before an earlier call on MSVC))
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("WriteThenLength")]
    [InlineData("LengthThenWrite")]
    [InlineData("ElementWriteThenLength")]
    [InlineData("ElementIncrementThenLength")]
    [InlineData("WriteThenLengthArgument")]
    [InlineData("WriteThenDivision")]
    [InlineData("WriteThenNullableValue")]
    [InlineData("WriteThenNotNull")]
    [InlineData("DivisionThenWrite")]
    public void LastSpilledOperand_CheckAndMemoryWrite_SpillsBothOperands(string method)
    {
        var operands = GetOperandEffects(method);

        // mutant: ThrowInterferesWith always false leaves both operands in place
        Assert.True(operands[0].ThrowInterferesWith(operands[1]));
        Assert.True(operands[0].MayThrow || operands[1].MayThrow);
        Assert.Equal(1, EvaluationOrder.LastSpilledOperand(operands));
    }

    // Only the order against a check is new: the operands read and write nothing the other one touches
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("WriteThenLength")]
    [InlineData("LengthThenWrite")]
    [InlineData("ElementWriteThenLength")]
    [InlineData("WriteThenDivision")]
    public void DataInterferesWith_CheckAndMemoryWrite_IsFalse(string method)
    {
        var operands = GetOperandEffects(method);

        Assert.False(operands[0].DataInterferesWith(operands[1]));
    }

    // A check next to a read leaves nothing to see after the exception; a write to a local ends with the thread
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ReadThenLength")]
    [InlineData("LengthThenRead")]
    [InlineData("LocalWriteThenLength")]
    [InlineData("WriteThenStackArrayLength")]
    [InlineData("WriteThenSpanLength")]
    [InlineData("WriteThenConstantDivision")]
    public void LastSpilledOperand_CheckWithoutMemoryWrite_IsNone(string method)
    {
        var operands = GetOperandEffects(method);

        Assert.False(operands[0].ThrowInterferesWith(operands[1]));
        Assert.Equal(-1, EvaluationOrder.LastSpilledOperand(operands));
    }

    // C# on the CLR checks an array element of every type, a struct included, when a simple assignment stores the
    // value, after the right side; a ref argument or a compound assignment checks it as it names it
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ElementTarget")]
    [InlineData("EnumElementTarget")]
    [InlineData("StructElementTarget")]
    public void OfAssignmentTarget_ArrayElement_LeavesTheElementCheckToTheStore(string method)
    {
        var (element, model) = GetResult(method);
        var (checkedIndex, checkedModel) = GetResult("ElementTargetWithCheckedIndex");

        Assert.True(ExpressionEffects.OfLocation(element, model).MayThrow);
        Assert.False(ExpressionEffects.OfAssignmentTarget(element, model).MayThrow);
        Assert.True(ExpressionEffects.OfAssignmentTarget(checkedIndex, checkedModel).MayThrow);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Constant")]
    [InlineData("ThreadIndexRead")]
    public void EffectsOf_ConstantsAndIntrinsics_AreEmpty(string method)
    {
        var effects = GetEffects(method);

        Assert.True(effects.IsEmpty);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EffectsOf_LocalRead_ReadsTheLocal()
    {
        var (effects, i) = GetEffects("LocalRead", "i");

        Assert.True(effects.Reads.Contains(i));
        Assert.False(effects.Reads.Memory);
        Assert.True(effects.Writes.IsEmpty);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EffectsOf_Increment_ReadsAndWritesTheLocal()
    {
        var (effects, i) = GetEffects("Increment", "i");

        Assert.True(effects.Reads.Contains(i));
        Assert.True(effects.Writes.Contains(i));
        Assert.False(effects.Writes.Memory);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EffectsOf_ElementRead_ReadsMemoryAndTheIndex()
    {
        var (effects, i) = GetEffects("ElementRead", "i");

        Assert.True(effects.Reads.Memory);
        Assert.True(effects.Reads.Contains(i));
        Assert.True(effects.Writes.IsEmpty);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EffectsOf_CallWithRefArgument_ReadsAndWritesTheArgument()
    {
        var (effects, i) = GetEffects("RefCall", "i");

        Assert.True(effects.Writes.Contains(i));
        Assert.False(effects.Writes.Memory);
        // The callee reads the variable through the reference while the call is evaluated
        Assert.True(effects.Reads.Contains(i));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("StaticFieldRead")]
    [InlineData("RefLocalRead")]
    [InlineData("SharedMemoryRead")]
    [InlineData("PointerRead")]
    public void EffectsOf_ReadThroughAnotherName_ReadsMemory(string method)
    {
        var effects = GetEffects(method);

        Assert.True(effects.Reads.Memory);
        Assert.False(effects.Writes.Memory);
    }

    // Roslyn reuses the syntax tree of an unchanged file in the next compilation, with new symbols
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LastSpilledOperand_AliasInSecondCompilationOfSameTree_IsStillFound()
    {
        var second = RoslynTestUtility.CreateCompilation("namespace OtherCases { class Unrelated { } }")
            .AddSyntaxTrees(s_compilation.SyntaxTrees);

        Assert.Equal(1, EvaluationOrder.LastSpilledOperand(GetOperandEffects("PointerAlias", s_compilation)));
        Assert.Equal(1, EvaluationOrder.LastSpilledOperand(GetOperandEffects("PointerAlias", second)));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("ExplicitConversion")]
    [InlineData("ImplicitConversion")]
    public void EffectsOf_UserDefinedConversion_WritesWhatTheOperatorWrites(string method)
    {
        var effects = GetEffects(method);

        Assert.True(effects.Writes.Memory);
        Assert.True(effects.Reads.Memory);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void EffectsOf_FieldOfThis_ReadsMemory()
    {
        var effects = GetEffects("ThisFieldRead");

        Assert.True(effects.Reads.Memory);
        Assert.True(effects.Writes.IsEmpty);
    }

    // The field keyword reads the backing field of the property, a field of this, as the implicit this field read of
    // ThisFieldRead does
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void EffectsOf_FieldKeyword_ReadsMemoryAsAFieldOfThis()
    {
        var effects = GetEffects("FieldRead");
        var thisField = GetEffects("ThisFieldRead");

        Assert.True(effects.Reads.Memory);
        Assert.True(effects.Writes.IsEmpty);
        Assert.Equal(thisField.Reads.Memory, effects.Reads.Memory);
        Assert.Equal(thisField.Writes.IsEmpty, effects.Writes.IsEmpty);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EffectsOf_FieldOfStructLocal_ReadsTheLocal()
    {
        var (effects, pair) = GetEffects("StructLocalFieldRead", "pair");

        Assert.True(effects.Reads.Contains(pair));
        Assert.False(effects.Reads.Memory);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EffectsOf_ComputedPropertyAsReceiver_CallsTheGetterOnItsStruct()
    {
        var (effects, s) = GetEffects("ReceiverMutatingGetterAlone", "s");

        Assert.True(effects.Reads.Contains(s));
        Assert.True(effects.Writes.Contains(s));
        Assert.True(effects.Reads.Memory);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EffectsOf_OutVarArgument_WritesTheDeclaredLocal()
    {
        var (effects, y) = GetEffects("OutVarArgument", "y");

        Assert.True(effects.Writes.Contains(y));
        Assert.False(effects.Writes.Memory);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void EffectsOf_WithExpression_WritesOnlyTheCopy()
    {
        var (effects, pair) = GetEffects("WithExpression", "pair");

        Assert.True(effects.Reads.Contains(pair));
        Assert.True(effects.Writes.IsEmpty);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("MemoryCall", true)]
    [InlineData("PureCall", false)]
    public void EffectsOf_Call_WritesWhatTheCalleeWrites(string method, bool writesMemory)
    {
        var effects = GetEffects(method);

        Assert.Equal(writesMemory, effects.Writes.Memory);
        Assert.True(effects.Reads.Memory);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void EffectsOf_RefParameterRead_ReadsMemory()
    {
        var effects = GetEffects("RefParameterRead");

        Assert.True(effects.Reads.Memory);
        Assert.True(effects.Writes.IsEmpty);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void EffectsOf_Conditional_IsUnionOfItsOperands()
    {
        var (effects, i) = GetEffects("ConditionalUnion", "i");

        Assert.True(effects.Writes.Contains(i));
        Assert.True(effects.Reads.Memory);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void EffectsOf_LocalFunctionCall_MayTouchEveryLocal()
    {
        var effects = GetEffects("LocalFunctionCall");

        Assert.True(effects.Writes.AllLocals);
        Assert.True(effects.Writes.Memory);
    }

    // The operands of the result expression in the C# order: the arguments of a call (the location of a ref argument),
    // or the two sides of a binary operator
    private static ExpressionEffects[] GetOperandEffects(string method) => GetOperandEffects(method, s_compilation);

    private static ExpressionEffects[] GetOperandEffects(string method, Compilation compilation)
    {
        var (expression, model) = GetResult(method, compilation);
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
                         .Where(n => n is VariableDeclaratorSyntax or ParameterSyntax or SingleVariableDesignationSyntax)
                         .Select(n => model.GetDeclaredSymbol(n))
                         .FirstOrDefault(s => s?.Name == local) ??
                     throw new InvalidOperationException($"'{method}' declares no local or parameter '{local}'.");
        return (ExpressionEffects.Of(expression, model), symbol);
    }

    private static (ExpressionSyntax Expression, SemanticModel Model) GetResult(string method) =>
        GetResult(method, s_compilation);

    private static (ExpressionSyntax Expression, SemanticModel Model) GetResult(string method, Compilation compilation)
    {
        var tree = compilation.SyntaxTrees.First(t => t.ToString().Contains("namespace OrderCases"));
        var model = compilation.GetSemanticModel(tree);
        // A case is a method, or a property whose getter holds the case (the field keyword is only in accessors)
        var declaration = tree.GetRoot()
            .DescendantNodes()
            .First(n => n switch
            {
                MethodDeclarationSyntax m => m.Identifier.ValueText == method,
                PropertyDeclarationSyntax p => p.Identifier.ValueText == method,
                _ => false
            });
        var result = declaration.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Last(v => v.Identifier.ValueText == "result");
        return (result.Initializer?.Value ?? throw new InvalidOperationException(method), model);
    }
}
