#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Sequencing;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>Runs the sequence pass on the methods of the test cases, for the tests of the pass.</summary>
internal static class IrSequenceTestCases
{
    public const string Forms = "Ir/IrSequenceCases.cs";
    public const string FormsType = "IrSequenceCases.Forms";

    /// <summary>The classified and decided body of <paramref name="method"/>.</summary>
    public static IrMethod Decide(Compilation compilation, IrMethod method)
    {
        var classified = IrClassifier.Classify(method, compilation);
        return IrDecisions.Decide(classified, IrDecisionTestContext.Create(compilation, classified));
    }

    /// <summary>The sequence pass over <paramref name="decided"/>.</summary>
    public static IrMethod Sequence(Compilation compilation, IrMethod decided) =>
        IrSequencer.Sequence(decided, new IrSequenceContext(IrDecisionTestContext.Create(compilation, decided)));

    /// <summary>
    /// The decided and the sequenced body of <paramref name="name"/> in <paramref name="testCase"/>; the sequenced one
    /// keeps the invariants of the IR, of the decisions and of the sequences.
    /// </summary>
    public static (IrMethod Decided, IrMethod Sequenced) Run(string name, string testCase = Forms,
        string type = FormsType)
    {
        var compilation = IrTestCases.Compile(testCase);
        var decided = Decide(compilation, IrTestCases.Build(testCase, type, name));
        var sequenced = Sequence(compilation, decided);
        Assert.Empty(IrInvariants.Verify(sequenced, compilation));
        Assert.Empty(IrDecisionInvariants.Verify(sequenced.Body));
        Assert.Empty(IrSequenceInvariants.Verify(sequenced.Body));
        return (decided, sequenced);
    }

    /// <summary>The sequences under <paramref name="root"/> in the order they are spelled.</summary>
    public static IrSequence[] SequencesOf(IrNode root) =>
        IrNodeChildren.DescendantsAndSelf(root).OfType<IrSequence>().ToArray();

    /// <summary>The declarations of <paramref name="sequence"/>.</summary>
    public static IrLocalDeclaration[] DeclarationsOf(IrSequence sequence) =>
        sequence.SideEffects.Cast<IrLocalDeclaration>().ToArray();

    /// <summary>The expression of the expression statement at <paramref name="index"/> of the body.</summary>
    public static IrExpression StatementExpression(IrMethod method, int index = 0) =>
        Assert.IsType<IrExpressionStatement>(IrTestCases.Statement(method, index)).Expression;

    /// <summary>The first node of type <typeparamref name="T"/> under <paramref name="root"/>.</summary>
    public static T First<T>(IrNode root) where T : IrNode =>
        IrNodeChildren.DescendantsAndSelf(root).OfType<T>().First();
}
