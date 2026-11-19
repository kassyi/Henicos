#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies the getter form, the receiver copy and the setter call decisions on <c>IrDecisionAccessorCases</c>
/// (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class IrAccessorDecisionTests
{
    private const string TestCase = "Ir/IrDecisionAccessorCases.cs";
    private const string TypeName = "IrDecisionAccessorCases.Accessors";
    private const string CtorHolderName = "IrDecisionAccessorCases.CtorHolder";
    private const string CounterName = "IrDecisionAccessorCases.Counter";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadOfAnAccessorProperty_IsAGetter()
    {
        var member = ReadMember("ReadGetter", 0);
        Assert.Equal(IrMemberForm.Getter, member.Decided.Form);
        Assert.Null(member.Decided.CopyType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadOfAnAutoProperty_IsOther()
    {
        var member = ReadMember("ReadAutoProperty", 0);
        Assert.Equal(IrMemberForm.Other, member.Decided.Form);
        Assert.Null(member.Decided.CopyType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_NonConstGetterOfAnInParameter_CopiesTheStruct()
    {
        var member = ReadMember("ReadBumpOfIn", 0);
        Assert.Equal(IrMemberForm.Getter, member.Decided.Form);
        AssertCopiesCounter(member);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ConstGetterOfAnInParameter_DoesNotCopy()
    {
        var member = ReadMember("ReadTwiceOfIn", 0);
        Assert.Equal(IrMemberForm.Getter, member.Decided.Form);
        Assert.Null(member.Decided.CopyType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_GetterOfAValueParameter_DoesNotCopy()
    {
        var member = ReadMember("ReadBumpOfValue", 0);
        Assert.Equal(IrMemberForm.Getter, member.Decided.Form);
        Assert.Null(member.Decided.CopyType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadonlyFieldOfAValueParameter_Copies()
    {
        AssertCopiesCounter(ReadMember("ReadBumpOfReadonlyFieldOfValue", 0));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_LooseFieldOfAnInParameter_Copies()
    {
        AssertCopiesCounter(ReadMember("ReadBumpOfLooseFieldOfIn", 0));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_LooseFieldOfAValueParameter_DoesNotCopy()
    {
        var member = ReadMember("ReadBumpOfLooseFieldOfValue", 0);
        Assert.Equal(IrMemberForm.Getter, member.Decided.Form);
        Assert.Null(member.Decided.CopyType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_RefReadonlyLocal_Copies()
    {
        AssertCopiesCounter(ReadMember("ReadBumpOfRefReadonlyLocal", 1));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_RefReadonlyReturn_Copies()
    {
        var member = ReadMember("ReadBumpOfRefReadonlyReturn", 0);
        Assert.IsType<IrCall>(member.Receiver);
        AssertCopiesCounter(member);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_RefReadonlyPropertyReturn_Copies()
    {
        var member = ReadMember("ReadBumpOfRefReadonlyProperty", 0);
        Assert.IsType<IrPropertyReference>(member.Receiver);
        AssertCopiesCounter(member);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadonlySetterOfAnInParameter_TargetHasNoCopy()
    {
        var assignment = StatementAssignment(DecideMethod("StoreReadonlySetterOfIn"), 0);
        var target = Assert.IsType<IrPropertyReference>(assignment.Target);
        Assert.Equal(IrMemberForm.Getter, target.Decided.Form);
        Assert.Null(target.Decided.CopyType);
        Assert.Equal(IrAccessorStore.CallsSetter, assignment.Decided.Accessor);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ObjectInitializerSetter_CallsTheSetter()
    {
        var declaration = Assert.IsType<IrLocalDeclaration>(
            IrTestCases.Statement(DecideMethod("InitializerSetter"), 0));
        var creation = Assert.IsType<IrStructCreation>(declaration.Initializer);
        var assignment = Assert.Single(creation.Initializers);
        var target = Assert.IsType<IrPropertyReference>(assignment.Target);

        Assert.IsType<IrInitializedObject>(target.Receiver);
        Assert.Equal(IrMemberForm.Getter, target.Decided.Form);
        Assert.Null(target.Decided.CopyType);
        Assert.Equal(IrAccessorStore.CallsSetter, assignment.Decided.Accessor);
        Assert.Null(assignment.Decided.Store);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_SetterStatement_CallsTheSetter()
    {
        var assignment = StatementAssignment(DecideMethod("StoreSetter"), 0);
        Assert.Equal(IrAccessorStore.CallsSetter, assignment.Decided.Accessor);
        Assert.Null(assignment.Decided.Store);
        var target = Assert.IsType<IrPropertyReference>(assignment.Target);
        Assert.Equal(IrMemberForm.Getter, target.Decided.Form);
        Assert.Null(target.Decided.CopyType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_AutoPropertyStore_HasNoAccessor()
    {
        var assignment = StatementAssignment(DecideMethod("StoreAuto"), 0);
        Assert.Equal(IrAccessorStore.None, assignment.Decided.Accessor);
        Assert.Equal(IrMemberForm.Other, Assert.IsType<IrPropertyReference>(assignment.Target).Decided.Form);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_SetterWithValueUse_StillCallsTheSetter()
    {
        var outer = StatementAssignment(DecideMethod("StoreSetterWithValueUse"), 0);
        var inner = Assert.IsType<IrAssignment>(outer.Value);
        Assert.Equal(IrAccessorStore.CallsSetter, inner.Decided.Accessor);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_CompoundOfAnAccessorTarget_MarksTheTargetAsGetter()
    {
        var statement = Assert.IsType<IrExpressionStatement>(
            IrTestCases.Statement(DecideMethod("CompoundOfSetter"), 0));
        var compound = Assert.IsType<IrCompoundAssignment>(statement.Expression);
        var target = Assert.IsType<IrPropertyReference>(compound.Target);
        Assert.Equal(IrMemberForm.Getter, target.Decided.Form);
        Assert.Null(target.Decided.CopyType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ElementStore_HasNoAccessor()
    {
        var assignment = StatementAssignment(DecideMethod("StoreElement"), 0);
        Assert.Equal(IrAccessorStore.None, assignment.Decided.Accessor);
        Assert.NotNull(assignment.Decided.Store);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Decide_IndexerProperty_IsNotAGetter()
    {
        var member = ReadMember("ReadIndexer", 0);
        Assert.False(member.Arguments.IsEmpty);
        Assert.Equal(IrMemberForm.Other, member.Decided.Form);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Decide_StaticProperty_IsNotAGetter()
    {
        var member = ReadMember("ReadStaticProperty", 0);
        Assert.Null(member.Receiver);
        Assert.Equal(IrMemberForm.Other, member.Decided.Form);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Decide_ReadonlyFieldOfThisInAConstructor_DoesNotCopy()
    {
        var member = ReadMember(CtorHolderName, ".ctor", 1);
        Assert.Equal(IrMemberForm.Getter, member.Decided.Form);
        Assert.Null(member.Decided.CopyType);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Decide_ReadonlyFieldOfAnotherInstanceInAConstructor_Copies()
    {
        AssertCopiesCounter(ReadMember(CtorHolderName, ".ctor", 2));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Decide_ReadonlyFieldOfThisInAMethod_Copies()
    {
        AssertCopiesCounter(ReadMember(CtorHolderName, "ReadInMethod", 0));
    }

    private static void AssertCopiesCounter(IrPropertyReference member)
    {
        var compilation = IrTestCases.Compile(TestCase);
        Assert.Equal(IrMemberForm.Getter, member.Decided.Form);
        Assert.Equal(compilation.GetTypeByMetadataName(CounterName), member.Decided.CopyType,
            SymbolEqualityComparer.Default);
    }

    private static IrPropertyReference ReadMember(string name, int statementIndex) =>
        ReadMember(TypeName, name, statementIndex);

    private static IrPropertyReference ReadMember(string type, string name, int statementIndex)
    {
        var assignment = StatementAssignment(DecideMethod(type, name), statementIndex);
        return Assert.IsType<IrPropertyReference>(assignment.Value);
    }

    private static IrAssignment StatementAssignment(IrMethod method, int statementIndex)
    {
        var statement = Assert.IsType<IrExpressionStatement>(IrTestCases.Statement(method, statementIndex));
        return Assert.IsType<IrAssignment>(statement.Expression);
    }

    private static IrMethod DecideMethod(string name) => DecideMethod(TypeName, name);

    private static IrMethod DecideMethod(string type, string name)
    {
        var compilation = IrTestCases.Compile(TestCase);
        var built = IrTestCases.Build(TestCase, type, name);
        var context = IrDecisionTestContext.Create(compilation, built);
        var decided = IrDecisions.Decide(built, context);
        Assert.Empty(IrDecisionInvariants.Verify(decided.Body, context.TemporaryInitializers));
        return decided;
    }
}
