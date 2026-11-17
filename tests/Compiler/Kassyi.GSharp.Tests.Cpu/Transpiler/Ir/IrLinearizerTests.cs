#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies <see cref="IrLinearizer"/>: flattening split <c>if</c> and <c>while</c> statements into labels and gotos
/// with the same two-round preorder numbering as the syntax lowering, and stripping top-level parentheses on spilled
/// expression statements.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrLinearizerTests
{
    private const string Cases = "Ir/IrLinearCases.cs";
    private const string CasesType = "IrLinear.IrLinearCases";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Linearize_IfWithBarrierInElse_InlinesTheBranchWithoutBarrier()
    {
        string[] expected =
        [
            "if!goto __gsharp_BranchElse_0",
            "decl",
            "expr",
            "goto __gsharp_BranchEnd_0",
            "__gsharp_BranchElse_0:",
            "split",
            "__gsharp_BranchEnd_0:"
        ];

        Assert.Equal(expected, ShapesOf("ElseBarrier"));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Linearize_IfWithoutBarrier_KeepsTheIf()
    {
        string[] expected = ["if", "split"];

        Assert.Equal(expected, ShapesOf("NoSplitIf"));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Linearize_WhileWithBarrier_FlattensTheLoop()
    {
        string[] expected =
        [
            "decl",
            "__gsharp_WhileStart_0:",
            "if!goto __gsharp_WhileEnd_0",
            "expr",
            "split",
            "expr",
            "__gsharp_WhileContinue_0:",
            "goto __gsharp_WhileStart_0",
            "__gsharp_WhileEnd_0:"
        ];

        Assert.Equal(expected, ShapesOf("WhileBarrier"));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Linearize_NestedIfsAndLoop_NumbersInPreorder()
    {
        string[] expected =
        [
            "decl",
            "if!goto __gsharp_BranchEnd_0",
            "if!goto __gsharp_BranchEnd_1",
            "split",
            "__gsharp_BranchEnd_1:",
            "__gsharp_BranchEnd_0:",
            "__gsharp_WhileStart_0:",
            "if!goto __gsharp_WhileEnd_0",
            "if!goto __gsharp_BranchEnd_2",
            "split",
            "__gsharp_BranchEnd_2:",
            "expr",
            "__gsharp_WhileContinue_0:",
            "goto __gsharp_WhileStart_0",
            "__gsharp_WhileEnd_0:"
        ];

        Assert.Equal(expected, ShapesOf("Numbering"));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Linearize_ShuffleInWhileCondition_PutsTheSpillAfterTheStart()
    {
        string[] expected =
        [
            "__gsharp_WhileStart_0:",
            "split",
            "if!goto __gsharp_WhileEnd_0",
            "expr",
            "__gsharp_WhileContinue_0:",
            "goto __gsharp_WhileStart_0",
            "__gsharp_WhileEnd_0:"
        ];

        var (linear, compilation) = Linearize("ShuffleCondition");
        Assert.Equal(expected, linear.Select(s => Shape(s, compilation)).ToArray());

        var conditionalGoto = Assert.IsType<IrConditionalGoto>(linear[2]);
        var binary = Assert.IsType<IrBinary>(conditionalGoto.Condition);
        var left = Assert.IsType<IrVariableReference>(binary.Left);
        Assert.Equal(IrVariableKind.Temporary, left.Variable.Kind);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Linearize_AndInWhileCondition_NumbersTheConditionIfLast()
    {
        string[] expected =
        [
            "__gsharp_WhileStart_0:",
            "decl",
            "if!goto __gsharp_BranchEnd_1",
            "split",
            "expr",
            "__gsharp_BranchEnd_1:",
            "if!goto __gsharp_WhileEnd_0",
            "if!goto __gsharp_BranchEnd_0",
            "split",
            "__gsharp_BranchEnd_0:",
            "__gsharp_WhileContinue_0:",
            "goto __gsharp_WhileStart_0",
            "__gsharp_WhileEnd_0:"
        ];

        Assert.Equal(expected, ShapesOf("AndCondition"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Linearize_TwoAndConditions_NumbersTheConditionIfsInLoopOrder()
    {
        // The condition ifs take their branch numbers in the second round, in the preorder of their loops.
        string[] expected =
        [
            "__gsharp_WhileStart_0:",
            "decl",
            "if!goto __gsharp_BranchEnd_0",
            "split",
            "expr",
            "__gsharp_BranchEnd_0:",
            "if!goto __gsharp_WhileEnd_0",
            "expr",
            "__gsharp_WhileContinue_0:",
            "goto __gsharp_WhileStart_0",
            "__gsharp_WhileEnd_0:",
            "__gsharp_WhileStart_1:",
            "decl",
            "if!goto __gsharp_BranchEnd_1",
            "split",
            "expr",
            "__gsharp_BranchEnd_1:",
            "if!goto __gsharp_WhileEnd_1",
            "expr",
            "__gsharp_WhileContinue_1:",
            "goto __gsharp_WhileStart_1",
            "__gsharp_WhileEnd_1:"
        ];

        Assert.Equal(expected, ShapesOf("TwoAndConditions"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Linearize_NestedWhiles_NumbersTheOuterLoopFirst()
    {
        string[] expected =
        [
            "decl",
            "__gsharp_WhileStart_0:",
            "if!goto __gsharp_WhileEnd_0",
            "decl",
            "__gsharp_WhileStart_1:",
            "if!goto __gsharp_WhileEnd_1",
            "split",
            "expr",
            "__gsharp_WhileContinue_1:",
            "goto __gsharp_WhileStart_1",
            "__gsharp_WhileEnd_1:",
            "expr",
            "__gsharp_WhileContinue_0:",
            "goto __gsharp_WhileStart_0",
            "__gsharp_WhileEnd_0:"
        ];

        Assert.Equal(expected, ShapesOf("NestedWhiles"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Linearize_ElseIfWithoutSplit_KeepsTheElseIf()
    {
        string[] expected =
        [
            "if!goto __gsharp_BranchElse_0",
            "split",
            "goto __gsharp_BranchEnd_0",
            "__gsharp_BranchElse_0:",
            "if",
            "__gsharp_BranchEnd_0:"
        ];

        Assert.Equal(expected, ShapesOf("ElseIfAfterSplit"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Linearize_SplitInElseIfWithEmptyElse_FlattensBothIfs()
    {
        string[] expected =
        [
            "if!goto __gsharp_BranchElse_0",
            "expr",
            "goto __gsharp_BranchEnd_0",
            "__gsharp_BranchElse_0:",
            "if!goto __gsharp_BranchElse_1",
            "split",
            "goto __gsharp_BranchEnd_1",
            "__gsharp_BranchElse_1:",
            "__gsharp_BranchEnd_1:",
            "__gsharp_BranchEnd_0:"
        ];

        Assert.Equal(expected, ShapesOf("SplitInElseIf"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Linearize_RemovedCallAroundShuffle_KeepsTheIf()
    {
        string[] expected = ["if"];

        var (linear, compilation) = Linearize("RemovedCallAroundShuffle");
        Assert.Equal(expected, linear.Select(s => Shape(s, compilation)).ToArray());
        Assert.Empty(IrInvariants.VerifyLinear(linear, compilation));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Apply_ParenthesizedCompoundAroundShuffle_WritesTheStoreWithoutParentheses()
    {
        var compilation = Compile();
        var classified = IrClassifier.Classify(IrTestCases.Build(Cases, CasesType, "ParenthesizedCompound"),
            compilation);
        var lowered = IrSplitLowering.Apply(classified, compilation, out var failure);
        Assert.NotNull(lowered);
        Assert.Equal(IrSplitLoweringFailureKind.None, failure.Kind);

        var firstBlock = Assert.IsType<IrBlock>(lowered.Body.Statements[0]);
        var expressionStatements = firstBlock.Statements.OfType<IrExpressionStatement>().ToArray();

        var compound = Assert.Single(expressionStatements, static s => s.Expression is IrCompoundAssignment);
        var store = Assert.Single(expressionStatements, static s => s.Expression is IrAssignment);

        Assert.Equal(0, compound.Expression.Parentheses);
        Assert.Equal(0, store.Expression.Parentheses);
    }

    private static Compilation Compile() => IrTestCases.Compile(Cases);

    private static (ImmutableArray<IrStatement> Linear, Compilation Compilation) Linearize(string methodName)
    {
        var compilation = Compile();
        var classified = IrClassifier.Classify(IrTestCases.Build(Cases, CasesType, methodName), compilation);
        var lowered = IrSplitLowering.Apply(classified, compilation, out var failure);
        Assert.NotNull(lowered);
        Assert.Equal(IrSplitLoweringFailureKind.None, failure.Kind);
        return (IrLinearizer.Linearize(lowered, compilation), compilation);
    }

    private static string[] ShapesOf(string methodName)
    {
        var (linear, compilation) = Linearize(methodName);
        return linear.Select(s => Shape(s, compilation)).ToArray();
    }

    private static string Shape(IrStatement statement, Compilation compilation) => statement switch
    {
        IrLabel label => $"{label.Label.Name}:",
        IrGoto @goto => $"goto {@goto.Target.Name}",
        IrConditionalGoto { JumpIfTrue: false } cond => $"if!goto {cond.Target.Name}",
        IrExpressionStatement { Expression: IrCall call } when IrSplitLowering.HoldsSplit(call, compilation) =>
            "split",
        IrLocalDeclaration { Initializer: IrCall call } when IrSplitLowering.HoldsSplit(call, compilation) =>
            "split",
        IrLocalDeclaration => "decl",
        IrExpressionStatement => "expr",
        IrIf => "if",
        IrWhile => "while",
        IrBlock => "block",
        IrReturn => "return",
        _ => throw new InvalidOperationException($"Unexpected statement shape: {statement.GetType().Name}")
    };
}
