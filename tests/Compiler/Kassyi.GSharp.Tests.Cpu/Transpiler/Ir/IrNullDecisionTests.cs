#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies null-check, store, null-test, coalesce, member, and call decisions on <c>IrDecisionNullCases.Nulls</c>
/// (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class IrNullDecisionTests
{
    private const string TestCase = "Ir/IrDecisionNullCases.cs";
    private const string TypeName = "IrDecisionNullCases.Nulls";
    private const string CallCase = "Ir/IrDecisionNullCallCases.cu.cs";
    private const string OverlapCase = "Ir/IrDecisionBclOverlapCases.cs";
    private const string ShapeKernels = "Transpiler/Emission/ArrayNullCheckShapeKernels.cu.cs";
    private const string ShapeKernelsType = "Kassyi.GSharp.Tests.TestCases.Emission.ArrayNullCheckShapeKernels";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadOfAParameterArray_ChecksTheReceiver()
    {
        var decided = DecideMethod("ReadParameter");
        Assert.Empty(IrDecisionInvariants.Verify(decided.Body));
        var element = Assert.IsType<IrElementReference>(AssignedValue(decided, 0));

        var decision = element.Decided;
        Assert.Equal(IrElementCheck.NullChecked, decision.Check);
        Assert.NotNull(decision.Site);
        Assert.Equal(ArrayNullCheck.TrapKind, decision.Site.Value.TrapKind);
        Assert.Equal(element.Receiver.Source, decision.Site.Value.Source);
        Assert.Equal(ArrayNullCheck.SiteMessage, decision.Site.Value.Message);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadOfANotNullLocal_NeedsNoCheck()
    {
        var element = Assert.IsType<IrElementReference>(AssignedValue(DecideMethod("ReadNotNull"), 1));
        Assert.Equal(IrElementCheck.None, element.Decided.Check);
        Assert.Null(element.Decided.Site);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadOfAWrittenLocal_ChecksTheReceiver()
    {
        var element = Assert.IsType<IrElementReference>(AssignedValue(DecideMethod("ReadWrittenLocal"), 2));
        Assert.Equal(IrElementCheck.NullChecked, element.Decided.Check);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadOfAStackArray_NeedsNoCheck()
    {
        var element = Assert.IsType<IrElementReference>(AssignedValue(DecideMethod("ReadStackArray"), 1));
        Assert.Equal(IrElementCheck.None, element.Decided.Check);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadThroughAConditional_ChecksWhenABranchMayBeNull()
    {
        var element = Assert.IsType<IrElementReference>(AssignedValue(DecideMethod("ReadConditional"), 0));
        Assert.Equal(IrElementCheck.NullChecked, element.Decided.Check);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadOfAConditionalInitializer_ChecksTheReceiver()
    {
        var element = Assert.IsType<IrElementReference>(AssignedValue(DecideMethod("ReadInitializerConditional"), 1));
        Assert.Equal(IrElementCheck.NullChecked, element.Decided.Check);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadAtAULongIndex_NeedsTheOverflowCheck()
    {
        var element = Assert.IsType<IrElementReference>(AssignedValue(DecideMethod("ReadULongIndex"), 0));
        Assert.True(element.Decided.IndexNeedsOverflowCheck);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_StoreIntoAParameterArray_PlansTheCheckAtTheStore()
    {
        var statement = Assert.IsType<IrExpressionStatement>(IrTestCases.Statement(DecideMethod("StoreElement"), 0));
        var assignment = Assert.IsType<IrAssignment>(statement.Expression);

        var store = assignment.Decided.Store;
        Assert.NotNull(store);
        Assert.False(store.Value.BindsArrayByReference);
        Assert.NotNull(store.Value.Site);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_StoreIntoAStackArray_BindsTheArrayByReference()
    {
        var statement = Assert.IsType<IrExpressionStatement>(IrTestCases.Statement(DecideMethod("StoreStackArray"), 1));
        var assignment = Assert.IsType<IrAssignment>(statement.Expression);

        var store = assignment.Decided.Store;
        Assert.NotNull(store);
        Assert.True(store.Value.BindsArrayByReference);
        Assert.Null(store.Value.Site);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_LengthOfAParameterArray_ChecksTheReceiver()
    {
        var member = Assert.IsType<IrPropertyReference>(AssignedValue(DecideMethod("LengthOfParameter"), 0));
        Assert.Equal(IrMemberForm.ArrayMember, member.Decided.Form);
        Assert.NotNull(member.Decided.ReceiverSite);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_LengthOfAStackArray_IsTheConstantLength()
    {
        var member = Assert.IsType<IrPropertyReference>(AssignedValue(DecideMethod("LengthOfStackArray"), 1));
        Assert.Equal(IrMemberForm.StackArrayLength, member.Decided.Form);
        Assert.Equal(4, member.Decided.StackArrayLength);
        Assert.Null(member.Decided.ReceiverSite);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_NullTestOfAnArray_UsesTheDataPointerForm()
    {
        var test = Assert.IsType<IrNullTest>(AssignedValue(DecideMethod("NullTestArray"), 0));
        Assert.Equal(NullForm.ArrayView, test.Decided.Form);
        Assert.Equal(0, test.Decided.SkippedConversions);
        Assert.False(test.Decided.OperandIsStackArrayView);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_NullTestOfAStackArray_IsConstant()
    {
        var test = Assert.IsType<IrNullTest>(AssignedValue(DecideMethod("NullTestStackArray"), 1));
        Assert.Equal(NullForm.ArrayView, test.Decided.Form);
        Assert.True(test.Decided.OperandIsStackArrayView);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_NullTestThroughACast_SkipsTheCast()
    {
        var test = Assert.IsType<IrNullTest>(AssignedValue(DecideMethod("NullTestCast"), 0));
        Assert.Equal(1, test.Decided.SkippedConversions);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_CoalesceOfAnArray_UsesTheArrayForm()
    {
        var coalesce = Assert.IsType<IrCoalesce>(AssignedValue(DecideMethod("CoalesceArray"), 0));
        Assert.Equal(NullForm.ArrayView, coalesce.Decided.LeftForm);
        Assert.False(coalesce.Decided.CastsToResult);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_CoalesceOfANullable_CastsBothBranches()
    {
        var coalesce = Assert.IsType<IrCoalesce>(AssignedValue(DecideMethod("CoalesceNullable"), 0));
        Assert.Equal(NullForm.NullableValue, coalesce.Decided.LeftForm);
        Assert.True(coalesce.Decided.CastsToResult);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_CallOfAPlainMethod_IsNotAnIntrinsic()
    {
        var call = Assert.IsType<IrCall>(AssignedValue(DecideMethod("CallPlain"), 0));
        Assert.Null(call.Decided.Target);
        Assert.Equal(ArgumentCheckKind.None, call.Decided.Arguments[0].Check.Kind);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_PlainCallWithAByte_DoesNotRecordAConversion()
    {
        var call = Assert.IsType<IrCall>(AssignedValue(DecideMethod("CallPlainWithByte"), 0));
        Assert.Null(call.Decided.Arguments[0].ConvertedTo);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_MappedCallWithAnInt_RecordsTheConversion()
    {
        var call = Assert.IsType<IrCall>(AssignedValue(DecideMethod("CallMappedWithInt"), 0));
        Assert.Equal(IrCallClaim.None, call.Decided.Claim);
        Assert.Equal(CallTargetKind.DialectIntrinsic, call.Decided.Target);
        var convertedTo = call.Decided.Arguments[0].ConvertedTo;
        Assert.NotNull(convertedTo);
        Assert.Equal(SpecialType.System_Single, convertedTo.SpecialType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ArgumentCheck_UsesTheOldMessage()
    {
        var compilation = IrTestCases.Compile(ShapeKernels);
        var built = IrTestCases.Build(ShapeKernels, ShapeKernelsType, "BoundaryKernel");
        var decided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
        Assert.Empty(IrDecisionInvariants.Verify(decided.Body));

        var calls = IrNodeChildren.DescendantsAndSelf(decided.Body).OfType<IrCall>().ToArray();
        var readFirst = calls.First(static c => c.Method.Name == "ReadFirst");
        var arrayCheck = readFirst.Decided.Arguments[0].Check;
        Assert.Equal(ArgumentCheckKind.Array, arrayCheck.Kind);
        var arrayParam = readFirst.Arguments[0].Parameter;
        Assert.NotNull(arrayCheck.ArraySite);
        Assert.Equal(
            ArgumentNullCheck.FormatArgumentMessage(arrayParam, null, arrayParam.Type, null),
            arrayCheck.ArraySite.Value.Message);

        var readHolder = calls.First(static c => c.Method.Name == "ReadHolder");
        var structCheck = readHolder.Decided.Arguments[0].Check;
        Assert.Equal(ArgumentCheckKind.Struct, structCheck.Kind);
        var holderParam = readHolder.Arguments[0].Parameter;
        var fieldCheck = Assert.Single(structCheck.StructChecks);
        var expectedPath = ArgumentNullCheck.FormatFieldPath(holderParam, fieldCheck.Path);
        Assert.Equal(expectedPath, fieldCheck.FieldPath);
        Assert.Equal(
            ArgumentNullCheck.FormatArgumentMessage(holderParam, expectedPath, fieldCheck.Path[^1].Type, null),
            fieldCheck.Site.Message);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_DialectIntrinsicProperty_IsAnIntrinsicForm()
    {
        var member = Assert.IsType<IrPropertyReference>(AssignedValue(DecideMethod("CallIntrinsic"), 0));
        Assert.Equal(IrMemberForm.DialectIntrinsic, member.Decided.Form);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_UndecidedCall_ReadsAsNull()
    {
        var decided = DecideMethod("CallPlain");
        var call = Assert.IsType<IrCall>(AssignedValue(decided, 0));
        Assert.Null(call.Decided.Target);
        Assert.Empty(IrDecisionInvariants.Verify(decided.Body));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_MathAbsOfInt_IsClaimedByAnotherRule()
    {
        var call = Assert.IsType<IrCall>(AssignedValue(DecideMethod("AbsOfInt"), 0));
        Assert.Equal(IrCallClaim.OtherRule, call.Decided.Claim);
        Assert.Null(call.Decided.Target);
        Assert.Null(call.Decided.Arguments[0].ConvertedTo);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_BclMappedCall_ClaimsTheBclMap()
    {
        var popCount = Assert.IsType<IrCall>(AssignedValue(DecideMethod("BclPopCountUInt"), 0));
        Assert.Equal(IrCallClaim.BclIntrinsicMap, popCount.Decided.Claim);
        Assert.Equal(CallTargetKind.BclIntrinsicMap, popCount.Decided.Target);

        var compilation = IrTestCases.Compile(OverlapCase);
        var built = IrTestCases.Build(OverlapCase, "Overlap", "Call");
        var decided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
        Assert.Empty(IrDecisionInvariants.Verify(decided.Body));
        var overlapCall = Assert.IsType<IrCall>(AssignedValue(decided, 0));
        Assert.Equal(IrCallClaim.BclIntrinsicMap, overlapCall.Decided.Claim);
        Assert.Equal(CallTargetKind.BclIntrinsicMap, overlapCall.Decided.Target);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_BclMappedCallWithACastedFirstArgument_SkipsTheFirstConversion()
    {
        var call = Assert.IsType<IrCall>(AssignedValue(DecideMethod("BclPopCountByte"), 0));
        Assert.Equal(IrCallClaim.BclIntrinsicMap, call.Decided.Claim);
        Assert.Equal(CallTargetKind.BclIntrinsicMap, call.Decided.Target);
        Assert.Null(call.Decided.Arguments[0].ConvertedTo);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_NullTestOfAString_HasNoNullForm()
    {
        var decided = DecideMethod("NullTestString");
        var test = Assert.IsType<IrNullTest>(AssignedValue(decided, 0));
        Assert.Equal(NullForm.None, test.Decided.Form);
        Assert.Empty(IrDecisionInvariants.Verify(decided.Body));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadOfANullForgivenArray_UsesOuterSpellingForSite()
    {
        var compilation = IrTestCases.Compile(TestCase);
        var built = IrTestCases.Build(TestCase, TypeName, "ReadNullForgiven");
        var decided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
        Assert.Empty(IrDecisionInvariants.Verify(decided.Body));

        var element = Assert.IsType<IrElementReference>(AssignedValue(decided, 0));
        var decision = element.Decided;
        Assert.Equal(IrElementCheck.NullChecked, decision.Check);
        Assert.NotNull(decision.Site);
        var siteSource = decision.Site.Value.Source;
        var location = siteSource.ToLocation(compilation);
        Assert.Equal("a!", location.SourceTree?.GetText().ToString(location.SourceSpan));
        Assert.Equal(element.Receiver.Source.Span.Start, siteSource.Span.Start);
        Assert.Equal(element.Receiver.Source.Span.Length + 1, siteSource.Span.Length);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadOfAParenthesizedCoalesce_UsesOuterSpellingForSite()
    {
        var compilation = IrTestCases.Compile(TestCase);
        var built = IrTestCases.Build(TestCase, TypeName, "ReadParenthesizedCoalesce");
        var decided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
        Assert.Empty(IrDecisionInvariants.Verify(decided.Body));

        var element = Assert.IsType<IrElementReference>(AssignedValue(decided, 0));
        var decision = element.Decided;
        Assert.Equal(IrElementCheck.NullChecked, decision.Check);
        Assert.NotNull(decision.Site);
        var siteSource = decision.Site.Value.Source;
        var location = siteSource.ToLocation(compilation);
        Assert.Equal("(pick ?? b)", location.SourceTree?.GetText().ToString(location.SourceSpan));
        Assert.Equal(element.Receiver.Source.Span.Start - 1, siteSource.Span.Start);
        Assert.Equal(element.Receiver.Source.Span.Length + 2, siteSource.Span.Length);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_LengthOfParenthesizedNullForgiven_UsesOuterSpellingForReceiverSite()
    {
        var compilation = IrTestCases.Compile(TestCase);
        var built = IrTestCases.Build(TestCase, TypeName, "LengthOfParenthesizedNullForgiven");
        var decided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
        Assert.Empty(IrDecisionInvariants.Verify(decided.Body));

        var member = Assert.IsType<IrPropertyReference>(AssignedValue(decided, 0));
        Assert.Equal(IrMemberForm.ArrayMember, member.Decided.Form);
        Assert.NotNull(member.Decided.ReceiverSite);
        var location = member.Decided.ReceiverSite.Value.Source.ToLocation(compilation);
        Assert.Equal("(a!)", location.SourceTree?.GetText().ToString(location.SourceSpan));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ArgumentCheckWithOuterSpelling_UsesOuterSpellingForArraySite()
    {
        var compilation = IrTestCases.Compile(CallCase);
        var built = IrTestCases.Build(CallCase, "CallSites", "Call");
        var decided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
        Assert.Empty(IrDecisionInvariants.Verify(decided.Body));

        string[] expected = ["b!", "(b!)", "null!"];
        for (var k = 0; k < expected.Length; k++)
        {
            var call = Assert.IsType<IrCall>(AssignedValue(decided, k));
            var site = call.Decided.Arguments[0].Check.ArraySite;
            Assert.NotNull(site);
            var location = site.Value.Source.ToLocation(compilation);
            Assert.Equal(expected[k], location.SourceTree?.GetText().ToString(location.SourceSpan));
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_DefaultLiteralArgumentWithOuterSpelling_UsesOuterSpellingForSite()
    {
        var compilation = IrTestCases.Compile(CallCase);
        var built = IrTestCases.Build(CallCase, "DefaultCallSites", "Call");
        var decided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
        Assert.Empty(IrDecisionInvariants.Verify(decided.Body));

        string[] expected = ["default!", "(default!)", "(default)", "default!", "default!"];
        for (var k = 0; k < expected.Length; k++)
        {
            var call = Assert.IsType<IrCall>(AssignedValue(decided, k));
            var check = call.Decided.Arguments[0].Check;
            var site = k == 4 ? Assert.Single(check.StructChecks).Site : check.ArraySite;
            Assert.NotNull(site);
            var location = site.Value.Source.ToLocation(compilation);
            Assert.Equal(expected[k], location.SourceTree?.GetText().ToString(location.SourceSpan));
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_RemovedDeviceFunctionCall_SkipsArgumentChecksAndConversions()
    {
        var compilation = IrTestCases.Compile(CallCase);
        var built = IrTestCases.Build(CallCase, "RemovedDeviceCalls", "Call");
        var decided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
        Assert.Empty(IrDecisionInvariants.Verify(decided.Body));

        for (var k = 0; k < 2; k++)
        {
            var stmt = Assert.IsType<IrExpressionStatement>(IrTestCases.Statement(decided, k));
            var call = Assert.IsType<IrCall>(stmt.Expression);
            Assert.Equal(CallTargetKind.Removed, call.Decided.Target);
            Assert.Equal(IrCallClaim.None, call.Decided.Claim);
            Assert.Equal(ArgumentCheckKind.None, call.Decided.Arguments[0].Check.Kind);
            Assert.Null(call.Decided.Arguments[0].Check.ArraySite);
            Assert.Null(call.Decided.Arguments[0].ConvertedTo);
            Assert.Null(call.Decided.Arguments[1].ConvertedTo);
        }
    }

    // C# converts a nuint index to ulong, then checks it with conv.ovf.i.un
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadAtANUIntIndex_NeedsTheOverflowCheck()
    {
        var element = Assert.IsType<IrElementReference>(AssignedValue(DecideMethod("ReadNUIntIndex"), 0));
        Assert.True(element.Decided.IndexNeedsOverflowCheck);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadAtANIntIndex_DoesNotNeedTheOverflowCheck()
    {
        var element = Assert.IsType<IrElementReference>(AssignedValue(DecideMethod("ReadNIntIndex"), 0));
        Assert.False(element.Decided.IndexNeedsOverflowCheck);
    }

    private static IrMethod DecideMethod(string name)
    {
        var compilation = IrTestCases.Compile(TestCase);
        var built = IrTestCases.Build(TestCase, TypeName, name);
        var decided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
        Assert.Empty(IrDecisionInvariants.Verify(decided.Body));
        return decided;
    }

    private static IrExpression AssignedValue(IrMethod method, int statementIndex)
    {
        var statement = Assert.IsType<IrExpressionStatement>(IrTestCases.Statement(method, statementIndex));
        var assignment = Assert.IsType<IrAssignment>(statement.Expression);
        return assignment.Value;
    }
}
