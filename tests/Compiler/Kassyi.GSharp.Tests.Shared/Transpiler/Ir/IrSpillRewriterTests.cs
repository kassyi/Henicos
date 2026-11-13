#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Verifies the rewrite of a split plan on the IR (#1561): the statements written before a statement for each step,
/// what is left of it, the forms that stay on the syntax passes, and the invariants of the output (R1 to R5).
/// </summary>
[Category(TestCategory.Compiler)]
public class IrSpillRewriterTests
{
    private const string Cases = "Ir/IrSpillPlannerCases.cs";
    private const string CasesType = "IrSpillPlannerCases.Cases";

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("LeftOperandWrittenBeforeShuffle", """
        local int $Argument0 = (ref a[l] = (l + 100))
        local int $Argument1 = a[l]
        local int $Warp0 = Warp.ShuffleSync(mask: 4294967295u, var: $Argument1, srcLane: 0, width: default 32)
        (ref result[l] = ($Argument0 + $Warp0))
        """)]
    [InlineData("EarlierArgumentOfACallThatDoesNotSplit", """
        local int $Argument0 = a[0]
        local int $Call0 = Cases.Suspend()
        (ref result[0] = Cases.Pair(a: $Argument0, b: $Call0))
        """)]
    [InlineData("NamedArgumentsInWrittenOrder", """
        local int $Argument0 = Cases.Inc(x: ref i)
        local int $Argument1 = i
        local int $Call0 = Cases.SuspendWith(a: $Argument1, b: $Argument0) eval(b, a)
        (ref result[0] = $Call0)
        """)]
    [InlineData("TargetIndexWrittenByTheValue", """
        local int $Argument0 = (t + 1)
        local int $Argument1 = Cases.Inc(x: ref t)
        local int $Call0 = Cases.SuspendWith(a: $Argument1, b: 0)
        (ref r[$Argument0] = $Call0)
        """)]
    [InlineData("ReturnAfterSplit", """
        local int $Argument0 = a[0]
        local int $Call0 = Cases.Suspend()
        return ($Argument0 + $Call0)
        """)]
    public void Apply_OperandsBeforeASplit_AreDeclaredBeforeWhatIsLeft(string kernel, string expected)
    {
        Assert.Equal(Lf(expected), PrintLast(kernel));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("ArgumentLeftOfAConditionalSplit", """
        local int $Argument0 = a[0]
        local int $Condition0 = default(int)
        if c
        {
        local int $Call0 = Cases.Suspend()
        (ref $Condition0 = $Call0)
        }
        else
        {
        (ref $Condition0 = 0)
        }
        local int $Call1 = Cases.SuspendWith(a: $Argument0, b: $Condition0)
        (ref result[0] = $Call1)
        """)]
    [InlineData("LaterDeclarator", """
        local bool $Condition0 = p
        if $Condition0
        {
        local int $Call0 = Cases.Suspend()
        (ref $Condition0 = ($Call0 > 0))
        }
        local bool q = $Condition0
        """)]
    [InlineData("OrElseDeclarator", """
        local bool $Condition0 = p
        if (!$Condition0)
        {
        local int $Call0 = Cases.Suspend()
        (ref $Condition0 = ($Call0 > 0))
        }
        local bool q = $Condition0
        """)]
    [InlineData("ConditionalBothArmsSplit", """
        local int $Condition0 = default(int)
        if c
        {
        local int $Call0 = Cases.Suspend()
        (ref $Condition0 = $Call0)
        }
        else
        {
        local int $Warp0 = Warp.ShuffleSync(mask: 4294967295u, var: x, srcLane: 0, width: default 32)
        (ref $Condition0 = $Warp0)
        }
        (ref result[0] = $Condition0)
        """)]
    [InlineData("EarlierOperandBeforeAnUnconditionalSplit", """
        local int $Argument0 = a[0]
        local int $Warp0 = Warp.ShuffleSync(mask: 4294967295u, var: x, srcLane: 0, width: default 32)
        local int $Condition0 = default(int)
        if c
        {
        local int $Call0 = Cases.Suspend()
        (ref $Condition0 = $Call0)
        }
        else
        {
        (ref $Condition0 = 0)
        }
        (ref result[0] = Cases.Triple(a: $Argument0, b: $Warp0, c: $Condition0))
        """)]
    public void Apply_ConditionalThatHoldsASplit_IsWrittenAsAnIf(string kernel, string expected)
    {
        Assert.Equal(Lf(expected), PrintLast(kernel));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Apply_SplitInTheConditionOfAnIf_LeavesTheBranchesAsTheyAre()
    {
        var plan = Plan("IfConditionSplit");
        var (condition, inner) = (plan.Statements[0], plan.Statements[1]);

        Assert.True(IrSpillRewriter.Apply(condition).TryGetLowered(out var lowered));

        Assert.Empty(Verify(condition, lowered));
        Assert.Equal("""
            local int $Call0 = Cases.Suspend()
            if ($Call0 > 0)
            {
            (ref result[0] = Cases.Suspend())
            }
            """.ReplaceLineEndings("\n"), Print(lowered));
        var then = Assert.IsType<IrBlock>(Assert.IsType<IrIf>(lowered.Statements[^1]).Then);
        Assert.Same(inner.Statement, then.Statements[0]);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("StructElementFieldTarget", """
        local int $Argument0 = b[i]
        (ref _ = ref cells[$Argument0])
        local int $Call0 = Cases.Suspend()
        (ref (ref cells[$Argument0]).F = $Call0)
        """)]
    [InlineData("SpanElementTarget", """
        (ref _ = ref s[index: i])
        local int $Call0 = Cases.Suspend()
        (ref s[index: i] = $Call0)
        """)]
    [InlineData("RefElementOfASplit", """
        (ref _ = ref a[i])
        Cases.SuspendRef(target: ref a[i], value: 1)
        """)]
    [InlineData("StructMethodOfElement", """
        (ref _ = ref cells[i])
        local int $Call0 = Cases.Suspend()
        (ref result[0] = (ref cells[i]).Add(v: $Call0))
        """)]
    [InlineData("CheckReadsTheIndexOnce", """
        local int $Argument0 = b[0]
        (ref _ = ref cells[$Argument0])
        local int $Call0 = Cases.Suspend()
        (ref (ref cells[$Argument0]).F = $Call0)
        """)]
    public void Apply_LocationCheckedBeforeTheSplit_IsReadIntoADiscard(string kernel, string expected)
    {
        Assert.Equal(Lf(expected), PrintLast(kernel));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("CompoundElementTarget", """
        local int $Argument0 = ref a[l]
        local int $Argument1 = Cases.Bump(data: a, i: l)
        local int $Warp0 = Warp.ShuffleSync(mask: 4294967295u, var: $Argument1, srcLane: 0, width: default 32)
        (ref $Argument0 += $Warp0)
        (ref a[l] = $Argument0)
        """)]
    [InlineData("CompoundLocalTarget", """
        local int $Argument0 = ref x
        local int $Argument1 = Cases.Inc(x: ref x)
        local int $Warp0 = Warp.ShuffleSync(mask: 4294967295u, var: $Argument1, srcLane: 0, width: default 32)
        (ref $Argument0 += $Warp0)
        (ref x = $Argument0)
        """)]
    [InlineData("CompoundThenReadSameElement", """
        local int $Argument0 = ref a[l]
        local int $Warp0 = Warp.ShuffleSync(mask: 4294967295u, var: x, srcLane: 0, width: default 32)
        (ref $Argument0 += $Warp0)
        (ref a[l] = $Argument0)
        local int $Argument1 = a[l]
        local int $Warp1 = Warp.ShuffleSync(mask: 4294967295u, var: y, srcLane: 0, width: default 32)
        (ref r[l] = Cases.Triple(a: $Argument0, b: $Argument1, c: $Warp1))
        """)]
    [InlineData("NestedCompound", """
        local int $Argument0 = ref a[l]
        local int $Argument1 = ref b[l]
        local int $Warp0 = Warp.ShuffleSync(mask: 4294967295u, var: x, srcLane: 0, width: default 32)
        (ref $Argument1 += $Warp0)
        (ref b[l] = $Argument1)
        (ref $Argument0 += $Argument1)
        (ref a[l] = $Argument0)
        """)]
    [InlineData("CompoundFieldOfElement", """
        local int $Argument0 = ref (ref cells[i]).F
        local int $Call0 = Cases.Suspend()
        (ref $Argument0 += $Call0)
        (ref (ref cells[i]).F = $Argument0)
        """)]
    public void Apply_CompoundAssignmentWhoseTargetIsRead_IsStoredAtItsStep(string kernel, string expected)
    {
        Assert.Equal(Lf(expected), PrintLast(kernel));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("BarrierStatement", "Gpu.SyncThreads()")]
    [InlineData("DiscardedShuffle",
        "Warp.ShuffleSync(mask: 4294967295u, var: x, srcLane: 0, width: default 32)")]
    [InlineData("DiscardedSuspend", "local int $Call0 = Cases.Suspend()")]
    [InlineData("ElementTarget", """
        local int $Call0 = Cases.Suspend()
        (ref a[i] = $Call0)
        """)]
    public void Apply_CallThatIsTheWholeStatement_StaysUnlessItsValueIsKept(string kernel, string expected)
    {
        Assert.Equal(Lf(expected), PrintLast(kernel));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("CoalesceRight", "CoalesceHoldsSplit")]
    [InlineData("ExtensionReceiverOfASplit", "SplitExtensionReceiver")]
    [InlineData("UserDefinedConversionBeforeASplit", "UserDefinedImplicitConversionSpill")]
    public void Apply_FormTheSyntaxPassesKeep_IsSkipped(string kernel, string expected)
    {
        var application = IrSpillRewriter.Apply(Plan(kernel).Statements[^1]);

        Assert.Equal(Enum.Parse<IrSpillApplySkip>(expected), application.Skip);
        Assert.False(application.TryGetLowered(out _));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Apply_PlanWithAFallback_Throws()
    {
        var last = Plan("SplitInLoop").Statements[^1];

        Assert.Throws<ArgumentException>(() => IrSpillRewriter.Apply(last));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Apply_EveryPlannedStatementOfTheCases_KeepsTheInvariants()
    {
        var compilation = IrTestCases.Compile(Cases);
        var type = compilation.GetTypeByMetadataName(CasesType) ??
                   throw new InvalidOperationException(CasesType + " not found.");
        var violations = new List<string>();
        var applied = 0;
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>()
                     .Where(static m => m.MethodKind == MethodKind.Ordinary))
        {
            if (IrBuilder.TryBuild(method, compilation, CancellationToken.None) is not { } built)
            {
                continue;
            }

            var plan = IrSpillPlanner.Plan(IrClassifier.Classify(built, compilation), compilation);
            foreach (var statement in plan.Statements.Where(static s => s.Fallback == IrSpillFallback.None))
            {
                if (IrSpillRewriter.Apply(statement).TryGetLowered(out var lowered))
                {
                    applied++;
                    violations.AddRange(IrLoweredInvariants.Verify(statement, lowered, compilation)
                        .Select(v => method.Name + ": " + v));
                }
            }
        }

        Assert.True(applied > 30, $"Only {applied} statements applied.");
        Assert.Empty(violations);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Verify_StoreMovedAfterTheLaterSteps_ReportsR5()
    {
        var (plan, lowered) = Lower("CompoundThenReadSameElement");
        // The two statements of the store, after everything else but what is left of the statement
        var stores = lowered.Statements.Where(static s => s is IrExpressionStatement).Take(2).ToList();
        var others = lowered.Statements.Except(stores).ToList();
        var moved = others.Take(others.Count - 1).Concat(stores).Append(others[^1]).ToImmutableArray();

        var violations = Verify(plan, Rebuild(lowered, moved));

        Assert.Contains(violations, static v => v.StartsWith("R5", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Verify_SpillDeclaredTwice_ReportsR3()
    {
        var (plan, lowered) = Lower("EarlierArgumentOfACallThatDoesNotSplit");
        var doubled = lowered.Statements.Insert(1, lowered.Statements[0]);

        var violations = Verify(plan, Rebuild(lowered, doubled));

        Assert.Contains(violations, static v => v.StartsWith("R3", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Verify_TemporaryReadBeforeItsDeclaration_ReportsR2()
    {
        var (plan, lowered) = Lower("EarlierArgumentOfACallThatDoesNotSplit");
        var reversed = lowered.Statements.Reverse().ToImmutableArray();

        var violations = Verify(plan, Rebuild(lowered, reversed));

        Assert.Contains(violations, static v => v.StartsWith("R2", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Verify_SplitLeftInsideAnExpression_ReportsR1()
    {
        var (plan, lowered) = Lower("LeftOperandWrittenBeforeShuffle");

        var violations = Verify(plan, Rebuild(lowered, [plan.Statement]));

        Assert.Contains(violations, static v => v.StartsWith("R1", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Verify_SequenceInTheOutput_ReportsR4()
    {
        var (plan, lowered) = Lower("EarlierArgumentOfACallThatDoesNotSplit");
        var last = (IrExpressionStatement)lowered.Statements[^1];
        var sequence = new IrSequence([], [], last.Expression, IrCategory.Value, last.Expression.Source);
        var replaced = lowered.Statements.SetItem(lowered.Statements.Length - 1,
            new IrExpressionStatement(sequence, last.Source));

        var violations = Verify(plan, Rebuild(lowered, replaced));

        Assert.Contains(violations, static v => v.StartsWith("R4", StringComparison.Ordinal));
    }

    private static IrSpillPlan Plan(string kernel)
    {
        var compilation = IrTestCases.Compile(Cases);
        var method = IrTestCases.Build(Cases, CasesType, kernel);
        return IrSpillPlanner.Plan(IrClassifier.Classify(method, compilation), compilation);
    }

    private static (IrStatementSpillPlan Plan, IrLoweredStatement Lowered) Lower(string kernel)
    {
        var last = Plan(kernel).Statements[^1];
        Assert.Equal(IrSpillFallback.None, last.Fallback);
        Assert.True(IrSpillRewriter.Apply(last).TryGetLowered(out var lowered));
        return (last, lowered);
    }

    // The output of the last statement of kernel; its invariants hold
    private static string PrintLast(string kernel)
    {
        var (plan, lowered) = Lower(kernel);
        Assert.Empty(Verify(plan, lowered));
        return Print(lowered);
    }

    // The expected text with the line breaks of the printer's output, whatever the checkout's line endings
    private static string Lf(string text) => text.ReplaceLineEndings("\n");

    private static string Print(IrLoweredStatement lowered) =>
        IrPrinter.Print(lowered.Statements, IrPrintOptions.WithoutClassifications).ReplaceLineEndings("\n");

    private static ImmutableArray<string> Verify(IrStatementSpillPlan plan, IrLoweredStatement lowered) =>
        IrLoweredInvariants.Verify(plan, lowered, IrTestCases.Compile(Cases));

    private static IrLoweredStatement Rebuild(IrLoweredStatement lowered, ImmutableArray<IrStatement> statements) =>
        new(lowered.Original, statements, lowered.Temporaries);
}
