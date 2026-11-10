#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies that <see cref="IrInvariants.Verify"/> reports each invariant (#1561): every check gets a tree, built by
/// hand from a valid one, that breaks exactly that invariant, and the check must name it. A check that reports nothing
/// would let the coverage test pass on broken trees.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrInvariantsTests
{
    private static Compilation Compilation => IrTestCases.Compile(IrTestCases.BuilderCases);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ValidTrees_HaveNoViolation()
    {
        foreach (var name in new[] { "RefArgument", "Params", "RefConditional", "OutVar", "ReturnRef", "AddressOf" })
        {
            Assert.Empty(IrInvariants.Verify(IrTestCases.BuildCase(name), Compilation));
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I1_PositionOutsideTheTrees()
    {
        var method = IrTestCases.BuildCase("Literal");
        var constant = (IrConstant)IrTestCases.Result(method);
        var moved = new IrConstant(constant.Value, null, constant.Type, constant.Source with { TreeOrdinal = 999 });

        AssertViolates("I1", WithResult(method, moved));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I3_ValueUsedAsLocation()
    {
        var method = IrTestCases.BuildCase("InNonVariable");
        var call = (IrCall)IrTestCases.Result(method);
        var sum = (IrBinary)call.Arguments[0].Value;
        var asLocation = new IrBinary(sum.Operator, sum.Left, sum.Right, sum.Type, IrCategory.ReadOnlyLocation,
            sum.ConstantValue, sum.Source);

        AssertViolates("I3", WithResult(method, ReplaceArgument(call, 0, asLocation)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I4_PointerCategoryOfAnIntegerRead()
    {
        var method = IrTestCases.BuildCase("InVariable");
        var source = IrTestCases.Result(method).Source;
        var pointer = new IrVariableReference(method.Parameters[0], false, IrCategory.Pointer, default, source);

        AssertViolates("I4", WithResult(method, pointer));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I5_AssignmentTargetThatIsAValue()
    {
        var method = IrTestCases.BuildCase("DiscardAssignment");
        var assignment = (IrAssignment)((IrExpressionStatement)IrTestCases.Statement(method)).Expression;
        var target = new IrVariableReference(method.Parameters[0], false, IrCategory.Value, default,
            assignment.Target.Source);

        AssertViolates("I5", WithStatement(method, new IrExpressionStatement(
            new IrAssignment(target, assignment.Value, false, assignment.Type, assignment.Category, assignment.Source),
            assignment.Source)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I6_RefAssignmentOfAValue()
    {
        var method = IrTestCases.BuildCase("RefLocal");
        var assignment = (IrAssignment)((IrExpressionStatement)IrTestCases.Statement(method, 1)).Expression;

        AssertViolates("I6", WithStatement(method, IrTestCases.Statement(method), new IrExpressionStatement(
            new IrAssignment(assignment.Target, assignment.Value, true, assignment.Type, assignment.Category,
                assignment.Source), assignment.Source)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I7_RefArgumentThatIsAValue()
    {
        var method = IrTestCases.BuildCase("RefArgument");
        var call = (IrCall)IrTestCases.Result(method);
        var reference = (IrVariableReference)call.Arguments[0].Value;
        var value = new IrVariableReference(reference.Variable, false, IrCategory.Value, default, reference.Source);

        AssertViolates("I7", WithResult(method, ReplaceArgument(call, 0, value)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I8_MissingArgument()
    {
        var method = IrTestCases.BuildCase("RefArgument");
        var call = (IrCall)IrTestCases.Result(method);

        AssertViolates("I8", WithResult(method, new IrCall(call.Method, null, ImmutableArray<IrArgument>.Empty,
            call.Type, call.Category, default, call.Source)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I8_EvaluationPositionsThatAreNotAPermutation()
    {
        var method = IrTestCases.BuildCase("Defaults");
        var call = (IrCall)IrTestCases.Result(method);
        var arguments = call.Arguments
            .Select(static argument => new IrArgument(argument.Parameter, argument.Value, argument.Kind, 0))
            .ToImmutableArray();

        AssertViolates("I8", WithResult(method, new IrCall(call.Method, null, arguments, call.Type, call.Category,
            default, call.Source)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I9_TwoInstancesOfOneParameter()
    {
        var method = IrTestCases.BuildCase("InVariable");
        var call = (IrCall)IrTestCases.Result(method);
        var reference = (IrVariableReference)call.Arguments[0].Value;
        var parameter = (IParameterSymbol)(reference.Variable.Symbol ?? throw new InvalidOperationException());
        var copy = IrVariable.CreateParameter(parameter, 0, reference.Variable.Source);

        var withCopy = WithResult(method, ReplaceArgument(call, 0,
            new IrVariableReference(copy, false, reference.Category, default, reference.Source)));

        // The original instance is read first, then the copy
        AssertViolates("I9", WithStatement(withCopy, new IrExpressionStatement(reference, reference.Source),
            withCopy.Body.Statements[0]));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I10_DeclarationOfAParameter()
    {
        var method = IrTestCases.BuildCase("InVariable");
        var declaration = (IrLocalDeclaration)IrTestCases.Statement(method);

        AssertViolates("I10", WithStatement(method,
            new IrLocalDeclaration(method.Parameters[0], declaration.Initializer, declaration.Source)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I11_ShortCircuitAsABinaryNode()
    {
        var method = IrTestCases.BuildCase("Logical");
        var logical = (IrLogical)IrTestCases.Result(method);

        AssertViolates("I11", WithResult(method, new IrBinary(
            new IrOperator<BinaryOperatorKind>(BinaryOperatorKind.ConditionalAnd, null, false), logical.Left,
            logical.Right, logical.Type, logical.Category, default, logical.Source)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I14_BuiltInComparisonWithNullThatIsNotANullTest()
    {
        var method = IrTestCases.BuildCase("NullEquals");
        var test = (IrNullTest)IrTestCases.Result(method);
        var nullConstant = new IrConstant(null, null, test.Operand.Type, test.Source);

        AssertViolates("I14", WithResult(method, new IrBinary(
            new IrOperator<BinaryOperatorKind>(BinaryOperatorKind.Equals, null, false), test.Operand, nullConstant,
            test.Type, test.Category, default, test.Source)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I15_RefConditionalWithValueBranches()
    {
        var method = IrTestCases.BuildCase("ValueConditional");
        var conditional = (IrConditional)IrTestCases.Result(method);

        AssertViolates("I15", WithResult(method, new IrConditional(conditional.Condition, conditional.WhenTrue,
            conditional.WhenFalse, true, IrCategory.Location, default, conditional.Source)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I16_StaticCallWithAReceiver()
    {
        var method = IrTestCases.BuildCase("InVariable");
        var call = (IrCall)IrTestCases.Result(method);

        AssertViolates("I16", WithResult(method, new IrCall(call.Method, call.Arguments[0].Value, call.Arguments,
            call.Type, call.Category, default, call.Source)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I17_DeclaringReferenceOutsideAnOutArgument()
    {
        var method = IrTestCases.BuildCase("OutVar");
        var read = (IrVariableReference)IrTestCases.Result(method);
        var declaring = new IrVariableReference(read.Variable, true, IrCategory.Value, default, read.Source);

        AssertViolates("I17", WithStatement(method, IrTestCases.Statement(method),
            new IrLocalDeclaration(((IrLocalDeclaration)IrTestCases.Statement(method, 1)).Variable, declaring,
                read.Source)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I18_IdentityConversionBetweenTwoTypes()
    {
        var method = IrTestCases.BuildCase("ImplicitNumeric");
        var conversion = (IrConversion)IrTestCases.Result(method);

        AssertViolates("I18", WithResult(method, new IrConversion(conversion.Operand,
            conversion.Conversion with { Kind = IrConversionKind.Identity }, true, conversion.Type, conversion.Category,
            default, conversion.Source)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I19_TemporaryOutsideItsSequence()
    {
        var method = IrTestCases.BuildCase("Literal");
        var constant = IrTestCases.Result(method);
        var temporary = IrVariable.CreateTemporary(constant.Type, RefKind.None, "seq", constant.Source);
        var read = new IrVariableReference(temporary, false, IrCategory.Value, default, constant.Source);

        AssertViolates("I19", WithResult(method, read));
        Assert.Empty(IrInvariants.Verify(WithResult(method, new IrSequence([temporary], [], read, IrCategory.Value,
            constant.Source)), Compilation));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I19_SequenceThatDeclaresALocal()
    {
        var method = IrTestCases.BuildCase("SameLocalTwice");
        var local = ((IrLocalDeclaration)IrTestCases.Statement(method)).Variable;
        var constant = IrTestCases.Result(IrTestCases.BuildCase("Literal"));

        AssertViolates("I19", WithStatement(method, IrTestCases.Statement(method), new IrExpressionStatement(
            new IrSequence([local], [], constant, IrCategory.Value, constant.Source), constant.Source)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I20_LocalOutsideEveryScope()
    {
        var method = IrTestCases.BuildCase("SameLocalTwice");
        var other = IrTestCases.BuildCase("MultipleDeclarators");
        var foreign = ((IrLocalDeclaration)IrTestCases.Statement(other)).Variable;
        var source = IrTestCases.Statement(method).Source;

        AssertViolates("I20", WithStatement(method, new IrExpressionStatement(
            new IrVariableReference(foreign, false, IrCategory.Value, default, source), source)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I21_ValueReturnedFromARefMethod()
    {
        var method = IrTestCases.BuildCase("ReturnRef");
        var @return = (IrReturn)IrTestCases.Statement(method);
        var element = (IrElementReference)(@return.Value ?? throw new InvalidOperationException());
        var value = new IrElementReference(element.Receiver, element.Index, element.Type, IrCategory.Value,
            element.Source);

        AssertViolates("I21", WithStatement(method, new IrReturn(value, @return.Source)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I22_ParamsArgumentThatIsNotAnArray()
    {
        var method = IrTestCases.BuildCase("Params");
        var call = (IrCall)IrTestCases.Result(method);
        var array = (IrArrayCreation)call.Arguments[0].Value;
        var argument = new IrArgument(call.Arguments[0].Parameter, array.Elements[0], IrArgumentKind.ParamArray, 0);

        AssertViolates("I22", WithResult(method, new IrCall(call.Method, null, [argument], call.Type, call.Category,
            default, call.Source)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void I23_SynthesizedNodeOutsideItsParent()
    {
        var method = IrTestCases.BuildCase("ImplicitNumeric");
        var conversion = (IrConversion)IrTestCases.Result(method);
        var operand = (IrVariableReference)conversion.Operand;
        var moved = new IrConversion(operand, conversion.Conversion, true, conversion.Type, conversion.Category,
            default, method.Source.AsSynthesized());

        AssertViolates("I23", WithResult(method, moved));
    }

    private static void AssertViolates(string number, IrMethod method)
    {
        var violations = IrInvariants.Verify(method, Compilation);
        Assert.Contains(violations, violation => violation.StartsWith(number + " ", StringComparison.Ordinal));
    }

    private static IrCall ReplaceArgument(IrCall call, int index, IrExpression value)
    {
        var argument = call.Arguments[index];
        return new IrCall(call.Method, call.Receiver,
            call.Arguments.SetItem(index, new IrArgument(argument.Parameter, value, argument.Kind,
                argument.EvaluationIndex)),
            call.Type, call.Category, call.ConstantValue, call.Source);
    }

    // The method with the initializer of its local result replaced
    private static IrMethod WithResult(IrMethod method, IrExpression result)
    {
        var statements = method.Body.Statements
            .Select(statement => statement is IrLocalDeclaration { Variable.Name: "result" } declaration
                ? new IrLocalDeclaration(declaration.Variable, result, declaration.Source)
                : statement)
            .ToImmutableArray();
        return WithStatements(method, statements);
    }

    private static IrMethod WithStatement(IrMethod method, params IrStatement[] statements) =>
        WithStatements(method, [.. statements]);

    private static IrMethod WithStatements(IrMethod method, ImmutableArray<IrStatement> statements) =>
        new(method.Symbol, method.Parameters, method.This,
            new IrBlock(statements, method.Body.Locals, method.Body.Source), method.Source);
}
