#nullable enable
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Analysis.Checks;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Compiler.Semantics;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Numeric;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies that <see cref="IrNumericFacts"/> extracts facts identical to <see cref="IOperation"/> facts (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
public class IrNumericFactsTests
{
    private const string TestCase = "Ir/IrNumericCases.cs";
    private const string TypeName = "IrNumericCases.Numeric";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Binary_AddOfThreadIndices_MatchesTheOperation()
    {
        var (irMethod, methodSyntax, model) = GetMethod("AddThreadIndices");
        var irBinary = (IrBinary)IrTestCases.Result(irMethod);
        var op = (IBinaryOperation)GetResultOperation(methodSyntax, model);

        var irFacts = IrNumericFacts.Binary(irBinary);
        var opFacts = IntegerBinaryFacts.Of(op);

        AssertBinaryFactsEqual(opFacts, irFacts);
        Assert.Equal(new IntegerRange(0, 1023), irFacts.Left);
        Assert.Equal(new IntegerRange(0, 1023), irFacts.Right);
        Assert.Null(IntegerSemantics.GetBinaryHelper(irFacts));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Binary_DivideByLength_MatchesTheOperation()
    {
        var (irMethod, methodSyntax, model) = GetMethod("DivideByLength");
        var irBinary = (IrBinary)IrTestCases.Result(irMethod);
        var op = (IBinaryOperation)GetResultOperation(methodSyntax, model);

        var irFacts = IrNumericFacts.Binary(irBinary);
        var opFacts = IntegerBinaryFacts.Of(op);

        AssertBinaryFactsEqual(opFacts, irFacts);
        Assert.Equal(new IntegerRange(0, int.MaxValue), irFacts.Right);
        Assert.Equal(GSharpInternalNames.IntegerSemantics.DivideInt32, IntegerSemantics.GetBinaryHelper(irFacts));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Binary_DivideByByte_MatchesTheOperation()
    {
        var (irMethod, methodSyntax, model) = GetMethod("DivideByCount");
        var irBinary = (IrBinary)IrTestCases.Result(irMethod);
        var op = (IBinaryOperation)GetResultOperation(methodSyntax, model);

        var irFacts = IrNumericFacts.Binary(irBinary);
        var opFacts = IntegerBinaryFacts.Of(op);

        AssertBinaryFactsEqual(opFacts, irFacts);
        Assert.Equal(new IntegerRange(0, 255), irFacts.Right);
        Assert.Equal(GSharpInternalNames.IntegerSemantics.DivideInt32, IntegerSemantics.GetBinaryHelper(irFacts));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Binary_MixedUnsigned_MatchesThePromotedType()
    {
        var (irMethod, methodSyntax, model) = GetMethod("MixedUnsigned");
        var irBinary = (IrBinary)IrTestCases.Result(irMethod);
        var op = (IBinaryOperation)GetResultOperation(methodSyntax, model);

        var irPromoted = ArithmeticConversions.TryGetPromotedType(
            irBinary.Operator.Kind,
            irBinary.Operator.Method == null,
            irBinary.Type,
            irBinary.Type,
            IrNumericFacts.SourceType(irBinary.Left),
            IrNumericFacts.SourceType(irBinary.Right),
            out var irType);

        var opPromoted = ArithmeticConversions.TryGetPromotedType(op, out var opType);

        Assert.True(irPromoted);
        Assert.True(opPromoted);
        Assert.True(SymbolEqualityComparer.Default.Equals(opType, irType));
        Assert.Equal(SpecialType.System_Int64, irType?.SpecialType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Unary_NegateUnsigned_MatchesThePromotedType()
    {
        var (irMethod, methodSyntax, model) = GetMethod("NegateUnsigned");
        var irUnary = (IrUnary)IrTestCases.Result(irMethod);
        var op = (IUnaryOperation)GetResultOperation(methodSyntax, model);

        var irPromoted = ArithmeticConversions.TryGetPromotedType(
            irUnary.Operator.Kind,
            irUnary.Operator.Method == null,
            irUnary.Type,
            IrNumericFacts.SourceType(irUnary.Operand),
            out var irType);

        var opPromoted = ArithmeticConversions.TryGetPromotedType(op, out var opType);

        Assert.True(irPromoted);
        Assert.True(opPromoted);
        Assert.True(SymbolEqualityComparer.Default.Equals(opType, irType));
        Assert.Equal(SpecialType.System_Int64, irType?.SpecialType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Binary_SmallEnumOr_MatchesTheNarrowEnumType()
    {
        var (irMethod, methodSyntax, model) = GetMethod("SmallOr");
        var irBinary = (IrBinary)IrTestCases.Result(irMethod);
        var op = (IBinaryOperation)GetResultOperation(methodSyntax, model);

        var irFacts = IrNumericFacts.Binary(irBinary);
        var opFacts = IntegerBinaryFacts.Of(op);

        var irNarrow = IntegerSemantics.TryGetNarrowEnumResultType(in irFacts, out var irNarrowType);
        var opNarrow = IntegerSemantics.TryGetNarrowEnumResultType(in opFacts, out var opNarrowType);

        Assert.True(irNarrow);
        Assert.True(opNarrow);
        Assert.True(SymbolEqualityComparer.Default.Equals(opNarrowType, irNarrowType));
        Assert.Equal(SpecialType.System_Byte, irNarrowType?.SpecialType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Step_IncrementShort_MatchesTheOperation()
    {
        var (irMethod, methodSyntax, model) = GetMethod("IncrementShort");
        var irIncrement = (IrIncrement)IrTestCases.Result(irMethod);
        var op = (IIncrementOrDecrementOperation)GetResultOperation(methodSyntax, model);

        var irFacts = IrNumericFacts.Step(irIncrement);
        var opFacts = IntegerStepFacts.Of(op);

        AssertStepFactsEqual(opFacts, irFacts);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Compound_ShortPlusInt_MatchesTheOperation()
    {
        var (irMethod, methodSyntax, model) = GetMethod("ShortCompound");
        var irCompound = (IrCompoundAssignment)((IrExpressionStatement)IrTestCases.Statement(irMethod, 0)).Expression;
        var op = (ICompoundAssignmentOperation)GetFirstStatementOperation(methodSyntax, model);

        var irFacts = IrNumericFacts.Compound(irCompound);
        var opFacts = IntegerCompoundFacts.Of(op);

        AssertCompoundFactsEqual(opFacts, irFacts);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Compound_ULongShiftLeft_MatchesTheOperation()
    {
        var (irMethod, methodSyntax, model) = GetMethod("ULongShiftCompound");
        var irCompound = (IrCompoundAssignment)((IrExpressionStatement)IrTestCases.Statement(irMethod, 0)).Expression;
        var op = (ICompoundAssignmentOperation)GetFirstStatementOperation(methodSyntax, model);

        var irFacts = IrNumericFacts.Compound(irCompound);
        var opFacts = IntegerCompoundFacts.Of(op);

        AssertCompoundFactsEqual(opFacts, irFacts);
        Assert.Equal(63, IntegerSemantics.GetCompoundShiftMask(irFacts));
    }

    private static (IrMethod IrMethod, MethodDeclarationSyntax MethodSyntax, SemanticModel Model) GetMethod(
        string methodName)
    {
        var compilation = IrTestCases.Compile(TestCase);
        var symbol = IrTestCases.FindMethod(compilation, TypeName, methodName);
        var syntaxRef = symbol.DeclaringSyntaxReferences.Single();
        var methodSyntax = (MethodDeclarationSyntax)syntaxRef.GetSyntax();
        var tree = syntaxRef.SyntaxTree;
        var model = compilation.GetSemanticModel(tree);
        var irMethod = IrBuilder.TryBuild(symbol, compilation, CancellationToken.None)
            ?? throw new InvalidOperationException($"No IR for {TypeName}.{methodName}.");
        return (irMethod, methodSyntax, model);
    }

    private static IOperation GetResultOperation(MethodDeclarationSyntax methodSyntax, SemanticModel model)
    {
        var declarator = methodSyntax.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Single(static d => d.Identifier.Text == "result");
        return model.GetOperation(declarator.Initializer!.Value)
            ?? throw new InvalidOperationException("No operation for result initializer.");
    }

    private static IOperation GetFirstStatementOperation(MethodDeclarationSyntax methodSyntax, SemanticModel model)
    {
        var statement = (ExpressionStatementSyntax)methodSyntax.Body!.Statements[0];
        return model.GetOperation(statement.Expression)
            ?? throw new InvalidOperationException("No operation for statement expression.");
    }

    private static void AssertBinaryFactsEqual(in IntegerBinaryFacts expected, in IntegerBinaryFacts actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.True(SymbolEqualityComparer.Default.Equals(expected.Type, actual.Type));
        Assert.True(SymbolEqualityComparer.Default.Equals(expected.LeftType, actual.LeftType));
        Assert.Equal(expected.IsBuiltIn, actual.IsBuiltIn);
        Assert.Equal(expected.HasConstantValue, actual.HasConstantValue);
        Assert.Equal(expected.Left, actual.Left);
        Assert.Equal(expected.Right, actual.Right);
        Assert.Equal(IntegerSemantics.GetBinaryHelper(expected), IntegerSemantics.GetBinaryHelper(actual));
        Assert.Equal(IntegerSemantics.GetShiftMask(expected), IntegerSemantics.GetShiftMask(actual));
    }

    private static void AssertStepFactsEqual(in IntegerStepFacts expected, in IntegerStepFacts actual)
    {
        Assert.Equal(expected.IsIncrement, actual.IsIncrement);
        Assert.True(SymbolEqualityComparer.Default.Equals(expected.Type, actual.Type));
        Assert.True(SymbolEqualityComparer.Default.Equals(expected.TargetType, actual.TargetType));
        Assert.Equal(expected.IsBuiltIn, actual.IsBuiltIn);
        Assert.Equal(
            IntegerSemantics.GetStepHelper(expected, undo: false),
            IntegerSemantics.GetStepHelper(actual, undo: false));
        Assert.Equal(
            IntegerSemantics.GetStepHelper(expected, undo: true),
            IntegerSemantics.GetStepHelper(actual, undo: true));
        Assert.True(SymbolEqualityComparer.Default.Equals(
            IntegerSemantics.GetStepNarrowing(expected),
            IntegerSemantics.GetStepNarrowing(actual)));
    }

    private static void AssertCompoundFactsEqual(in IntegerCompoundFacts expected, in IntegerCompoundFacts actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.True(SymbolEqualityComparer.Default.Equals(expected.TargetType, actual.TargetType));
        Assert.True(SymbolEqualityComparer.Default.Equals(expected.ValueType, actual.ValueType));
        Assert.Equal(expected.IsBuiltIn, actual.IsBuiltIn);
        Assert.Equal(expected.Target, actual.Target);
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(IntegerSemantics.GetCompoundHelper(expected), IntegerSemantics.GetCompoundHelper(actual));
        Assert.Equal(IntegerSemantics.GetCompoundShiftMask(expected), IntegerSemantics.GetCompoundShiftMask(actual));
        Assert.True(SymbolEqualityComparer.Default.Equals(
            IntegerSemantics.GetCompoundNarrowing(expected),
            IntegerSemantics.GetCompoundNarrowing(actual)));
    }
}
