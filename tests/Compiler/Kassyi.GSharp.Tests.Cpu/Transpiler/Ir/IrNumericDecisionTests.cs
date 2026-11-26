#nullable enable
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Helpers;
using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies numeric decisions (<see cref="IrDecisions"/>, <see cref="IrDecisionInvariants"/>) and temporary
/// resolution (<see cref="IrTemporaryInitializers"/>) on <c>IrDecisionCases.Numeric</c> (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class IrNumericDecisionTests
{
    private const string TestCase = "Ir/IrDecisionCases.cs";
    private const string TypeName = "IrDecisionCases.Numeric";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_FoldedShift_FoldsToTheMaskedValue()
    {
        var decided = DecideMethod("FoldedShift");
        Assert.Empty(IrDecisionInvariants.Verify(decided.Body));
        var binary = Assert.IsType<IrBinary>(AssignedValue(decided));

        var decision = binary.Decided;
        Assert.NotNull(decision.Folded);
        Assert.Equal(SpecialType.System_Int32, decision.Folded.Value.Type.SpecialType);
        Assert.Equal(6, decision.Folded.Value.Value);
        Assert.Null(decision.Helper);
        Assert.Null(decision.ManagedDivision);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_DivideByAnyInt_TakesTheHelperAndTheSite()
    {
        var decided = DecideMethod("DivideByAny");
        var binary = Assert.IsType<IrBinary>(AssignedValue(decided));

        var decision = binary.Decided;
        Assert.Equal(GSharpInternalNames.IntegerSemantics.DivideInt32, decision.Helper);
        Assert.NotNull(decision.Site);
        Assert.Equal(DivisorCheck.TrapKind, decision.Site.Value.TrapKind);
        Assert.Equal(DivisorCheck.Message, decision.Site.Value.Message);
        Assert.Equal(binary.Source, decision.Site.Value.Source);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_DivideByAnyInt_PlansTheManagedDivision()
    {
        var divide = Assert.IsType<IrBinary>(AssignedValue(DecideMethod("DivideByAny")));
        var shift = Assert.IsType<IrBinary>(AssignedValue(DecideMethod("FoldedShift")));

        var plan = divide.Decided.ManagedDivision;
        Assert.NotNull(plan);
        Assert.Equal(GSharpInternalNames.IntegerSemantics.DivideInt32, plan.Value.Helper);
        Assert.Equal(ManagedDivisionForm.Call, plan.Value.Form);
        Assert.Null(shift.Decided.ManagedDivision);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_DivideByPositiveDivisor_KeepsTheOperator()
    {
        var decided = DecideMethod("DivideByPositive");
        var binary = Assert.IsType<IrBinary>(AssignedValue(decided));

        var decision = binary.Decided;
        Assert.Null(decision.Helper);
        Assert.Null(decision.Site);
        Assert.Null(decision.ManagedDivision);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_FloatRemainder_UsesFmodf()
    {
        var decided = DecideMethod("RemainderFloat");
        var binary = Assert.IsType<IrBinary>(AssignedValue(decided));

        var decision = binary.Decided;
        Assert.Equal("fmodf", decision.FloatingRemainder);
        Assert.Null(decision.Helper);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ShiftOfUInt_MasksTheCount()
    {
        var decided = DecideMethod("ShiftUInt");
        var binary = Assert.IsType<IrBinary>(AssignedValue(decided));

        Assert.Equal(31, binary.Decided.ShiftMask);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_UIntPlusInt_PromotesToLong()
    {
        var decided = DecideMethod("MixedUnsigned");
        var binary = Assert.IsType<IrBinary>(AssignedValue(decided));

        var decision = binary.Decided;
        Assert.NotNull(decision.PromotedType);
        Assert.Equal(SpecialType.System_Int64, decision.PromotedType.SpecialType);
        Assert.True(decision.ConvertsLeft);
        Assert.True(decision.ConvertsRight);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_NegateUnsigned_PromotesTheOperand()
    {
        var decided = DecideMethod("NegateUnsigned");
        var unary = Assert.IsType<IrUnary>(AssignedValue(decided));

        var decision = unary.Decided;
        Assert.NotNull(decision.PromotedType);
        Assert.Equal(SpecialType.System_Int64, decision.PromotedType.SpecialType);
        Assert.True(decision.ConvertsOperand);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_NarrowEnumOr_CastsBackToTheUnderlyingType()
    {
        var decided = DecideMethod("NarrowEnumOr");
        var binary = Assert.IsType<IrBinary>(AssignedValue(decided));

        Assert.NotNull(binary.Decided.NarrowEnumType);
        Assert.Equal(SpecialType.System_Byte, binary.Decided.NarrowEnumType.SpecialType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_IncrementOfShort_TakesTheStepHelpers()
    {
        var decided = DecideMethod("IncrementShort");
        var step = Assert.IsType<IrIncrement>(AssignedValue(decided));

        var decision = step.Decided;
        Assert.Equal(GSharpInternalNames.IntegerSemantics.AddInt32, decision.Helper);
        Assert.Equal(GSharpInternalNames.IntegerSemantics.SubtractInt32, decision.UndoHelper);
        Assert.NotNull(decision.Narrowing);
        Assert.Equal(SpecialType.System_Int16, decision.Narrowing.SpecialType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_CompoundDivide_TakesTheHelperAndTheSite()
    {
        var decided = DecideMethod("CompoundDivide");
        var statement = Assert.IsType<IrExpressionStatement>(IrTestCases.Statement(decided, 0));
        var compound = Assert.IsType<IrCompoundAssignment>(statement.Expression);

        var decision = compound.Decided;
        Assert.Equal(GSharpInternalNames.IntegerSemantics.DivideInt32, decision.Helper);
        Assert.NotNull(decision.Site);
        Assert.Equal(DivisorCheck.TrapKind, decision.Site.Value.TrapKind);
        Assert.Equal(SpecialType.System_Int32, decision.OperatorType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_CompoundShiftOfULong_MasksTheCount()
    {
        var decided = DecideMethod("CompoundShiftULong");
        var statement = Assert.IsType<IrExpressionStatement>(IrTestCases.Statement(decided, 0));
        var compound = Assert.IsType<IrCompoundAssignment>(statement.Expression);

        var decision = compound.Decided;
        Assert.Equal(63, decision.ShiftMask);
        Assert.Equal(SpecialType.System_UInt64, decision.OperatorType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_FloatToInt_UsesTheConversionHelper()
    {
        var decided = DecideMethod("FloatToInt");
        var conversion = Assert.IsType<IrConversion>(AssignedValue(decided));

        var decision = conversion.Decided;
        Assert.Equal(IrConversionForm.FloatToInteger, decision.Form);
        Assert.Equal(GSharpInternalNames.IntegerSemantics.ConvertToInt32, decision.Helper);
        Assert.False(decision.NeedsCast);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_FloatToByte_CastsTheHelperResult()
    {
        var decided = DecideMethod("FloatToByte");
        var conversion = Assert.IsType<IrConversion>(AssignedValue(decided));

        var decision = conversion.Decided;
        Assert.Equal(IrConversionForm.FloatToInteger, decision.Form);
        Assert.Equal(GSharpInternalNames.IntegerSemantics.ConvertToInt32, decision.Helper);
        Assert.True(decision.NeedsCast);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_FoldedCast_FoldsTheConstant()
    {
        var decided = DecideMethod("FoldedCast");
        var conversion = Assert.IsType<IrConversion>(AssignedValue(decided));

        var decision = conversion.Decided;
        Assert.Equal(IrConversionForm.Folded, decision.Form);
        Assert.NotNull(decision.Folded);
        Assert.Equal(SpecialType.System_Int32, decision.Folded.Value.Type.SpecialType);
        Assert.Equal(6, decision.Folded.Value.Value);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ConstNoConversion_KeepsTheOwnType()
    {
        var decided = DecideMethod("ConstNoConversion");
        var constant = Assert.IsType<IrConstant>(AssignedValue(decided));

        Assert.Equal(SpecialType.System_Byte, constant.Decided.TypeAtUse.SpecialType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ConstUnderImplicitConversion_TakesTheTargetType()
    {
        var decided = DecideMethod("ConstUnderConversion");
        var call = Assert.IsType<IrCall>(AssignedValue(decided));
        var conversion = Assert.IsType<IrConversion>(call.Arguments[0].Value);
        var constant = Assert.IsType<IrConstant>(conversion.Operand);

        Assert.Equal(IrConversionForm.Transparent, conversion.Decided.Form);
        Assert.Equal(SpecialType.System_Int32, constant.Decided.TypeAtUse.SpecialType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_NegatedFloatLiteral_FoldsWithItsOwnType()
    {
        var decided = DecideMethod("NegatedFloatLiteral");
        var unary = Assert.IsType<IrUnary>(AssignedValue(decided));

        var decision = unary.Decided;
        Assert.NotNull(decision.Folded);
        Assert.Equal(SpecialType.System_Single, decision.Folded.Value.Type.SpecialType);
        Assert.Equal(-1.5f, decision.Folded.Value.Value);
        Assert.Null(decision.Helper);
        Assert.Null(decision.PromotedType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_NegatedIntMin_FoldsToTheMinimumValue()
    {
        var decided = DecideMethod("NegatedIntMin");
        // C# binds -2147483648 as one literal, not as a negation of 2147483648
        var constant = Assert.IsType<IrConstant>(AssignedValue(decided));
        Assert.Equal(int.MinValue, constant.Value);
        Assert.Equal(SpecialType.System_Int32, constant.Decided.TypeAtUse.SpecialType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ConditionalOfUIntAndInt_PromotesToLong()
    {
        var decided = DecideMethod("ConditionalMixed");
        Assert.Empty(IrDecisionInvariants.Verify(decided.Body));
        var conversion = Assert.IsType<IrConversion>(AssignedValue(decided));
        Assert.True(conversion.IsImplicit);
        Assert.Equal(IrConversionForm.Transparent, conversion.Decided.Form);
        var conditional = Assert.IsType<IrConditional>(conversion.Operand);

        var decision = conditional.Decided;
        Assert.NotNull(decision.PromotedType);
        Assert.Equal(SpecialType.System_Int64, decision.PromotedType.SpecialType);
        Assert.True(decision.ConvertsWhenTrue);
        Assert.True(decision.ConvertsWhenFalse);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ConversionToObject_IsUnsupported()
    {
        var decided = DecideMethod("ConversionToObject");
        var conversion = Assert.IsType<IrConversion>(AssignedValue(decided));

        Assert.Equal(IrConversionForm.Unsupported, conversion.Decided.Form);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Decide_AnyNode_BeforeThePassReadsAreErrors()
    {
        var method = IrTestCases.Build(TestCase, TypeName, "MixedUnsigned");
        var binary = Assert.IsType<IrBinary>(AssignedValue(method));

        Assert.Throws<InvalidOperationException>(() => binary.Decided);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Resolve_TemporaryChain_ReachesTheOriginalOperand()
    {
        var compilation = IrTestCases.Compile(TestCase);
        var intType = compilation.GetSpecialType(SpecialType.System_Int32);
        var source = IrTestCases.Build(TestCase, TypeName, "DivideByAny").Body.Source;

        var xVar = IrVariable.CreateTemporary(intType, RefKind.None, "x", source);
        var t0 = IrVariable.CreateTemporary(intType, RefKind.None, "t", source);
        var t1 = IrVariable.CreateTemporary(intType, RefKind.None, "t", source);
        var cond = IrVariable.CreateTemporary(intType, RefKind.None, "Condition", source);

        var xRef = new IrVariableReference(xVar, false, IrCategory.Value, default, source);
        var t0Ref = new IrVariableReference(t0, false, IrCategory.Value, default, source);
        var t1Ref = new IrVariableReference(t1, false, IrCategory.Value, default, source);
        var condRef = new IrVariableReference(cond, false, IrCategory.Value, default, source);
        var declRef = new IrVariableReference(t0, true, IrCategory.Location, default, source);

        var block = new IrBlock(
            [
                new IrLocalDeclaration(t0, xRef, source),
                new IrLocalDeclaration(t1, t0Ref, source),
                new IrLocalDeclaration(cond, new IrDefaultValue(intType, IrCategory.Value, source), source)
            ],
            [xVar, t0, t1, cond],
            source);

        var temporaries = IrTemporaryInitializers.Create(block);
        Assert.Same(xRef, temporaries.Resolve(t1Ref));
        Assert.Same(condRef, temporaries.Resolve(condRef));
        Assert.Same(declRef, temporaries.Resolve(declRef));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Verify_WholeNumericCases_HasNoViolations()
    {
        var compilation = IrTestCases.Compile(TestCase);
        foreach (var built in IrTestCases.BuildAll(TestCase, TypeName))
        {
            var decided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
            Assert.Empty(IrDecisionInvariants.Verify(decided.Body));
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Verify_MissingDecision_ReportsI28()
    {
        var built = IrTestCases.Build(TestCase, TypeName, "MixedUnsigned");
        var violations = IrDecisionInvariants.Verify(built.Body);

        Assert.Contains(violations, static v => v.StartsWith("I28 ", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Verify_WrongDecisionKind_ReportsI29()
    {
        var compilation = IrTestCases.Compile(TestCase);
        var decided = DecideMethod("MixedUnsigned");
        var binary = Assert.IsType<IrBinary>(AssignedValue(decided));
        var wrong = IrExpression.WithDecision(binary,
            new IrConstantDecision(compilation.GetSpecialType(SpecialType.System_Int32)));

        var violations = IrDecisionInvariants.Verify(wrong);
        Assert.Contains(violations, static v => v.StartsWith("I29 ", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Verify_BrokenFieldRule_ReportsI30()
    {
        var compilation = IrTestCases.Compile(TestCase);
        var decided = DecideMethod("FoldedShift");
        var binary = Assert.IsType<IrBinary>(AssignedValue(decided));
        var broken = binary.WithDecision(new IrBinaryDecision(
            new IrFoldedConstant(compilation.GetSpecialType(SpecialType.System_Int32), 6),
            null,
            GSharpInternalNames.IntegerSemantics.AddInt32,
            null,
            0,
            null,
            null,
            false,
            false,
            null));

        var violations = IrDecisionInvariants.Verify(broken);
        Assert.Contains(violations, static v => v.StartsWith("I30 ", StringComparison.Ordinal));
    }

    private static IrMethod DecideMethod(string name)
    {
        var compilation = IrTestCases.Compile(TestCase);
        var built = IrTestCases.Build(TestCase, TypeName, name);
        return IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
    }

    private static IrExpression AssignedValue(IrMethod method)
    {
        var statement = Assert.IsType<IrExpressionStatement>(IrTestCases.Statement(method, 0));
        var assignment = Assert.IsType<IrAssignment>(statement.Expression);
        return assignment.Value;
    }
}
