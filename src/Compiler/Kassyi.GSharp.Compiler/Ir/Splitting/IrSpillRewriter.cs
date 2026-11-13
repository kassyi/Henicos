using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// Applies the split plan of one statement on the IR (<see cref="IrStatementSpillPlan"/>): writes each step as a
/// statement before it, in the order of the steps, and replaces every node a step evaluated by its temporary in what
/// is left of the statement. The output uses the node kinds of the IR only, no <see cref="IrSequence"/>: the plan
/// already says what goes before the statement and in which order, which is what Roslyn's spill sequences are lifted
/// to.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>A spilled operand: <c>var t = n;</c>, and <c>n</c> reads <c>t</c>
/// afterwards.</description></item>
/// <item><description>A call that splits: <c>var t = c;</c>; the call stays where it is when it is the whole
/// statement and has no value to keep (a call of a <see langword="void"/> method, a warp operation whose value is
/// discarded).</description></item>
/// <item><description>A <c>?:</c>: <c>var t = default; if (cond) { ...; t = a; } else { ...; t = b; }</c>;
/// <c>&amp;&amp;</c> and <c>||</c>: <c>var t = left; if (t) { ...; t = right; }</c> (<c>if (!t)</c> for
/// <c>||</c>).</description></item>
/// <item><description>A check of a location: <c>_ = loc;</c>.</description></item>
/// <item><description>A compound assignment whose target was read: <c>t op= value; target = t;</c>, written where its
/// <see cref="IrSpillCompoundStore"/> step is.</description></item>
/// </list>
/// Every expression a step writes is itself rewritten with the temporaries made before it, so that nothing is
/// evaluated twice. The temporaries of a branch are declared in the branch.
/// </remarks>
internal static class IrSpillRewriter
{
    /// <summary>Rewrites the statement of <paramref name="plan"/>, or says why it stays on the syntax passes.</summary>
    /// <param name="plan">A plan without a fallback.</param>
    /// <exception cref="ArgumentException">The plan has a fallback.</exception>
    public static IrSpillApplication Apply(IrStatementSpillPlan plan)
    {
        if (plan.Fallback != IrSpillFallback.None)
        {
            throw new ArgumentException("A plan with a fallback has no steps to apply.", nameof(plan));
        }

        var skip = IrSpillApplySkips.Of(plan);
        if (skip != IrSpillApplySkip.None)
        {
            return IrSpillApplication.Skipped(skip);
        }

        var writer = new Writer(plan.Statement);
        var statements = ImmutableArray.CreateBuilder<IrStatement>();
        writer.WriteSteps(plan.Steps, statements, null);
        var remaining = writer.Remaining();
        if (remaining is not null)
        {
            statements.Add(remaining);
        }

        return IrSpillApplication.Lowered(new IrLoweredStatement(plan.Statement, statements.ToImmutable(),
            writer.Temporaries.ToImmutable(), remaining));
    }

    // Writes the steps of one statement; holds the temporaries made so far and the nodes they replace
    private sealed class Writer(IrStatement statement)
    {
        private readonly Dictionary<IrExpression, IrVariable> _replaced = [];
        private readonly Dictionary<IrExpression, IrVariable> _compoundReads = [];
        private readonly Substitution _substitution = new();

        public ImmutableArray<IrSpillTemporary>.Builder Temporaries { get; } =
            ImmutableArray.CreateBuilder<IrSpillTemporary>();

        // statements: the list the steps write to; locals: the temporaries declared in a branch, null at the top
        public void WriteSteps(ImmutableArray<IrSpillStep> steps, ImmutableArray<IrStatement>.Builder statements,
            List<IrVariable>? locals)
        {
            foreach (var step in steps)
            {
                switch (step)
                {
                    case IrSpillValue { Role: IrSpillRole.CompoundRead } read:
                        _compoundReads[read.Node] = Declare(read.Node, IrTemporaryKind.Argument, statements, locals);
                        break;
                    case IrSpillValue value when !_replaced.ContainsKey(value.Node):
                        _replaced[value.Node] = Declare(value.Node, IrTemporaryKind.Argument, statements, locals);
                        break;
                    case IrSpillValue:
                        // A compound assignment that its store step already replaced by its temporary
                        break;
                    case IrSpillSplit split:
                        WriteSplit((IrCall)split.Node, statements, locals);
                        break;
                    case IrSpillConditional conditional:
                        WriteConditional(conditional, statements, locals);
                        break;
                    case IrSpillCheck check:
                        statements.Add(Check(check.Node));
                        break;
                    case IrSpillCompoundStore store:
                        WriteStore(store.Compound, statements);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"The split rewriter has no case for {step.GetType().Name}.");
                }
            }
        }

        /// <summary>What is left of the statement, with every replaced node read from its temporary; none when
        /// nothing is left (a call whose value went to a temporary, a compound assignment completed by its
        /// store).</summary>
        public IrStatement? Remaining()
        {
            IrStatement remaining = statement switch
            {
                IrIf @if => @if.Update(Substitute(@if.Condition), @if.Then, @if.Else),
                IrExpressionStatement expression => expression.Update(Substitute(expression.Expression)),
                IrLocalDeclaration declaration => declaration.Update(SubstituteOptional(declaration.Initializer)),
                IrReturn @return => @return.Update(SubstituteOptional(@return.Value)),
                _ => throw new InvalidOperationException(
                    $"The split rewriter has no case for a statement of kind {statement.GetType().Name}.")
            };
            return remaining is IrExpressionStatement { Expression: IrVariableReference reference } &&
                   reference.Variable.Kind == IrVariableKind.Temporary
                ? null
                : remaining;
        }

        private IrExpression Substitute(IrExpression expression) => _substitution.Apply(expression, _replaced);

        private IrExpression? SubstituteOptional(IrExpression? expression) =>
            expression is null ? null : Substitute(expression);

        // var t = <node'>;
        private IrVariable Declare(IrExpression node, IrTemporaryKind kind,
            ImmutableArray<IrStatement>.Builder statements, List<IrVariable>? locals) =>
            Declare(node, Substitute(node), kind, statements, locals);

        private IrVariable Declare(IrExpression origin, IrExpression initializer, IrTemporaryKind kind,
            ImmutableArray<IrStatement>.Builder statements, List<IrVariable>? locals)
        {
            var source = origin.Source.AsSynthesized();
            var temporary = IrVariable.CreateTemporary(origin.Type, RefKind.None, kind.ToString(), source);
            Temporaries.Add(new IrSpillTemporary(temporary, kind, origin));
            locals?.Add(temporary);
            statements.Add(new IrLocalDeclaration(temporary, initializer, source));
            return temporary;
        }

        // A call that is the whole statement and keeps no value stays in place; any other gets a temporary
        private void WriteSplit(IrCall call, ImmutableArray<IrStatement>.Builder statements, List<IrVariable>? locals)
        {
            var split = call.Classification.Split.Kind;
            var isStatement = statement is IrExpressionStatement { Expression: var expression } && expression == call;
            if (isStatement && (call.Method.ReturnsVoid || split != IrSplitKind.SuspendingCall))
            {
                return;
            }

            var kind = split == IrSplitKind.WarpShuffle ? IrTemporaryKind.Warp : IrTemporaryKind.Call;
            _replaced[call] = Declare(call, kind, statements, locals);
        }

        // var t = default; if (<cond'>) { <branch 0>; t = <when true'>; } else { <branch 1>; t = <when false'>; }
        // var t = <left'>; if (t) { <branch>; t = <right'>; }     (if (!t) for ||)
        private void WriteConditional(IrSpillConditional step, ImmutableArray<IrStatement>.Builder statements,
            List<IrVariable>? locals)
        {
            var source = step.Node.Source.AsSynthesized();
            switch (step.Node)
            {
                case IrConditional conditional:
                {
                    var temporary = Declare(conditional, new IrDefaultValue(conditional.Type, IrCategory.Value, source),
                        IrTemporaryKind.Condition, statements, locals);
                    var condition = Substitute(conditional.Condition);
                    var whenTrue = Branch(step.Branches[0], temporary, conditional.WhenTrue);
                    var whenFalse = Branch(step.Branches[1], temporary, conditional.WhenFalse);
                    statements.Add(new IrIf(condition, whenTrue, whenFalse, source));
                    _replaced[conditional] = temporary;
                    return;
                }
                case IrLogical logical:
                {
                    var temporary = Declare(logical, Substitute(logical.Left), IrTemporaryKind.Condition, statements,
                        locals);
                    var read = Read(temporary, source);
                    IrExpression condition = logical.IsAnd
                        ? read
                        : new IrUnary(new IrOperator<UnaryOperatorKind>(UnaryOperatorKind.Not, null, false), read,
                            logical.Type, IrCategory.Value, default, source);
                    statements.Add(new IrIf(condition, Branch(step.Branches[0], temporary, logical.Right), null,
                        source));
                    _replaced[logical] = temporary;
                    return;
                }
                default:
                    throw new InvalidOperationException(
                        $"The split rewriter has no case for a conditional of kind {step.Node.GetType().Name}.");
            }
        }

        // { <steps>; t = <value'>; }, with the temporaries of the steps declared in the block
        private IrBlock Branch(ImmutableArray<IrSpillStep> steps, IrVariable temporary, IrExpression value)
        {
            var statements = ImmutableArray.CreateBuilder<IrStatement>();
            var locals = new List<IrVariable>();
            WriteSteps(steps, statements, locals);
            var source = value.Source.AsSynthesized();
            statements.Add(new IrExpressionStatement(Assign(Write(temporary, source), Substitute(value), source),
                source));
            return new IrBlock(statements.ToImmutable(), locals.ToImmutableArray(), source);
        }

        // _ = <location'>;
        private IrExpressionStatement Check(IrExpression location)
        {
            var source = location.Source.AsSynthesized();
            var discard = new IrDiscard(location.Type, source);
            return new IrExpressionStatement(Assign(discard, Substitute(location), source), source);
        }

        // t op= <value'>; <target'> = t;
        private void WriteStore(IrCompoundAssignment compound, ImmutableArray<IrStatement>.Builder statements)
        {
            if (!_compoundReads.TryGetValue(compound.Target, out var temporary))
            {
                throw new InvalidOperationException("A compound assignment is stored before its target is read.");
            }

            var source = compound.Source.AsSynthesized();
            var apply = compound.Update(Write(temporary, source), Substitute(compound.Value));
            statements.Add(new IrExpressionStatement(apply, source));
            var store = Assign(Substitute(compound.Target), Read(temporary, source), source);
            statements.Add(new IrExpressionStatement(store, source));
            _replaced[compound] = temporary;
        }

        private static IrAssignment Assign(IrExpression target, IrExpression value, IrSource source) =>
            new(target, value, false, target.Type, IrCategory.Value, source);

        private static IrVariableReference Read(IrVariable temporary, IrSource source) =>
            new(temporary, false, IrCategory.Value, default, source);

        private static IrVariableReference Write(IrVariable temporary, IrSource source) =>
            new(temporary, false, IrCategory.Location, default, source);
    }

    // Replaces each node of the table, found by reference, with a read of its temporary
    private sealed class Substitution : IrRewriter
    {
        private Dictionary<IrExpression, IrVariable> _table = [];

        public IrExpression Apply(IrExpression expression, Dictionary<IrExpression, IrVariable> table)
        {
            _table = table;
            return VisitExpression(expression);
        }

        public override IrExpression VisitExpression(IrExpression expression) =>
            _table.TryGetValue(expression, out var temporary)
                ? new IrVariableReference(temporary, false, IrCategory.Value, default,
                    expression.Source.AsSynthesized())
                : base.VisitExpression(expression);
    }
}
