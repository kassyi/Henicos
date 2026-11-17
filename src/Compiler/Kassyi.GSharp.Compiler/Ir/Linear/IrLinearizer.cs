using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>
/// Flattens a split-lowered method body (<see cref="IrSplitLoweredBody"/>) into a flat list of statements, labels,
/// and gotos with the same two-round preorder numbering as <see cref="BranchRewriter"/> and
/// <see cref="LoopRewriter"/>.
/// </summary>
internal static class IrLinearizer
{
    /// <summary>
    /// The statements of <paramref name="body"/> as one list of statements, labels and gotos, as the syntax lowering
    /// flattens it.
    /// </summary>
    public static ImmutableArray<IrStatement> Linearize(IrSplitLoweredBody body, Compilation compilation)
    {
        var state = new State(body, compilation);
        state.NumberBody(body.Body);
        state.NumberLoopConditionIfs();
        return state.Emit();
    }

    private sealed class State(IrSplitLoweredBody body, Compilation compilation)
    {
        private readonly Dictionary<IrIf, int> _ifOrdinals = [];
        private readonly Dictionary<IrWhile, int> _whileOrdinals = [];
        private readonly ImmutableArray<IrWhile>.Builder _splitWhiles = ImmutableArray.CreateBuilder<IrWhile>();
        private readonly Dictionary<(IrLabelKind Kind, int Ordinal), IrLabelSymbol> _labels = [];
        private int _branchCounter;
        private int _loopCounter;

        // Round 1: preorder numbering of split-holding ifs and whiles in the body (excluding while condition spills).
        public void NumberBody(IrStatement statement)
        {
            if (!IrSplitLowering.HoldsSplit(statement, compilation))
            {
                return;
            }

            switch (statement)
            {
                case IrBlock block:
                    foreach (var inner in block.Statements)
                    {
                        NumberBody(inner);
                    }

                    break;
                case IrIf @if:
                    _ifOrdinals.Add(@if, _branchCounter++);
                    NumberBody(@if.Then);
                    if (@if.Else is { } @else)
                    {
                        NumberBody(@else);
                    }

                    break;
                case IrWhile @while:
                    _whileOrdinals.Add(@while, _loopCounter++);
                    _splitWhiles.Add(@while);
                    NumberBody(@while.Body);
                    break;
            }
        }

        // Round 2: continue branch numbering over ifs produced from while condition spills, in while preorder.
        public void NumberLoopConditionIfs()
        {
            foreach (var @while in _splitWhiles)
            {
                if (!IrSplitLowering.HoldsSplit(@while.Condition, compilation))
                {
                    continue;
                }

                if (!body.LoopConditions.TryGetValue(@while, out var loweredCondition))
                {
                    throw new InvalidOperationException(
                        "A while loop whose condition holds a split has no lowered condition.");
                }

                foreach (var statement in loweredCondition.Statements)
                {
                    NumberConditionSpills(statement);
                }
            }
        }

        private void NumberConditionSpills(IrStatement statement)
        {
            if (!IrSplitLowering.HoldsSplit(statement, compilation))
            {
                return;
            }

            switch (statement)
            {
                case IrBlock block:
                    foreach (var inner in block.Statements)
                    {
                        NumberConditionSpills(inner);
                    }

                    break;
                case IrIf @if:
                    _ifOrdinals.Add(@if, _branchCounter++);
                    NumberConditionSpills(@if.Then);
                    if (@if.Else is { } @else)
                    {
                        NumberConditionSpills(@else);
                    }

                    break;
            }
        }

        public ImmutableArray<IrStatement> Emit()
        {
            var output = ImmutableArray.CreateBuilder<IrStatement>();
            foreach (var statement in body.Body.Statements)
            {
                EmitStatement(statement, output);
            }

            return output.ToImmutable();
        }

        private void EmitStatement(IrStatement statement, ImmutableArray<IrStatement>.Builder output)
        {
            if (!IrSplitLowering.HoldsSplit(statement, compilation))
            {
                output.Add(statement);
                return;
            }

            switch (statement)
            {
                case IrBlock block:
                    foreach (var inner in block.Statements)
                    {
                        EmitStatement(inner, output);
                    }

                    break;
                case IrIf @if:
                    EmitIf(@if, output);
                    break;
                case IrWhile @while:
                    EmitWhile(@while, output);
                    break;
                case IrExpressionStatement or IrLocalDeclaration or IrReturn:
                    output.Add(statement);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"The linearizer has no rule for a split statement of kind {statement.GetType().Name}.");
            }
        }

        private void EmitIf(IrIf @if, ImmutableArray<IrStatement>.Builder output)
        {
            var ordinal = _ifOrdinals[@if];
            var source = @if.Source.AsSynthesized();
            var endLabel = GetLabel(IrLabelKind.BranchEnd, ordinal);

            if (@if.Else is { } @else)
            {
                var elseLabel = GetLabel(IrLabelKind.BranchElse, ordinal);
                output.Add(new IrConditionalGoto(@if.Condition, jumpIfTrue: false, elseLabel, source,
                    @if.Uniformity));
                EmitInlined(@if.Then, output);
                output.Add(new IrGoto(endLabel, source));
                output.Add(new IrLabel(elseLabel, source));
                EmitInlined(@else, output);
                output.Add(new IrLabel(endLabel, source));
            }
            else
            {
                output.Add(new IrConditionalGoto(@if.Condition, jumpIfTrue: false, endLabel, source,
                    @if.Uniformity));
                EmitInlined(@if.Then, output);
                output.Add(new IrLabel(endLabel, source));
            }
        }

        private void EmitWhile(IrWhile @while, ImmutableArray<IrStatement>.Builder output)
        {
            var ordinal = _whileOrdinals[@while];
            var source = @while.Source.AsSynthesized();
            var startLabel = GetLabel(IrLabelKind.WhileStart, ordinal);
            var continueLabel = GetLabel(IrLabelKind.WhileContinue, ordinal);
            var endLabel = GetLabel(IrLabelKind.WhileEnd, ordinal);

            output.Add(new IrLabel(startLabel, source));

            IrExpression condition;
            if (IrSplitLowering.HoldsSplit(@while.Condition, compilation))
            {
                if (!body.LoopConditions.TryGetValue(@while, out var loweredCondition))
                {
                    throw new InvalidOperationException(
                        "A while loop whose condition holds a split has no lowered condition.");
                }

                foreach (var spillStatement in loweredCondition.Statements)
                {
                    EmitStatement(spillStatement, output);
                }

                condition = loweredCondition.Condition;
            }
            else
            {
                condition = @while.Condition;
            }

            output.Add(new IrConditionalGoto(condition, jumpIfTrue: false, endLabel, source, @while.Uniformity));
            EmitInlined(@while.Body, output);
            output.Add(new IrLabel(continueLabel, source));
            output.Add(new IrGoto(startLabel, source));
            output.Add(new IrLabel(endLabel, source));
        }

        private void EmitInlined(IrStatement statement, ImmutableArray<IrStatement>.Builder output)
        {
            if (statement is IrBlock block)
            {
                foreach (var inner in block.Statements)
                {
                    EmitStatement(inner, output);
                }
            }
            else
            {
                EmitStatement(statement, output);
            }
        }

        private IrLabelSymbol GetLabel(IrLabelKind kind, int ordinal)
        {
            var key = (kind, ordinal);
            if (!_labels.TryGetValue(key, out var symbol))
            {
                symbol = new IrLabelSymbol(kind, ordinal);
                _labels.Add(key, symbol);
            }

            return symbol;
        }
    }
}
