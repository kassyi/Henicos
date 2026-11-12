#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies the node the IR builder makes for each kind of expression (#1561), and the expressions it keeps as opaque
/// leaves with the reason, in the order Invalid, unsupported operation, typeless, unsupported shape.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrBuilderExpressionTests
{
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Literal", "7")]
    [InlineData("ConstField", "K=3")]
    [InlineData("ConstLocal", "Local=9")]
    [InlineData("ConstantExpression", "(1 + 2)")]
    [InlineData("Logical", "(a && (!b))")]
    [InlineData("NullEquals", "(data is null)")]
    [InlineData("NullNotEquals", "(data is not null)")]
    [InlineData("IsNull", "(data is null)")]
    [InlineData("IsNotNull", "(data is not null)")]
    [InlineData("LiftedNullTest", "(n is null)")]
    [InlineData("UserEquals", "(a ==@op_Equality b)")]
    [InlineData("UserEqualsNull", "(node ==@op_Equality null)")]
    [InlineData("UserOperator", "(a +@op_Addition b)")]
    [InlineData("UserConversion", "implicit:UserDefined<int>(a)")]
    [InlineData("Coalesce", "(a ?? b)")]
    [InlineData("NullAssignment", "null")]
    [InlineData("ExplicitNumeric", "(int)x")]
    [InlineData("ImplicitNumeric", "implicit<long>(x)")]
    [InlineData("Identity", "(int)x")]
    [InlineData("ImplicitConstant", "implicit:ImplicitConstant<byte>(1)")]
    [InlineData("EnumConversion", "(int:Enumeration)color")]
    [InlineData("Prefix", "(++ref x)")]
    [InlineData("Postfix", "(ref x--)")]
    [InlineData("CheckedAdd", "(checked a + b)")]
    [InlineData("StructInitializer", "new Cell() {(ref (ref <init>).F = j), (ref (ref <init>).Auto = 2)}")]
    [InlineData("DefaultStruct", "default(Cell)")]
    [InlineData("ArrayCreation", "new int[2] {n, (n + 1)}")]
    public void Expression_BuildsItsNode(string method, string expected) =>
        Assert.Equal(expected, IrTestCases.PrintCaseResult(method));

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ConstantExpression_KeepsItsStructureAndValue()
    {
        var binary = Assert.IsType<IrBinary>(IrTestCases.CaseResult("ConstantExpression"));

        Assert.Equal(BinaryOperatorKind.Add, binary.Operator.Kind);
        Assert.Equal(3, binary.ConstantValue.Value);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CompoundAssignment_KeepsItsConversions()
    {
        var statement = IrTestCases.Statement(IrTestCases.BuildCase("Compound"));
        var compound = Assert.IsType<IrCompoundAssignment>(
            Assert.IsType<Kassyi.GSharp.Compiler.Ir.Statements.IrExpressionStatement>(statement).Expression);

        Assert.Equal("(ref b += 1)", IrPrinter.Print(compound));
        Assert.Equal(IrConversionKind.Numeric, compound.InConversion);
        Assert.Equal(IrConversionKind.Numeric, compound.OutConversion);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void UserDefinedEquality_IsNotANullTest() =>
        Assert.IsType<IrBinary>(IrTestCases.CaseResult("UserEquals"));

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Invalid", nameof(IrOpaqueReason.Invalid), "opaque:Invalid(InvocationExpression)")]
    [InlineData("Lambda", nameof(IrOpaqueReason.UnsupportedOperation),
        "opaque:UnsupportedOperation(SimpleLambdaExpression)")]
    [InlineData("SwitchExpression", nameof(IrOpaqueReason.UnsupportedOperation),
        "opaque:UnsupportedOperation(SwitchExpression)")]
    [InlineData("With", nameof(IrOpaqueReason.UnsupportedOperation), "opaque:UnsupportedOperation(WithExpression)")]
    [InlineData("Dereference", nameof(IrOpaqueReason.UnsupportedOperation),
        "opaque:UnsupportedOperation(PointerIndirectionExpression)")]
    [InlineData("Stackalloc", nameof(IrOpaqueReason.UnsupportedOperation),
        "opaque:UnsupportedOperation(StackAllocArrayCreationExpression)")]
    [InlineData("LiftedAdd", nameof(IrOpaqueReason.UnsupportedShape), "opaque:UnsupportedShape(AddExpression)")]
    [InlineData("MultiDimensional", nameof(IrOpaqueReason.UnsupportedShape),
        "opaque:UnsupportedShape(ArrayCreationExpression)")]
    [InlineData("MultiDimensionalElement", nameof(IrOpaqueReason.UnsupportedShape),
        "opaque:UnsupportedShape(ElementAccessExpression)")]
    [InlineData("DelegateInvoke", nameof(IrOpaqueReason.UnsupportedShape),
        "opaque:UnsupportedShape(InvocationExpression)")]
    [InlineData("NestedInitializer", nameof(IrOpaqueReason.UnsupportedShape),
        "opaque:UnsupportedShape(ObjectCreationExpression)")]
    public void UnsupportedExpression_IsAnOpaqueLeaf(string method, string reason, string expected)
    {
        var result = IrTestCases.CaseResult(method);

        var opaque = Assert.IsType<IrOpaqueExpression>(result);
        Assert.Equal(Enum.Parse<IrOpaqueReason>(reason), opaque.Reason);
        Assert.Equal(expected, IrPrinter.Print(result));
    }

    // A throw expression is an operation without a type; it is reported as the kind it is, not as typeless
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ThrowExpression_IsAnUnsupportedOperationInsideItsParent()
    {
        var coalesce = Assert.IsType<IrCoalesce>(IrTestCases.CaseResult("Throw"));
        var conversion = Assert.IsType<IrConversion>(coalesce.WhenNull);

        Assert.Equal(IrOpaqueReason.UnsupportedOperation, Assert.IsType<IrOpaqueExpression>(conversion.Operand).Reason);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void OpaqueArgument_KeepsTheCall()
    {
        var call = Assert.IsType<IrCall>(
            IrTestCases.Result(IrTestCases.Build(IrTestCases.OrderCases, "OrderCases.Order", "LambdaIsNotEvaluated")));

        Assert.IsType<IrOpaqueExpression>(call.Arguments[0].Value);
        Assert.IsType<IrCall>(call.Arguments[1].Value);
    }
}
