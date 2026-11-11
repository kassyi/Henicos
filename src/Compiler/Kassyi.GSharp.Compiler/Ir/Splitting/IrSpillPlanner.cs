using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// Plans the split pass of the CPU paths on a classified IR (<see cref="IrSpillPlan"/>): for each statement that holds
/// a call that splits, what C# evaluates before the split and so must be evaluated before it, into temporaries, in the
/// C# order of evaluation. Decides only; nothing is rewritten.
/// </summary>
/// <remarks>
/// <para>
/// The rule is that of Roslyn's <c>SpillSequenceSpiller</c>: in each list of operands (<see cref="IrOperandLists"/>:
/// a receiver and the arguments in their order of evaluation, a left and a right operand, a target and a value), every
/// operand before the last one that holds a split is evaluated before it ("everything up until the last spill must be
/// spilled entirely"). An operand that holds a split itself is planned first, then spilled, unless its value already
/// is a temporary (a call that splits, a conditional written as an <see langword="if"/>). An operand stays in place
/// when nothing can change it across the split (<see cref="IrStayInPlace"/>). A location (a <see langword="ref"/>,
/// <see langword="out"/> or <see langword="in"/> argument, an assignment target, a struct receiver) is not copied: its
/// parts are (<see cref="IrLocationSpills"/>), and the target of a compound assignment is also read before.
/// </para>
/// <para>
/// A call that splits runs again on every resume, so each of its arguments is evaluated once before it, unless it
/// stays in place against every other argument. A split inside a conditionally evaluated operand (an arm of
/// <c>?:</c>, the right operand of <c>&amp;&amp;</c>, <c>||</c>, <c>??</c>) makes the conditional an
/// <see langword="if"/> after the operands before it are spilled; each branch is planned as a statement of its own.
/// </para>
/// <para>
/// A statement whose split cannot be planned stays on the syntax-based lowering (<see cref="IrSpillFallback"/>).
/// Loops, switches and lambdas are opaque in the IR, so their statements are such fallbacks for now.
/// </para>
/// </remarks>
internal sealed class IrSpillPlanner
{
    private readonly IrSplitMarks _marks;
    private IrSpillFallback _fallback;

    private IrSpillPlanner(Compilation compilation, IrSplitMarks marks)
    {
        Compilation = compilation;
        _marks = marks;
        _fallback = marks.Fallback;
    }

    /// <summary>The compilation of the method.</summary>
    public Compilation Compilation { get; }

    /// <summary>Plans every statement of <paramref name="method"/> that holds a split.</summary>
    /// <param name="method">A method whose calls and opaque nodes are classified.</param>
    /// <param name="compilation">The compilation of the method.</param>
    /// <exception cref="InvalidOperationException">A call or opaque node is not classified.</exception>
    public static IrSpillPlan Plan(IrMethod method, Compilation compilation)
    {
        var plans = ImmutableArray.CreateBuilder<IrStatementSpillPlan>();
        PlanStatement(method.Body, compilation, plans);
        return new IrSpillPlan(plans.ToImmutable());
    }

    /// <summary>Whether a call below <paramref name="node"/>, or the node itself, splits.</summary>
    public bool Holds(IrNode node) => _marks.Holds(node);

    // A block and the branches of an if hold statements of their own; the condition of an if is evaluated before them
    private static void PlanStatement(IrStatement statement, Compilation compilation,
        ImmutableArray<IrStatementSpillPlan>.Builder plans)
    {
        switch (statement)
        {
            case IrBlock block:
                foreach (var inner in block.Statements)
                {
                    PlanStatement(inner, compilation, plans);
                }

                return;
            case IrIf @if:
                PlanExpression(@if, @if.Condition, compilation, plans);
                PlanStatement(@if.Then, compilation, plans);
                if (@if.Else is { } @else)
                {
                    PlanStatement(@else, compilation, plans);
                }

                return;
            case IrOpaqueStatement opaque:
                PlanOpaque(opaque, plans);
                return;
            default:
                PlanExpression(statement, ExpressionOf(statement), compilation, plans);
                return;
        }
    }

    // The one expression of a simple statement
    private static IrExpression? ExpressionOf(IrStatement statement) => statement switch
    {
        IrExpressionStatement expression => expression.Expression,
        IrLocalDeclaration declaration => declaration.Initializer,
        IrReturn @return => @return.Value,
        _ => throw new InvalidOperationException(
            $"The split pass has no case for a statement of kind {statement.GetType().Name}.")
    };

    private static void PlanOpaque(IrOpaqueStatement opaque, ImmutableArray<IrStatementSpillPlan>.Builder plans)
    {
        switch (opaque.Split)
        {
            case IrOpaqueSplit.Unclassified:
                throw new InvalidOperationException("The opaque statement has not been classified.");
            case IrOpaqueSplit.HoldsSplit:
                plans.Add(new IrStatementSpillPlan(opaque, IrSpillFallback.OpaqueHoldsSplit,
                    ImmutableArray<IrSpillStep>.Empty));
                return;
        }
    }

    // The plan of the one expression of a statement, when it holds a split
    private static void PlanExpression(IrStatement statement, IrExpression? expression, Compilation compilation,
        ImmutableArray<IrStatementSpillPlan>.Builder plans)
    {
        if (expression is null)
        {
            return;
        }

        var marks = IrSplitMarks.Of(expression,
            SemanticModelExtensions.GetSyntaxTrees(compilation)[expression.Source.TreeOrdinal]);
        if (!marks.Holds(expression))
        {
            return;
        }

        var planner = new IrSpillPlanner(compilation, marks);
        var steps = ImmutableArray.CreateBuilder<IrSpillStep>();
        if (IsLocation(expression))
        {
            // A ref local or a returned reference that holds a split
            planner.Fail(IrSpillFallback.LocationNotRebuildable);
        }
        else if (planner._fallback == IrSpillFallback.None)
        {
            planner.PlanValue(expression, steps);
        }

        plans.Add(planner._fallback == IrSpillFallback.None
            ? new IrStatementSpillPlan(statement, IrSpillFallback.None, steps.ToImmutable())
            : new IrStatementSpillPlan(statement, planner._fallback, ImmutableArray<IrSpillStep>.Empty));
    }

    // Plans the splits below expression, which is evaluated for its value
    private void PlanValue(IrExpression expression, ImmutableArray<IrSpillStep>.Builder steps)
    {
        if (!_marks.Holds(expression) || _fallback != IrSpillFallback.None)
        {
            return;
        }

        switch (expression)
        {
            case IrCall call:
                PlanCall(call, steps);
                return;
            case IrLogical logical:
                PlanConditional(logical, logical.Left, [logical.Right], steps);
                return;
            case IrConditional conditional:
                PlanConditional(conditional, conditional.Condition, [conditional.WhenTrue, conditional.WhenFalse],
                    steps);
                return;
            case IrCoalesce coalesce:
                PlanConditional(coalesce, coalesce.Value, [coalesce.WhenNull], steps);
                return;
            case IrCompoundAssignment compound:
                PlanCompoundAssignment(compound, steps);
                return;
            case IrAssignment { IsRef: true } or IrAddressOf:
                // A ref assignment or an address whose operand holds a split
                Fail(IrSpillFallback.LocationNotRebuildable);
                return;
        }

        var (operands, role) = IrOperandLists.Of(expression) ?? throw new InvalidOperationException(
            $"The split pass has no case for an expression of kind {expression.GetType().Name}.");
        PlanOperands(operands, role, steps);
    }

    // A call that splits evaluates each argument once before it (it runs again on every resume); any other call
    // evaluates the operands before its last split-holding one before the split
    private void PlanCall(IrCall call, ImmutableArray<IrSpillStep>.Builder steps)
    {
        var operands = IrOperandLists.Of(call.Receiver, call.Arguments);
        if (!call.Classification.Split.IsSplit)
        {
            PlanOperands(operands, IrSpillRole.EarlierOperand, steps);
            return;
        }

        var effects = IrOperandLists.EffectsOf(operands, Compilation);
        var receivers = IrOperandLists.ReceiverCount(call);
        for (var index = 0; index < operands.Length; index++)
        {
            var later = IrOperandLists.Union(effects, index + 1, effects.Length);
            var others = IrOperandLists.Union(effects, 0, index).Union(later);
            var role = index < receivers ? IrSpillRole.SplitReceiver : IrSpillRole.SplitArgument;
            PlanBefore(operands[index], new IrSpillContext(others, later, role, false), steps);
        }

        steps.Add(new IrSpillSplit(call));
    }

    /// <summary>
    /// Plans a list of operands: every operand before the last one that holds a split is evaluated before it, that
    /// one is planned in turn, and the ones after it run after the split, where they stand.
    /// </summary>
    public void PlanOperands(IrExpression[] operands, IrSpillRole role, ImmutableArray<IrSpillStep>.Builder steps)
    {
        var last = Array.FindLastIndex(operands, _marks.Holds);
        if (last < 0)
        {
            return;
        }

        var effects = IrOperandLists.EffectsOf(operands, Compilation);
        for (var index = 0; index < last; index++)
        {
            var rest = IrOperandLists.Union(effects, index + 1, last + 1);
            var forced = IsForcedByConditional(operands, index, last);
            PlanBefore(operands[index], new IrSpillContext(rest, rest, role, forced), steps);
        }

        var holder = operands[last];
        if (IsLocation(holder))
        {
            IrLocationSpills.PlanHoldingSplit(this, holder, steps);
        }
        else
        {
            PlanValue(holder, steps);
        }
    }

    /// <summary>Plans an operand evaluated before a split: a location spills its parts, a value its value.</summary>
    public void PlanBefore(IrExpression operand, in IrSpillContext context, ImmutableArray<IrSpillStep>.Builder steps)
    {
        if (IsLocation(operand))
        {
            IrLocationSpills.PlanParts(this, operand, context.ForParts(), steps);
        }
        else
        {
            PlanValueBefore(operand, context, steps);
        }
    }

    /// <summary>
    /// Plans a value evaluated before a split: its own splits, then the value unless it stays in place.
    /// </summary>
    public void PlanValueBefore(IrExpression operand, in IrSpillContext context,
        ImmutableArray<IrSpillStep>.Builder steps)
    {
        if (IrStayInPlace.WithoutBuiltInImplicitConversions(operand) is IrOpaqueExpression)
        {
            Fail(IrSpillFallback.OpaqueOperand);
            return;
        }

        if (_marks.Holds(operand))
        {
            PlanValue(operand, steps);
            if (!IsTemporary(operand))
            {
                Spill(operand, context, steps);
            }

            return;
        }

        if (!IrStayInPlace.Stays(operand, context.Others, context.Later, Compilation))
        {
            Spill(operand, context, steps);
        }
    }

    /// <summary>Leaves the statement to the syntax-based lowering for <paramref name="reason"/>.</summary>
    public void Fail(IrSpillFallback reason)
    {
        if (_fallback == IrSpillFallback.None)
        {
            _fallback = reason;
        }
    }

    private void Spill(IrExpression operand, in IrSpillContext context, ImmutableArray<IrSpillStep>.Builder steps)
    {
        if (operand.Type.IsRefLikeType)
        {
            Fail(IrSpillFallback.RefLikeOperand);
            return;
        }

        steps.Add(new IrSpillValue(operand, context.Role, context.ForcedByConditional));
    }

    // C# finds the target, reads it, then evaluates the value: with a split in the value, the parts of the target and
    // its value are evaluated before the split
    private void PlanCompoundAssignment(IrCompoundAssignment compound, ImmutableArray<IrSpillStep>.Builder steps)
    {
        var target = compound.Target;
        var value = compound.Value;
        if (!_marks.Holds(value))
        {
            IrLocationSpills.PlanHoldingSplit(this, target, steps);
            return;
        }

        if (_marks.Holds(target))
        {
            // Splits on both sides of the target's read
            Fail(IrSpillFallback.LocationNotRebuildable);
            return;
        }

        var rest = IrStayInPlace.EffectsOf(value, Compilation);
        var context = new IrSpillContext(rest, rest, IrSpillRole.TargetPart, !_marks.HoldsOutsideConditional(value));
        IrLocationSpills.PlanParts(this, target, context, steps);
        if (!IrStayInPlace.Stays(target, rest, rest, Compilation))
        {
            Spill(target, context with { Role = IrSpillRole.CompoundRead }, steps);
        }

        PlanValue(value, steps);
    }

    // The condition (or left operand) is evaluated where it stands; a conditionally evaluated operand that holds a
    // split makes the whole expression an if, each branch planned on its own
    private void PlanConditional(IrExpression conditional, IrExpression first, IrExpression[] branches,
        ImmutableArray<IrSpillStep>.Builder steps)
    {
        var holds = Array.Exists(branches, _marks.Holds);
        if (conditional is IrConditional { IsRef: true } && holds)
        {
            Fail(IrSpillFallback.LocationNotRebuildable);
            return;
        }

        PlanValue(first, steps);
        if (!holds)
        {
            return;
        }

        var planned = ImmutableArray.CreateBuilder<ImmutableArray<IrSpillStep>>(branches.Length);
        foreach (var branch in branches)
        {
            var branchSteps = ImmutableArray.CreateBuilder<IrSpillStep>();
            PlanValue(branch, branchSteps);
            planned.Add(branchSteps.ToImmutable());
        }

        steps.Add(new IrSpillConditional(conditional, planned.MoveToImmutable()));
    }

    // A spill before operands[last] exists only for a split inside a conditional when no operand after it, up to
    // operands[last], holds a split that always runs
    private bool IsForcedByConditional(IrExpression[] operands, int index, int last)
    {
        for (var later = index + 1; later <= last; later++)
        {
            if (_marks.HoldsOutsideConditional(operands[later]))
            {
                return false;
            }
        }

        return true;
    }

    // Whether the value of a split-holding operand is a temporary once it is planned: a call that splits, or a
    // conditional that becomes an if
    private bool IsTemporary(IrExpression operand) =>
        IrStayInPlace.WithoutBuiltInImplicitConversions(operand) switch
        {
            IrCall call => call.Classification.Split.IsSplit,
            IrConditional conditional => _marks.Holds(conditional.WhenTrue) || _marks.Holds(conditional.WhenFalse),
            IrLogical logical => _marks.Holds(logical.Right),
            IrCoalesce coalesce => _marks.Holds(coalesce.WhenNull),
            _ => false
        };

    private static bool IsLocation(IrExpression operand) =>
        operand.Category is IrCategory.Location or IrCategory.ReadOnlyLocation;
}
