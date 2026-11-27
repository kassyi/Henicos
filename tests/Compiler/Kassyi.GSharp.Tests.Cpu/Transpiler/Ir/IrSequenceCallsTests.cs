#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Sequencing;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;
using Kassyi.GSharp.Compiler.Analysis;
using static Kassyi.GSharp.Tests.Shared.Transpiler.Ir.IrSequenceTestCases;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// The sequences the sequence pass makes for the arguments of calls and struct creations on
/// <c>IrSequenceCases.Forms</c>, as <c>EvaluationOrder.BeginCall</c> of the syntax path writes them: the written
/// arguments in the C# order of evaluation up to the last one that interferes, read in parameter order.
/// </summary>
/// <remarks>
/// The callees have no GPU attribute, so their effects come from their declarations: <c>Bump(ref x)</c> writes its
/// <see langword="ref"/> argument, <c>Touch(m)</c> writes memory, the others read their arguments only.
/// </remarks>
[Category(TestCategory.Compiler)]
public class IrSequenceCallsTests
{
    [Theory]
    [InlineData("CallNoInterference")]
    [InlineData("CallNamedNoInterference")]
    [InlineData("CallSingleArgument")]
    [InlineData("CreationInOrder")]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_CallWithoutInterference_AddsNothing(string name)
    {
        var (decided, sequenced) = Run(name);

        Assert.Same(decided, sequenced);
        Assert.Empty(SequencesOf(sequenced.Body));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_CallArgumentsInterfere_SpillsUpToTheLastInterferingArgument()
    {
        var (decided, sequenced) = Run("CallArgumentsInterfere");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(2, declarations.Length);
        AssertNamedInt(declarations[0]);
        AssertNamedInt(declarations[1]);
        Assert.IsType<IrCall>(declarations[0].Initializer);
        Assert.IsType<IrVariableReference>(declarations[1].Initializer);
        var call = Assert.IsType<IrCall>(sequence.Value);
        var original = OuterCall(decided, "Two");
        AssertReads(call.Arguments[0].Value, declarations[0]);
        AssertReads(call.Arguments[1].Value, declarations[1]);
        Assert.Same(original.Decision, call.Decision);
        Assert.Equal(original.Decided.Arguments, call.Decided.Arguments);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_CallNamedWithInterference_SpillsInWrittenOrder()
    {
        var (_, sequenced) = Run("CallNamedWithInterference");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(2, declarations.Length);
        Assert.Equal("Bump", Assert.IsType<IrCall>(declarations[0].Initializer).Method.Name);
        Assert.IsType<IrVariableReference>(declarations[1].Initializer);
        var call = Assert.IsType<IrCall>(sequence.Value);
        Assert.Equal("a", call.Arguments[0].Parameter.Name);
        AssertReads(call.Arguments[0].Value, declarations[1]);
        AssertReads(call.Arguments[1].Value, declarations[0]);
        var numbered = IrSequenceNumbering.Of(sequenced.Body);
        Assert.Equal([0, 1], declarations.Select(d => numbered.IndexOf(d.Variable)).ToArray());
        Assert.Equal(2, numbered.Length);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Sequence_CallThree_StopsAtTheLastInterferingArgument()
    {
        var (_, sequenced) = Run("CallThree");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));

        Assert.Equal(2, DeclarationsOf(sequence).Length);
        var call = Assert.IsType<IrCall>(sequence.Value);
        Assert.IsType<IrConstant>(call.Arguments[2].Value);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Sequence_CallThreeAfterLast_LeavesAReadAfterTheLastInterferenceInPlace()
    {
        // Three(Bump(ref x), x, y): y reads a variable nothing writes, after the last interfering argument
        var (_, sequenced) = Run("CallThreeAfterLast");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));

        Assert.Equal(2, DeclarationsOf(sequence).Length);
        var argument = Assert.IsType<IrVariableReference>(Assert.IsType<IrCall>(sequence.Value).Arguments[2].Value);
        Assert.NotEqual(IrVariableKind.Temporary, argument.Variable.Kind);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Sequence_CreationWithDefault_SpillsWrittenArgumentsOnly()
    {
        // new Cell3(b: a, a: b): out of parameter order, so every written argument moves; the default of c does not
        var (_, sequenced) = Run("CreationWithDefault");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));

        Assert.Equal(2, DeclarationsOf(sequence).Length);
        var creation = Assert.IsType<IrStructCreation>(sequence.Value);
        Assert.Equal(IrArgumentKind.DefaultValue, creation.Arguments[2].Kind);
        Assert.IsNotType<IrVariableReference>(creation.Arguments[2].Value);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_CallRefArgumentAfterTheWrite_AddsNothing()
    {
        // Pair(Bump(ref x), ref y): evaluating the location of y reads nothing that Bump writes
        var (decided, sequenced) = Run("CallRefArgument");

        Assert.Same(decided, sequenced);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_CallRefArgument_BindsByReference()
    {
        // RefFirst(ref r[i], Bump(ref i), i): the location of r[i] reads i, which Bump writes, which the last reads
        var (_, sequenced) = Run("CallRefArgumentFirst");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(3, declarations.Length);
        Assert.Equal(RefKind.Ref, declarations[0].Variable.RefKind);
        Assert.Equal(IrDeclaredTypeForm.Deduced, declarations[0].Variable.DeclaredTypeForm);
        Assert.Equal(IrCategory.Location, declarations[0].Initializer?.Category);
        AssertNamedInt(declarations[1]);
        AssertNamedInt(declarations[2]);
        AssertReads(Assert.IsType<IrCall>(sequence.Value).Arguments[0].Value, declarations[0]);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Sequence_CallWithDefault_SpillsWrittenArgumentsOnly()
    {
        var (decided, sequenced) = Run("CallWithDefault");

        Assert.Contains(OuterCall(decided, "WithDefault").Arguments,
            static argument => argument.Kind == IrArgumentKind.DefaultValue);
        Assert.Empty(SequencesOf(sequenced.Body));
        Assert.Empty(IrSequenceNumbering.Of(sequenced.Body));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_CallProvenArgument_DoesNotThrowForTheCheck()
    {
        var (_, sequenced) = Run("CallProven");

        Assert.Empty(SequencesOf(sequenced.Body));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_CallUnprovenArgument_ThrowsForTheCheck()
    {
        var (decided, sequenced) = Run("CallUnproven");
        var parameter = OuterCall(decided, "Use").Arguments[0].Parameter;

        Assert.Equal(ArgumentCheckKind.Array, ArgumentNullCheck.GetArgumentCheckKind(parameter));
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        Assert.Equal(2, DeclarationsOf(sequence).Length);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_CreationOutOfOrder_SpillsEveryWrittenArgument()
    {
        var (decided, sequenced) = Run("CreationOutOfOrder");
        var creation = First<IrStructCreation>(decided.Body);
        var compilation = IrTestCases.Compile(Forms);
        var constructor = Assert.IsAssignableFrom<IMethodSymbol>(creation.Constructor);

        var kind = StructCreationClassifier.Classify(creation.Type, constructor, compilation,
            HostDialects.Cpu, out _);
        Assert.Equal(StructCreationKind.Factory, kind);
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);
        Assert.Equal(2, declarations.Length);
        AssertNamedInt(declarations[0]);
        AssertNamedInt(declarations[1]);
        var value = Assert.IsType<IrStructCreation>(sequence.Value);
        AssertReads(value.Arguments[0].Value, declarations[1]);
        AssertReads(value.Arguments[1].Value, declarations[0]);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_CreationArgumentsInterfere_SpillsUpToTheLastInterferingArgument()
    {
        var (_, sequenced) = Run("CreationInterfere");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));

        Assert.Equal(2, DeclarationsOf(sequence).Length);
        Assert.IsType<IrStructCreation>(sequence.Value);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_CallNestedInArgument_NumbersTheInnerSequenceInsideTheFirstDeclaration()
    {
        var (_, sequenced) = Run("CallNestedInArgument");
        var sequences = SequencesOf(sequenced.Body);

        Assert.Equal(2, sequences.Length);
        var outer = DeclarationsOf(sequences[0]);
        Assert.Same(sequences[1], outer[0].Initializer);
        var inner = DeclarationsOf(sequences[1]);
        var numbered = IrSequenceNumbering.Of(sequenced.Body);
        Assert.Equal([0, 1, 2, 3],
            new[] { outer[0], inner[0], inner[1], outer[1] }.Select(d => numbered.IndexOf(d.Variable)).ToArray());
        Assert.Equal(4, numbered.Length);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Sequence_CallWithParamsArray_LeavesTheArgumentsInPlace()
    {
        var (decided, sequenced) = Run("CallParams", Forms, "IrSequenceCases.SkippedForms");

        Assert.True(IrSequenceSupport.SkipsArgumentsOf(OuterCall(decided, "Sum")));
        Assert.Empty(SequencesOf(sequenced.Body));
    }

    [Theory]
    [InlineData("CallInRvalue")]
    [InlineData("ExtensionInRvalue")]
    [InlineData("CreationInRvalue")]
    [Scenario(TestScenario.Boundary)]
    public void Sequence_UnconvertedRvalueToInParameter_CopiesWhereTheSyntaxPathBindsAReference(string name)
    {
        const string type = "IrSequenceCases.InRvalueForms";
        var (_, sequenced) = Run(name, Forms, type);
        var declarations = DeclarationsOf(Assert.Single(SequencesOf(sequenced.Body)));

        Assert.Equal(2, declarations.Length);
        AssertNamedInt(declarations[0]);
        AssertNamedInt(declarations[1]);

        // The known difference: the same two temporaries, the first one a reference on the syntax path
        var compilation = IrTestCases.Compile(Forms);
        var declaration = compilation.GetTypeByMetadataName(type)?.GetMembers(name).OfType<IMethodSymbol>().Single()
                              .DeclaringSyntaxReferences.Single().GetSyntax() as MethodDeclarationSyntax ??
                          throw new InvalidOperationException($"{name} is missing.");
        var old = IrSequenceTemporaries.OfSyntaxPath(declaration, compilation.GetSemanticModel(declaration.SyntaxTree));
        var ir = IrSequenceTemporaries.Of(sequenced);
        Assert.Equal(ir.Firsts.ToArray(), old.Firsts.ToArray());
        Assert.Equal(new[] { (0, IrSequenceDeclarationForm.Ref), (1, IrSequenceDeclarationForm.Named) },
            old.Declarations.ToArray());
        Assert.Equal(new[] { (0, IrSequenceDeclarationForm.Named), (1, IrSequenceDeclarationForm.Named) },
            ir.Declarations.ToArray());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_InstanceInReceiverCopy_SpillsDeducedCopyBeforeArgument()
    {
        var (_, sequenced) = Run("InstanceInReceiverCopy");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(2, declarations.Length);
        Assert.Equal(RefKind.None, declarations[0].Variable.RefKind);
        Assert.Equal(IrDeclaredTypeForm.Deduced, declarations[0].Variable.DeclaredTypeForm);
        AssertNamedInt(declarations[1]);
        var call = Assert.IsType<IrCall>(sequence.Value);
        AssertReads(call.Receiver, declarations[0]);
        AssertReads(call.Arguments[0].Value, declarations[1]);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_InstanceRefReadOnlyLocalCopy_SpillsDeducedCopyBeforeArgument()
    {
        var (_, sequenced) = Run("InstanceRefReadOnlyLocalCopy");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(2, declarations.Length);
        Assert.Equal(RefKind.None, declarations[0].Variable.RefKind);
        Assert.Equal(IrDeclaredTypeForm.Deduced, declarations[0].Variable.DeclaredTypeForm);
        AssertNamedInt(declarations[1]);
        var call = Assert.IsType<IrCall>(sequence.Value);
        AssertReads(call.Receiver, declarations[0]);
        AssertReads(call.Arguments[0].Value, declarations[1]);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Sequence_InstanceRefStructReceiver_SpillsReferenceBeforeArgument()
    {
        var (_, sequenced) = Run("InstanceRefStructReceiver");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(2, declarations.Length);
        Assert.Equal(RefKind.Ref, declarations[0].Variable.RefKind);
        Assert.Equal(IrDeclaredTypeForm.Deduced, declarations[0].Variable.DeclaredTypeForm);
        AssertNamedInt(declarations[1]);
        var call = Assert.IsType<IrCall>(sequence.Value);
        AssertReads(call.Receiver, declarations[0]);
        AssertReads(call.Arguments[0].Value, declarations[1]);

        var (decidedNoSpill, sequencedNoSpill) = Run("InstanceRefStructLocalNoSpill");
        Assert.Same(decidedNoSpill, sequencedNoSpill);
        Assert.Empty(SequencesOf(sequencedNoSpill.Body));
    }

    [Theory]
    [InlineData("InstanceRefReadOnlyConditionalCopy")]
    [InlineData("InstanceMixedRefReadOnlyConditionalCopy")]
    [InlineData("InstanceConstTrueRefConditionalReadOnlyCopy")]
    [Scenario(TestScenario.Boundary)]
    public void Sequence_InstanceRefReadOnlyConditionalReceiver_SpillsCopyBeforeArgument(string name)
    {
        var (_, sequenced) = Run(name);
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(2, declarations.Length);
        Assert.Equal(RefKind.None, declarations[0].Variable.RefKind);
        Assert.Equal(IrDeclaredTypeForm.Deduced, declarations[0].Variable.DeclaredTypeForm);
        AssertNamedInt(declarations[1]);
        var call = Assert.IsType<IrCall>(sequence.Value);
        AssertReads(call.Receiver, declarations[0]);
        AssertReads(call.Arguments[0].Value, declarations[1]);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Sequence_InstanceInInlineArrayElement_SpillsCopyBeforeArgument()
    {
        var (_, sequenced) = Run("InstanceInInlineArrayElementCopy", type: "IrSequenceCases.InlineArrayForms");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(2, declarations.Length);
        Assert.Equal(RefKind.None, declarations[0].Variable.RefKind);
        Assert.Equal(IrDeclaredTypeForm.Deduced, declarations[0].Variable.DeclaredTypeForm);
        AssertNamedInt(declarations[1]);
        var call = Assert.IsType<IrCall>(sequence.Value);
        AssertReads(call.Receiver, declarations[0]);
        AssertReads(call.Arguments[0].Value, declarations[1]);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Sequence_InstanceConstFalseRefConditionalReceiver_SpillsAddressBeforeArgument()
    {
        var (_, sequenced) = Run("InstanceConstFalseRefConditionalAddress");
        var sequence = Assert.Single(SequencesOf(sequenced.Body));
        var declarations = DeclarationsOf(sequence);

        Assert.Equal(2, declarations.Length);
        Assert.Equal(RefKind.Ref, declarations[0].Variable.RefKind);
        Assert.Equal(IrDeclaredTypeForm.Deduced, declarations[0].Variable.DeclaredTypeForm);
        AssertNamedInt(declarations[1]);
        var call = Assert.IsType<IrCall>(sequence.Value);
        AssertReads(call.Receiver, declarations[0]);
        AssertReads(call.Arguments[0].Value, declarations[1]);
    }

    [Theory]
    [InlineData("ParenthesizedInstanceCallOrder")]
    [InlineData("ParenthesizedInstanceCallOrderFirst")]
    [InlineData("ParenthesizedExtensionCallOrder")]
    [Scenario(TestScenario.Boundary)]
    public void Sequence_ParenthesizedMemberAndExtensionCall_SpillsOperandsInOrder(string name)
    {
        var (_, sequenced) = Run(name);
        var sequence = Assert.Single(SequencesOf(sequenced.Body));

        Assert.Equal(2, DeclarationsOf(sequence).Length);
    }

    // The call of <name> in the method
    private static IrCall OuterCall(IrMethod method, string name) =>
        IrNodeChildren.DescendantsAndSelf(method.Body).OfType<IrCall>().First(call => call.Method.Name == name);

    private static void AssertNamedInt(IrLocalDeclaration declaration)
    {
        Assert.Equal(SpecialType.System_Int32, declaration.Variable.Type.SpecialType);
        Assert.Equal(IrDeclaredTypeForm.Named, declaration.Variable.DeclaredTypeForm);
        Assert.Equal(RefKind.None, declaration.Variable.RefKind);
        Assert.Equal(IrVariableKind.Temporary, declaration.Variable.Kind);
    }

    private static void AssertReads(IrExpression? expression, IrLocalDeclaration declaration) =>
        Assert.Same(declaration.Variable, Assert.IsType<IrVariableReference>(expression).Variable);
}
