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
/// The rule is that of Roslyn's <c>SpillSequenceSpiller</c>: in each list of operands (a receiver and the arguments in
/// their order of evaluation, a left and a right operand, a target and a value), every operand before the last one
/// that holds a split is evaluated before it ("everything up until the last spill must be spilled entirely"). An
/// operand that holds a split itself is planned first, then spilled, unless its value already is a temporary (a call
/// that splits, a conditional written as an <see langword="if"/>). An operand stays in place when nothing can change
/// it across the split (<see cref="IrStayInPlace"/>). A location (a <see langword="ref"/>, <see langword="out"/> or
/// <see langword="in"/> argument, an assignment target, a struct receiver) is not copied: its parts are (the array and
/// the index of an element, the receiver of a field), and the target of a compound assignment is also read before.
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
    private readonly Compilation _compilation;
    private readonly IrSplitMarks _marks;
    private IrSpillFallback _fallback;

    private IrSpillPlanner(Compilation compilation, IrSplitMarks marks)
    {
        _compilation = compilation;
        _marks = marks;
    }

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
                // The condition is evaluated before the branches, which are statements of their own
                PlanExpression(@if, @if.Condition, compilation, plans);
                PlanStatement(@if.Then, compilation, plans);
                if (@if.Else is { } @else)
                {
                    PlanStatement(@else, compilation, plans);
                }

                return;
            case IrExpressionStatement expression:
                PlanExpression(expression, expression.Expression, compilation, plans);
                return;
            case IrLocalDeclaration declaration:
                PlanExpression(declaration, declaration.Initializer, compilation, plans);
                return;
            case IrReturn @return:
                PlanExpression(@return, @return.Value, compilation, plans);
                return;
            case IrOpaqueStatement opaque:
                if (opaque.Split == IrOpaqueSplit.Unclassified)
                {
                    throw new InvalidOperationException("The opaque statement has not been classified.");
                }

                if (opaque.Split == IrOpaqueSplit.HoldsSplit)
                {
                    plans.Add(new IrStatementSpillPlan(opaque, IrSpillFallback.OpaqueHoldsSplit,
                        ImmutableArray<IrSpillStep>.Empty));
                }

                return;
            default:
                throw new InvalidOperationException(
                    $"The split pass has no case for a statement of kind {statement.GetType().Name}.");
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

        var planner = new IrSpillPlanner(compilation, marks) { _fallback = marks.Fallback };
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
            case IrBinary binary:
                PlanOperands([binary.Left, binary.Right], IrSpillRole.LeftOperand, steps);
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
            case IrUnary unary:
                PlanValue(unary.Operand, steps);
                return;
            case IrConversion conversion:
                PlanValue(conversion.Operand, steps);
                return;
            case IrNullTest test:
                PlanValue(test.Operand, steps);
                return;
            case IrFieldReference { Receiver: { } receiver }:
                PlanOperands([receiver], IrSpillRole.EarlierOperand, steps);
                return;
            case IrElementReference element:
                PlanOperands([element.Receiver, element.Index], IrSpillRole.EarlierOperand, steps);
                return;
            case IrPropertyReference property:
                PlanOperands(OperandsOf(property.Receiver, property.Arguments), IrSpillRole.EarlierOperand, steps);
                return;
            case IrAssignment { IsRef: false } assignment:
                PlanOperands([assignment.Target, assignment.Value], IrSpillRole.TargetPart, steps);
                return;
            case IrCompoundAssignment compound:
                PlanCompoundAssignment(compound, steps);
                return;
            case IrIncrement increment:
                PlanOperands([increment.Target], IrSpillRole.TargetPart, steps);
                return;
            case IrStructCreation creation:
                PlanOperands(OperandsOf(creation), IrSpillRole.EarlierOperand, steps);
                return;
            case IrArrayCreation array:
                PlanOperands([array.Length, .. array.Elements], IrSpillRole.EarlierOperand, steps);
                return;
            case IrAssignment or IrAddressOf:
                // A ref assignment or an address whose operand holds a split
                Fail(IrSpillFallback.LocationNotRebuildable);
                return;
            default:
                throw new InvalidOperationException(
                    $"The split pass has no case for an expression of kind {expression.GetType().Name}.");
        }
    }

    // A call that splits evaluates each argument once before it (it runs again on every resume); any other call
    // evaluates the operands before its last split-holding one before the split
    private void PlanCall(IrCall call, ImmutableArray<IrSpillStep>.Builder steps)
    {
        var operands = OperandsOf(call.Receiver, call.Arguments);
        if (!call.Classification.Split.IsSplit)
        {
            PlanOperands(operands, IrSpillRole.EarlierOperand, steps);
            return;
        }

        var effects = EffectsOf(operands);
        var receivers = ReceiverCount(call, _marks.Tree);
        for (var index = 0; index < operands.Length; index++)
        {
            var others = Union(effects, 0, index).Union(Union(effects, index + 1, effects.Length));
            var later = Union(effects, index + 1, effects.Length);
            var role = index < receivers ? IrSpillRole.SplitReceiver : IrSpillRole.SplitArgument;
            PlanBefore(operands[index], others, later, role, false, steps);
        }

        steps.Add(IrSpillStep.Split(call));
    }

    // Every operand before the last one that holds a split is evaluated before it; that one is planned in turn; the
    // ones after it run after the split, where they stand
    private void PlanOperands(IrExpression[] operands, IrSpillRole role,
        ImmutableArray<IrSpillStep>.Builder steps)
    {
        var last = -1;
        for (var index = 0; index < operands.Length; index++)
        {
            if (_marks.Holds(operands[index]))
            {
                last = index;
            }
        }

        if (last < 0)
        {
            return;
        }

        var forcedByConditional = !_marks.HoldsOutsideConditional(operands[last]);
        var effects = EffectsOf(operands);
        for (var index = 0; index < last; index++)
        {
            var rest = Union(effects, index + 1, last + 1);
            PlanBefore(operands[index], rest, rest, role, forcedByConditional, steps);
        }

        var holder = operands[last];
        if (IsLocation(holder))
        {
            PlanLocationHoldingSplit(holder, steps);
        }
        else
        {
            PlanValue(holder, steps);
        }
    }

    // An operand evaluated before a split: a location spills its parts, a value its value
    private void PlanBefore(IrExpression operand, in ExpressionEffects others, in ExpressionEffects later,
        IrSpillRole role, bool forcedByConditional, ImmutableArray<IrSpillStep>.Builder steps)
    {
        if (IsLocation(operand))
        {
            var partRole = role is IrSpillRole.SplitArgument or IrSpillRole.SplitReceiver
                ? role
                : IrSpillRole.TargetPart;
            PlanLocationParts(operand, others, later, partRole, forcedByConditional, steps);
        }
        else
        {
            PlanValueBefore(operand, others, later, role, forcedByConditional, steps);
        }
    }

    private void PlanValueBefore(IrExpression operand, in ExpressionEffects others, in ExpressionEffects later,
        IrSpillRole role, bool forcedByConditional, ImmutableArray<IrSpillStep>.Builder steps)
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
                Spill(operand, role, forcedByConditional, steps);
            }

            return;
        }

        if (!IrStayInPlace.Stays(operand, others, later, _compilation))
        {
            Spill(operand, role, forcedByConditional, steps);
        }
    }

    private void Spill(IrExpression operand, IrSpillRole role, bool forcedByConditional,
        ImmutableArray<IrSpillStep>.Builder steps)
    {
        if (operand.Type.IsRefLikeType)
        {
            Fail(IrSpillFallback.RefLikeOperand);
            return;
        }

        steps.Add(IrSpillStep.Spill(operand, role, forcedByConditional));
    }

    // The parts of a location evaluated before a split: the array (unless it is a stack array) and the index of an
    // element, the receiver of a field, the receiver and the indices of a span element; a variable has none
    private void PlanLocationParts(IrExpression location, in ExpressionEffects others, in ExpressionEffects later,
        IrSpillRole role, bool forcedByConditional, ImmutableArray<IrSpillStep>.Builder steps)
    {
        switch (location)
        {
            case IrVariableReference or IrDiscard:
                return;
            case IrElementReference element:
                if (!IrStayInPlace.IsStackArray(element.Receiver))
                {
                    PlanValueBefore(element.Receiver, others, later, role, forcedByConditional, steps);
                }

                PlanValueBefore(element.Index, others, later, role, forcedByConditional, steps);
                return;
            case IrFieldReference { Receiver: null }:
                return;
            case IrFieldReference { Receiver: { } receiver }:
                PlanBefore(receiver, others, later, role, forcedByConditional, steps);
                return;
            case IrPropertyReference { Property.IsIndexer: true, Receiver: { Type.IsRefLikeType: true } receiver }
                property:
                PlanBefore(receiver, others, later, role, forcedByConditional, steps);
                foreach (var index in OperandsOf(null, property.Arguments))
                {
                    PlanBefore(index, others, later, role, forcedByConditional, steps);
                }

                return;
            default:
                Fail(IrSpillFallback.LocationNotRebuildable);
                return;
        }
    }

    // A location whose parts hold a split: its parts are operands like any other
    private void PlanLocationHoldingSplit(IrExpression location, ImmutableArray<IrSpillStep>.Builder steps)
    {
        switch (location)
        {
            case IrElementReference element:
                PlanOperands([element.Receiver, element.Index], IrSpillRole.TargetPart, steps);
                return;
            case IrFieldReference { Receiver: { } receiver }:
                PlanOperands([receiver], IrSpillRole.TargetPart, steps);
                return;
            case IrPropertyReference { Property.IsIndexer: true, Receiver.Type.IsRefLikeType: true } property:
                PlanOperands(OperandsOf(property.Receiver, property.Arguments), IrSpillRole.TargetPart, steps);
                return;
            default:
                Fail(IrSpillFallback.LocationNotRebuildable);
                return;
        }
    }

    // C# finds the target, reads it, then evaluates the value: with a split in the value, the parts of the target and
    // its value are evaluated before the split
    private void PlanCompoundAssignment(IrCompoundAssignment compound, ImmutableArray<IrSpillStep>.Builder steps)
    {
        var target = compound.Target;
        var value = compound.Value;
        if (!_marks.Holds(value))
        {
            PlanLocationHoldingSplit(target, steps);
            return;
        }

        if (_marks.Holds(target))
        {
            // Splits on both sides of the target's read
            Fail(IrSpillFallback.LocationNotRebuildable);
            return;
        }

        var forcedByConditional = !_marks.HoldsOutsideConditional(value);
        var rest = IrStayInPlace.EffectsOf(value, _compilation);
        PlanLocationParts(target, rest, rest, IrSpillRole.TargetPart, forcedByConditional, steps);
        if (!IrStayInPlace.Stays(target, rest, rest, _compilation))
        {
            Spill(target, IrSpillRole.CompoundRead, forcedByConditional, steps);
        }

        PlanValue(value, steps);
    }

    // The condition (or left operand) is evaluated where it stands; a conditionally evaluated operand that holds a
    // split makes the whole expression an if, each branch planned on its own
    private void PlanConditional(IrExpression conditional, IrExpression first,
        IrExpression[] branches, ImmutableArray<IrSpillStep>.Builder steps)
    {
        if (conditional is IrConditional { IsRef: true } &&
            branches.AsStackEnumerable().Any(_marks, static (branch, marks) => marks.Holds(branch)))
        {
            Fail(IrSpillFallback.LocationNotRebuildable);
            return;
        }

        PlanValue(first, steps);
        if (!branches.AsStackEnumerable().Any(_marks, static (branch, marks) => marks.Holds(branch)))
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

        steps.Add(IrSpillStep.Conditional(conditional, planned.MoveToImmutable()));
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

    private void Fail(IrSpillFallback reason)
    {
        if (_fallback == IrSpillFallback.None)
        {
            _fallback = reason;
        }
    }

    // How many operands of a call are its receiver: the receiver of an instance method, or the first argument of an
    // extension method written as a call on it ('x.M()'). The IR keeps an extension call in its static form, so the
    // spelling is read from the position of the argument in the call's syntax.
    private static int ReceiverCount(IrCall call, SyntaxTree tree)
    {
        if (call.Receiver is not null)
        {
            return 1;
        }

        if (!call.Method.IsExtensionMethod ||
            tree.GetRoot().FindNode(call.Source.Span, getInnermostNodeForTie: true) is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax access
            })
        {
            return 0;
        }

        return call.Arguments
            .AsStackEnumerable()
            .Any(access.Expression.Span, static (argument, receiverSpan) =>
                argument.Parameter.Ordinal == 0 && receiverSpan.Contains(argument.Value.Source.Span))
            ? 1
            : 0;
    }

    private static bool IsLocation(IrExpression operand) =>
        operand.Category is IrCategory.Location or IrCategory.ReadOnlyLocation;

    // A receiver, then the arguments in the C# order of evaluation
    private static IrExpression[] OperandsOf(IrExpression? receiver, ImmutableArray<IrArgument> arguments)
    {
        var offset = receiver is null ? 0 : 1;
        var operands = new IrExpression[arguments.Length + offset];
        if (receiver is not null)
        {
            operands[0] = receiver;
        }

        // EvaluationIndex numbers the arguments 0..n-1 in the order C# evaluates them
        foreach (var argument in arguments)
        {
            operands[argument.EvaluationIndex + offset] = argument.Value;
        }

        return operands;
    }

    // The arguments of a creation, then the values of its initializer (each target is a member of the new value)
    private static IrExpression[] OperandsOf(IrStructCreation creation)
    {
        var arguments = OperandsOf(null, creation.Arguments);
        var operands = new IrExpression[arguments.Length + creation.Initializers.Length];
        arguments.CopyTo(operands, 0);
        for (var index = 0; index < creation.Initializers.Length; index++)
        {
            operands[arguments.Length + index] = creation.Initializers[index].Value;
        }

        return operands;
    }

    private ImmutableArray<ExpressionEffects> EffectsOf(IrExpression[] operands)
    {
        var effects = ImmutableArray.CreateBuilder<ExpressionEffects>(operands.Length);
        foreach (var operand in operands)
        {
            effects.Add(IrStayInPlace.EffectsOf(operand, _compilation));
        }

        return effects.MoveToImmutable();
    }

    // The union of effects[start..end)
    private static ExpressionEffects Union(ImmutableArray<ExpressionEffects> effects, int start, int end)
    {
        var union = default(ExpressionEffects);
        for (var index = start; index < end; index++)
        {
            union = union.Union(effects[index]);
        }

        return union;
    }
}
