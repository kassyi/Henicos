#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Sequencing;
using Kassyi.GSharp.Compiler.Ir.Statements;
using static Kassyi.GSharp.Tests.Shared.Transpiler.Ir.IrSequenceTestCases;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// The sequences the sequence pass makes for each form on <c>IrSequenceCases.Forms</c>: which operands go into
/// temporaries, in which order, of which type and declaration, as the syntax path writes its temporaries.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrSequenceFormsTests
{
    [Theory]
    [InlineData("NoInterference")]
    [InlineData("CompoundPlain")]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_OperandsDoNotInterfere_AddsNothing(string name)
    {
        var (decided, sequenced) = Run(name);
        Assert.Same(decided, sequenced);
        Assert.Empty(SequencesOf(sequenced.Body));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_NothingToChange_ReturnsTheSameMethod()
    {
        var (decided, sequenced) = Run("NoInterference");
        var (whileDecided, whileSequenced) = Run("WhileBarrier", "Ir/IrLinearCases.cs", "IrLinear.IrLinearCases");

        Assert.Same(decided, sequenced);
        Assert.Same(whileDecided, whileSequenced);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_BinaryWriteThenRead_SpillsBothOperands()
    {
        var (decided, sequenced) = Run("BinaryWriteThenRead");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(2, declarations.Length);
        AssertDeclaration(declarations[0], SpecialType.System_Int32, IrDeclaredTypeForm.Deduced, RefKind.None);
        AssertDeclaration(declarations[1], SpecialType.System_Int32, IrDeclaredTypeForm.Deduced, RefKind.None);
        Assert.IsType<IrCall>(declarations[0].Initializer);
        Assert.IsType<IrVariableReference>(declarations[1].Initializer);
        var binary = Assert.IsType<IrBinary>(sequence.Value);
        AssertReads(binary.Left, declarations[0]);
        AssertReads(binary.Right, declarations[1]);
        Assert.Same(First<IrBinary>(decided.Body).Decision, binary.Decision);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_BinaryTrapAfterWrite_UsesThrowInterference()
    {
        var (_, sequenced) = Run("BinaryTrapAfterWrite");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(2, declarations.Length);
        Assert.IsType<IrCall>(declarations[0].Initializer);
        Assert.IsType<IrBinary>(declarations[1].Initializer);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_ShiftOfInterferingOperands_KeepsTheOperator()
    {
        var (decided, sequenced) = Run("ShiftKeepsTheOrder");
        var shift = First<IrBinary>(decided.Body);

        Assert.Null(shift.Decided.Helper);
        Assert.Empty(SequencesOf(sequenced.Body));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_FloatRemainder_SpillsConvertedValues()
    {
        var (_, sequenced) = Run("FloatRemainder");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(2, declarations.Length);
        AssertDeclaration(declarations[0], SpecialType.System_Double, IrDeclaredTypeForm.Named, RefKind.None);
        AssertDeclaration(declarations[1], SpecialType.System_Double, IrDeclaredTypeForm.Named, RefKind.None);
        Assert.NotNull(Assert.IsType<IrBinary>(sequence.Value).Decided.FloatingRemainder);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_NestedBinary_NumbersInSpellingOrder()
    {
        var (_, sequenced) = Run("NestedBinary");
        var sequences = SequencesOf(sequenced.Body);

        Assert.Equal(2, sequences.Length);
        var outer = DeclarationsOf(sequences[0]);
        var inner = DeclarationsOf(sequences[1]);
        Assert.Same(sequences[1], outer[0].Initializer);
        var numbered = IrSequenceNumbering.Of(sequenced.Body);
        Assert.Equal([0, 1, 2, 3],
            new[] { outer[0], inner[0], inner[1], outer[1] }.Select(d => numbered.IndexOf(d.Variable)).ToArray());
        Assert.Equal(4, numbered.Length);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_InWhileCondition_StaysInTheCondition()
    {
        var (_, sequenced) = Run("WhileCondition");
        var loop = Assert.IsType<IrWhile>(IrTestCases.Statement(sequenced));
        var comparison = Assert.IsType<IrBinary>(loop.Condition);

        Assert.IsType<IrSequence>(comparison.Left);
        Assert.Single(SequencesOf(sequenced.Body));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_ElementAssignment_SpillsArrayIndexAndValue()
    {
        var (_, sequenced) = Run("ElementAssignment");
        var sequence = Assert.IsType<IrSequence>(StatementExpression(sequenced));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(3, declarations.Length);
        Assert.IsAssignableFrom<IArrayTypeSymbol>(declarations[0].Variable.Type);
        Assert.Equal(IrDeclaredTypeForm.Deduced, declarations[0].Variable.DeclaredTypeForm);
        Assert.Equal(RefKind.None, declarations[0].Variable.RefKind);
        AssertDeclaration(declarations[1], SpecialType.System_Int32, IrDeclaredTypeForm.Deduced, RefKind.None);
        AssertDeclaration(declarations[2], SpecialType.System_Int32, IrDeclaredTypeForm.Named, RefKind.None);
        var assignment = Assert.IsType<IrAssignment>(sequence.Value);
        var element = Assert.IsType<IrElementReference>(assignment.Target);
        AssertReads(element.Receiver, declarations[0]);
        AssertReads(element.Index, declarations[1]);
        AssertReads(assignment.Value, declarations[2]);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_FieldAssignment_BindsTheTargetByReference()
    {
        var (_, sequenced) = Run("FieldAssignment");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(2, declarations.Length);
        Assert.Equal(RefKind.Ref, declarations[0].Variable.RefKind);
        Assert.Equal(IrDeclaredTypeForm.Deduced, declarations[0].Variable.DeclaredTypeForm);
        Assert.Equal(IrCategory.Location, declarations[0].Initializer?.Category);
        AssertDeclaration(declarations[1], SpecialType.System_Int32, IrDeclaredTypeForm.Named, RefKind.None);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_AssignmentToALocal_AddsNothing()
    {
        var (_, sequenced) = Run("LocalAssignment");
        Assert.Empty(SequencesOf(sequenced.Body));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_CompoundNoCopy_SpillsTargetAndValue()
    {
        var (_, sequenced) = Run("CompoundNoCopy");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(2, sequence.SideEffects.Length);
        Assert.Equal(RefKind.Ref, declarations[0].Variable.RefKind);
        AssertDeclaration(declarations[1], SpecialType.System_Int32, IrDeclaredTypeForm.Deduced, RefKind.None);
        Assert.False(IrSequenceSupport.HasTargetCopy(sequence));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_CompoundWithCopy_CopiesTheTarget()
    {
        var (_, sequenced) = Run("CompoundWithCopy");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(3, declarations.Length);
        Assert.Equal(RefKind.Ref, declarations[0].Variable.RefKind);
        AssertDeclaration(declarations[1], SpecialType.System_Int32, IrDeclaredTypeForm.Deduced, RefKind.None);
        AssertReads(declarations[1].Initializer, declarations[0]);
        AssertDeclaration(declarations[2], SpecialType.System_Int32, IrDeclaredTypeForm.Deduced, RefKind.None);
        Assert.True(IrSequenceSupport.HasTargetCopy(sequence));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_TargetPart_SpillsTheIndexOnly()
    {
        var (decided, sequenced) = Run("TargetPart");
        Assert.NotNull(First<IrCompoundAssignment>(decided.Body).Decided.Helper);
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declaration = Assert.Single(DeclarationsOf(sequence));

        AssertDeclaration(declaration, SpecialType.System_Int32, IrDeclaredTypeForm.Deduced, RefKind.None);
        Assert.IsType<IrIncrement>(declaration.Initializer);
        var compound = Assert.IsType<IrCompoundAssignment>(sequence.Value);
        var field = Assert.IsType<IrFieldReference>(compound.Target);
        var element = Assert.IsType<IrElementReference>(field.Receiver);
        AssertReads(element.Index, declaration);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_StepTargetPart_SpillsTheIndexOnly()
    {
        var (decided, sequenced) = Run("StepTargetPart");
        var step = Assert.IsType<IrIncrement>(StatementExpression(decided));
        Assert.NotNull(step.Decided.Helper);
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declaration = Assert.Single(DeclarationsOf(sequence));

        Assert.IsType<IrIncrement>(declaration.Initializer);
        var increment = Assert.IsType<IrIncrement>(sequence.Value);
        AssertReads(Assert.IsType<IrElementReference>(increment.Target).Index, declaration);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_CheckedElement_SpillsReceiverAndIndex()
    {
        var (decided, sequenced) = Run("CheckedElement");
        var original = Assert.IsType<IrElementReference>(
            Assert.IsType<IrAssignment>(StatementExpression(decided)).Value);
        Assert.Equal(IrElementCheck.NullChecked, original.Decided.Check);
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(2, declarations.Length);
        Assert.IsAssignableFrom<IArrayTypeSymbol>(declarations[0].Variable.Type);
        Assert.Equal(IrDeclaredTypeForm.Deduced, declarations[0].Variable.DeclaredTypeForm);
        AssertDeclaration(declarations[1], SpecialType.System_Int32, IrDeclaredTypeForm.Deduced, RefKind.None);
        var element = Assert.IsType<IrElementReference>(sequence.Value);
        Assert.Equal(IrElementCheck.NullChecked, element.Decided.Check);
        Assert.Equal(original.Category, sequence.Category);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_LengthOfAStackArray_DoesNotThrow_WhenDecisionsAreRead()
    {
        var (_, sequenced) = Run("StackArrayLength");
        Assert.Empty(SequencesOf(sequenced.Body));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_CompoundFloatRemainder_NamesTheTargetType()
    {
        var (_, sequenced) = Run("CompoundFloatRemainder");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(3, declarations.Length);
        Assert.Equal(RefKind.Ref, declarations[0].Variable.RefKind);
        Assert.Equal(IrDeclaredTypeForm.Deduced, declarations[0].Variable.DeclaredTypeForm);
        AssertDeclaration(declarations[1], SpecialType.System_Double, IrDeclaredTypeForm.Deduced, RefKind.None);
        AssertReads(declarations[1].Initializer, declarations[0]);
        AssertDeclaration(declarations[2], SpecialType.System_Double, IrDeclaredTypeForm.Named, RefKind.None);
        Assert.IsType<IrCall>(declarations[2].Initializer);
        Assert.True(IrSequenceSupport.HasTargetCopy(sequence));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_FloatRemainderTargetPart_SpillsTheIndex()
    {
        var (_, sequenced) = Run("FloatRemainderTargetPart");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declaration = Assert.Single(DeclarationsOf(sequence));

        AssertDeclaration(declaration, SpecialType.System_Int32, IrDeclaredTypeForm.Deduced, RefKind.None);
        Assert.IsType<IrIncrement>(declaration.Initializer);
        Assert.IsType<IrCompoundAssignment>(sequence.Value);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_ElementAssignmentWithInterferingIndex_IsOneSequence()
    {
        var (_, sequenced) = Run("ElementAssignmentWithInterferingIndex");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(3, declarations.Length);
        Assert.IsAssignableFrom<IArrayTypeSymbol>(declarations[0].Variable.Type);
        Assert.Equal(IrDeclaredTypeForm.Deduced, declarations[0].Variable.DeclaredTypeForm);
        AssertDeclaration(declarations[1], SpecialType.System_Int32, IrDeclaredTypeForm.Deduced, RefKind.None);
        AssertDeclaration(declarations[2], SpecialType.System_Int32, IrDeclaredTypeForm.Named, RefKind.None);
        var assignment = Assert.IsType<IrAssignment>(sequence.Value);
        Assert.IsType<IrElementReference>(assignment.Target);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_PromotedBinary_SpillsUnderTheImplicitConversion()
    {
        var (decided, sequenced) = Run("PromotedBinary");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(2, declarations.Length);
        AssertDeclaration(declarations[0], SpecialType.System_Int32, IrDeclaredTypeForm.Deduced, RefKind.None);
        Assert.IsType<IrVariableReference>(declarations[0].Initializer);
        AssertDeclaration(declarations[1], SpecialType.System_Int64, IrDeclaredTypeForm.Deduced, RefKind.None);
        Assert.IsType<IrCall>(declarations[1].Initializer);
        var binary = Assert.IsType<IrBinary>(sequence.Value);
        var conversion = Assert.IsType<IrConversion>(binary.Left);
        Assert.True(conversion.IsImplicit);
        AssertReads(conversion.Operand, declarations[0]);
        Assert.Same(First<IrBinary>(decided.Body).Decision, binary.Decision);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_ConvertedAssignmentValue_TakesTheConversionWithIt()
    {
        var (_, assigned) = Run("ConvertedAssignmentValue");
        var assignedSequence = Assert.Single(SequencesOf(assigned.Body));
        var value = DeclarationsOf(assignedSequence)[2];
        AssertDeclaration(value, SpecialType.System_Int64, IrDeclaredTypeForm.Named, RefKind.None);
        Assert.True(Assert.IsType<IrConversion>(value.Initializer).IsImplicit);
        AssertReads(Assert.IsType<IrAssignment>(assignedSequence.Value).Value, value);

        var (_, remainder) = Run("RemainderWithFloat");
        var remainderSequence = Assert.Single(SequencesOf(remainder.Body));
        var right = DeclarationsOf(remainderSequence)[1];
        AssertDeclaration(right, SpecialType.System_Double, IrDeclaredTypeForm.Named, RefKind.None);
        var conversion = Assert.IsType<IrConversion>(right.Initializer);
        Assert.Equal(SpecialType.System_Single, conversion.Operand.Type.SpecialType);
        AssertReads(Assert.IsType<IrBinary>(remainderSequence.Value).Right, right);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_EnumValue_IsConvertedButDeduced()
    {
        var (_, sequenced) = Run("EnumAssignmentValue");
        var value = DeclarationsOf(Assert.Single(SequencesOf(sequenced.Body)))[2];

        Assert.Equal(TypeKind.Enum, value.Variable.Type.TypeKind);
        Assert.Equal("Color", value.Variable.Type.Name);
        Assert.Equal(IrDeclaredTypeForm.Deduced, value.Variable.DeclaredTypeForm);
        Assert.False(Assert.IsType<IrConversion>(value.Initializer).IsImplicit);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_IsPure_DoesNotChangeTheInput()
    {
        var compilation = IrTestCases.Compile(Forms);
        var decided = Decide(compilation, IrTestCases.Build(Forms, FormsType, "BinaryWriteThenRead"));
        var statement = IrTestCases.Statement(decided);
        var before = IrTestCases.PrintLf(decided);

        var first = Sequence(compilation, decided);
        var second = Sequence(compilation, decided);

        Assert.Same(statement, IrTestCases.Statement(decided));
        Assert.Equal(before, IrTestCases.PrintLf(decided));
        Assert.Empty(SequencesOf(decided.Body));
        Assert.NotSame(first, second);
        Assert.Equal(IrTestCases.PrintLf(first), IrTestCases.PrintLf(second));
        Assert.Single(SequencesOf(first.Body));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Sequence_BeforeTheDecisionPass_IsAnError()
    {
        var compilation = IrTestCases.Compile(Forms);
        var classified = IrClassifier.Classify(
            IrTestCases.Build(Forms, FormsType, "BinaryWriteThenRead"), compilation);

        Assert.Throws<InvalidOperationException>(() => Sequence(compilation, classified));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_KeepsParenthesesAndDecisions()
    {
        var (decided, sequenced) = Run("ParenthesizedBinary");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var binary = Assert.IsType<IrBinary>(sequence.Value);

        Assert.Equal(1, sequence.Parentheses);
        Assert.Equal(0, binary.Parentheses);
        Assert.Same(First<IrBinary>(decided.Body).Decision, binary.Decision);
    }

    private static void AssertDeclaration(IrLocalDeclaration declaration, SpecialType type, IrDeclaredTypeForm form,
        RefKind refKind)
    {
        Assert.Equal(type, declaration.Variable.Type.SpecialType);
        Assert.Equal(form, declaration.Variable.DeclaredTypeForm);
        Assert.Equal(refKind, declaration.Variable.RefKind);
        Assert.Equal(IrVariableKind.Temporary, declaration.Variable.Kind);
    }

    private static void AssertReads(IrExpression? expression, IrLocalDeclaration declaration) =>
        Assert.Same(declaration.Variable, Assert.IsType<IrVariableReference>(expression).Variable);
}
