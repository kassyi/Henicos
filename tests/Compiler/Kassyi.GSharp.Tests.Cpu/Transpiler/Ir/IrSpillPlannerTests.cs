#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies the split plan of the IR (#1561): what a statement evaluates before its split points, in the C# order of
/// evaluation, for the shapes the syntax-based lowering evaluates after the split (#1535, #991, #1256) and for the
/// operands that may stay in place.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrSpillPlannerTests
{
    private const string Cases = "Ir/IrSpillPlannerCases.cs";

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("LeftOperandWrittenBeforeShuffle",
        "spill[LeftOperand](a[l] = l + 100) spill[SplitArgument](a[l]) split(Warp.ShuffleSync(0xffffffffu, a[l], 0))")]
    [InlineData("CompoundElementTarget",
        "spill[CompoundRead](a[l]) spill[SplitArgument](Bump(a, l)) " +
        "split(Warp.ShuffleSync(0xffffffffu, Bump(a, l), 0)) " +
        "store(a[l] += Warp.ShuffleSync(0xffffffffu, Bump(a, l), 0))")]
    [InlineData("CompoundLocalTarget",
        "spill[CompoundRead](x) spill[SplitArgument](Inc(ref x)) split(Warp.ShuffleSync(0xffffffffu, Inc(ref x), 0)) " +
        "store(x += Warp.ShuffleSync(0xffffffffu, Inc(ref x), 0))")]
    [InlineData("EarlierArgumentOfACallThatDoesNotSplit", "spill[EarlierOperand](a[0]) split(Suspend())")]
    [InlineData("ArgumentLeftOfAConditionalSplit",
        "spill[SplitArgument](a[0]) cond{split(Suspend())|} split(SuspendWith(a[0], c ? Suspend() : 0))")]
    [InlineData("NamedArgumentsInWrittenOrder",
        "spill[SplitArgument](Inc(ref i)) spill[SplitArgument](i) split(SuspendWith(b: Inc(ref i), a: i))")]
    public void Plan_OperandEvaluatedBeforeTheSplit_IsSpilledInOrder(string kernel, string expected)
    {
        Assert.Equal(expected, PrintLast(kernel));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("TargetIndexThatCanThrow", "spill[TargetPart](t / u) split(Suspend())")]
    [InlineData("StructElementFieldTarget", "spill[TargetPart](b[i]) check(cells[b[i]]) split(Suspend())")]
    [InlineData("RefElementArgumentOfACallThatDoesNotSplit",
        "spill[TargetPart](b[i]) check(a[b[i]]) split(Suspend())")]
    [InlineData("TargetIndexWrittenByTheValue", "spill[TargetPart](t + 1) spill[SplitArgument](Inc(ref t)) " +
                                                "split(SuspendWith(Inc(ref t), 0))")]
    public void Plan_PartOfATargetLocation_IsSpilledBeforeTheSplit(string kernel, string expected)
    {
        Assert.Equal(expected, PrintLast(kernel));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("LocalLeftOperandStays", "split(Warp.ShuffleSync(0xffffffffu, y, 0))")]
    [InlineData("SpanLocalStays", "split(Suspend())")]
    [InlineData("TargetIndexOverLocals", "split(Warp.ActiveMask())")]
    public void Plan_OperandNothingCanChange_StaysInPlace(string kernel, string expected)
    {
        Assert.Equal(expected, PrintLast(kernel));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("SpanElementTarget", "check(s[i]) split(Suspend())")]
    [InlineData("StructMethodOfElement", "check(cells[i]) split(Suspend())")]
    [InlineData("RefElementOfASplit", "check(a[i]) split(SuspendRef(ref a[i], 1))")]
    [InlineData("CheckReadsTheIndexOnce", "spill[TargetPart](b[0]) check(cells[b[0]]) split(Suspend())")]
    public void Plan_ElementCSharpChecksWhereItIsSelected_IsCheckedBeforeTheSplit(string kernel, string expected)
    {
        Assert.Equal(expected, PrintLast(kernel));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ElementTarget", "split(Suspend())")]
    [InlineData("TargetIndexThatCanThrow", "spill[TargetPart](t / u) split(Suspend())")]
    [InlineData("CompoundFieldOfElement",
        "spill[CompoundRead](cells[i].F) split(Suspend()) store(cells[i].F += Suspend())")]
    public void Plan_ElementCSharpChecksAfterTheValue_GetsNoCheck(string kernel, string expected)
    {
        Assert.Equal(expected, PrintLast(kernel));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("CompoundThenReadSameElement",
        "spill[CompoundRead](a[l]) split(Warp.ShuffleSync(0xffffffffu, x, 0)) " +
        "store(a[l] += Warp.ShuffleSync(0xffffffffu, x, 0)) " +
        "spill[EarlierOperand](a[l] += Warp.ShuffleSync(0xffffffffu, x, 0)) spill[EarlierOperand](a[l]) " +
        "split(Warp.ShuffleSync(0xffffffffu, y, 0))")]
    [InlineData("NestedCompound",
        "spill[CompoundRead](a[l]) spill[CompoundRead](b[l]) split(Warp.ShuffleSync(0xffffffffu, x, 0)) " +
        "store(b[l] += Warp.ShuffleSync(0xffffffffu, x, 0)) " +
        "store(a[l] += b[l] += Warp.ShuffleSync(0xffffffffu, x, 0))")]
    public void Plan_CompoundAssignmentWithASplitInItsValue_IsStoredBeforeTheLaterOperands(string kernel,
        string expected)
    {
        Assert.Equal(expected, PrintLast(kernel));
    }

    // Whether an index of a target can be read after the split instead of before it: only when it cannot throw
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("TargetIndexOverLocals", "split(Warp.ActiveMask())")]
    [InlineData("IndexThreadIndex", "split(Suspend())")]
    [InlineData("IndexNumericConversion", "split(Suspend())")]
    [InlineData("IndexEnumConversion", "split(Suspend())")]
    [InlineData("TargetIndexThatCanThrow", "spill[TargetPart](t / u) split(Suspend())")]
    [InlineData("IndexCheckedArithmetic", "spill[TargetPart](t + u) split(Suspend())")]
    [InlineData("IndexNullableConversion", "spill[TargetPart]((int)n) split(Suspend())")]
    [InlineData("IndexUnboxing", "spill[TargetPart]((int)o) split(Suspend())")]
    [InlineData("IndexArrayRead", "spill[TargetPart](idx[0]) split(Suspend())")]
    public void Plan_TargetIndex_StaysOnlyWhenItCannotThrow(string kernel, string expected)
    {
        Assert.Equal(expected, PrintLast(kernel));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ReadOnlyReceiverOfAReadOnlyMember_IsPlanned()
    {
        Assert.Equal("split(Suspend())", PrintLast("ReadOnlyReceiverOfAReadOnlyMethod"));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Plan_StructValueAsTheReceiverOfATargetField_LeavesTheStatementToTheSyntaxPasses()
    {
        // C# never makes a field of a struct value a location; the IR is changed by hand to hold one
        var compilation = IrTestCases.Compile(Cases);
        var method = IrClassifier.Classify(
            IrTestCases.Build(Cases, "IrSpillPlannerCases.Cases", "StructElementFieldTarget"), compilation);
        var changed = new StructReceiverAsValue().RewriteMethod(method);

        var last = IrSpillPlanner.Plan(changed, compilation).Statements[^1];

        Assert.Equal(IrSpillFallback.StructValueReceiverTarget, last.Fallback);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_LaterDeclarator_IsPlannedAfterTheEarlierOne()
    {
        var plan = Plan("LaterDeclarator");

        var planned = Assert.Single(plan.Statements);
        Assert.Equal("q", Assert.IsType<Kassyi.GSharp.Compiler.Ir.Statements.IrLocalDeclaration>(planned.Statement)
            .Variable.Name);
        Assert.Equal("cond{split(Suspend())}", Print(planned.Steps));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_OperandBeforeASplitInsideAConditional_IsMarkedAsForcedByTheConditional()
    {
        var steps = Plan("LeftOperandOfAConditionalSplit").Statements[^1].Steps;

        Assert.Equal("spill[LeftOperand](a[0]) cond{split(Suspend())|}", Print(steps));
        Assert.True(Assert.IsType<IrSpillValue>(steps[0]).ForcedByConditional);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_OperandBeforeAnUnconditionalSplitAndAConditionalOne_IsNotMarkedAsForced()
    {
        var steps = Plan("EarlierOperandBeforeAnUnconditionalSplit").Statements[^1].Steps;

        Assert.Equal("spill[EarlierOperand](a[0]) split(Warp.ShuffleSync(0xffffffffu, x, 0)) cond{split(Suspend())|}",
            Print(steps));
        Assert.False(Assert.IsType<IrSpillValue>(steps[0]).ForcedByConditional);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("SpanValueBeforeSplit", "RefLikeOperand")]
    [InlineData("SpanLocalWrittenByALaterArgument", "RefLikeOperand")]
    [InlineData("SplitInLoop", "OpaqueHoldsSplit")]
    [InlineData("ReadOnlyReceiverOfAMutatingMethod", "ReadOnlyVariableReceiver")]
    public void Plan_SplitTheIrCannotPlan_LeavesTheStatementToTheSyntaxPasses(string kernel,
        string expected)
    {
        var last = Plan(kernel).Statements[^1];

        Assert.Equal(Enum.Parse<IrSpillFallback>(expected), last.Fallback);
        Assert.Empty(last.Steps);
    }

    private static IrSpillPlan Plan(string kernel)
    {
        var compilation = IrTestCases.Compile(Cases);
        var method = IrTestCases.Build(Cases, "IrSpillPlannerCases.Cases", kernel);
        return IrSpillPlanner.Plan(IrClassifier.Classify(method, compilation), compilation);
    }

    private static string PrintLast(string kernel)
    {
        var last = Plan(kernel).Statements[^1];
        Assert.Equal(IrSpillFallback.None, last.Fallback);
        return Print(last.Steps);
    }

    // spill[<role>](<source>), split(<source>), check(<source>), store(<source>), cond{<branch>|<branch>}
    private static string Print(IEnumerable<IrSpillStep> steps) =>
        string.Join(" ", steps.Select(static step => step switch
        {
            IrSpillValue spill => $"spill[{spill.Role}]({Source(step.Node)})",
            IrSpillSplit => $"split({Source(step.Node)})",
            IrSpillCheck => $"check({Source(step.Node)})",
            IrSpillCompoundStore => $"store({Source(step.Node)})",
            IrSpillConditional conditional =>
                "cond{" + string.Join("|", conditional.Branches.Select(static branch => Print(branch))) + "}",
            _ => throw new InvalidOperationException("Unknown step " + step.GetType().Name)
        }));

    private static string Source(IrExpression node) =>
        node.Source.ToLocation(IrTestCases.Compile(Cases)).SourceTree?.GetText().ToString(node.Source.Span) ?? "";

    // Makes every struct element that is a location a value, as no C# source can
    private sealed class StructReceiverAsValue : IrRewriter
    {
        public override IrExpression VisitExpression(IrExpression expression)
        {
            var visited = base.VisitExpression(expression);
            return visited is IrElementReference { Category: IrCategory.Location, Type.IsValueType: true } element
                ? new IrElementReference(element.Receiver, element.Index, element.Type, IrCategory.Value,
                    element.Source)
                : visited;
        }
    }
}
