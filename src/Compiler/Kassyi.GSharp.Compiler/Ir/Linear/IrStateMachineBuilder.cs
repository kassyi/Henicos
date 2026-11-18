using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>
/// Partitions a linearized IR method body into numbered <see cref="IrState"/> sections with barriers, warp
/// collectives, and suspending calls matching <see cref="StateLabelPlanner"/> and <see cref="StateMachineLowerer"/>.
/// </summary>
internal static class IrStateMachineBuilder
{
    /// <summary>
    /// The states of the linear statements of a kernel or device function, numbered as <see cref="StateLabelPlanner"/>
    /// and <see cref="StateMachineLowerer"/> number them.
    /// </summary>
    public static IrStateMachine Build(ImmutableArray<IrStatement> linear, bool isDeviceFunction, IrSource body,
        Compilation compilation)
    {
        var labelStates = NumberLabelStates(linear, compilation);
        var states = ImmutableArray.CreateBuilder<IrState>();
        var current = ImmutableArray.CreateBuilder<IrStatement>();
        var callFrames = ImmutableArray.CreateBuilder<IMethodSymbol>();
        var state = 0;
        var warpSiteCount = 0;

        foreach (var statement in linear)
        {
            switch (statement)
            {
                case IrLabel label:
                    current.Add(new IrStateJump(condition: null, jumpIfTrue: false, state + 1, label.Source));
                    CommitState(states, current, ref state);
                    if (!labelStates.TryGetValue(label.Label, out var planned) || planned != state)
                    {
                        throw new InvalidOperationException(
                            $"The label '{label.Label.Name}' starts state {state}, but was planned as {planned}.");
                    }

                    break;
                case IrGoto @goto:
                    current.Add(new IrStateJump(condition: null, jumpIfTrue: false,
                        GetLabelState(labelStates, @goto.Target), @goto.Source));
                    break;
                case IrConditionalGoto conditionalGoto:
                    current.Add(new IrStateJump(conditionalGoto.Condition, conditionalGoto.JumpIfTrue,
                        GetLabelState(labelStates, conditionalGoto.Target), conditionalGoto.Source,
                        conditionalGoto.Uniformity));
                    break;
                default:
                    if (IrSplitLowering.HoldsSplit(statement, compilation))
                    {
                        EmitSplitStatement(statement, states, current, callFrames, ref state, ref warpSiteCount,
                            compilation);
                    }
                    else
                    {
                        current.Add(statement);
                    }

                    break;
            }
        }

        current.Add(new IrStateMachineExit(body.AsSynthesized()));
        CommitState(states, current, ref state);
        return new IrStateMachine(states.ToImmutable(), isDeviceFunction, warpSiteCount, callFrames.ToImmutable(),
            body);
    }

    // Pass 1: match StateLabelPlanner.NumberStates (increment before IrLabel, increment after split statement).
    private static Dictionary<IrLabelSymbol, int> NumberLabelStates(ImmutableArray<IrStatement> linear,
        Compilation compilation)
    {
        var labelStates = new Dictionary<IrLabelSymbol, int>();
        var state = 0;
        foreach (var statement in linear)
        {
            if (statement is IrLabel label)
            {
                state++;
                if (labelStates.ContainsKey(label.Label))
                {
                    throw new InvalidOperationException($"Duplicate label '{label.Label.Name}' in linear form.");
                }

                labelStates.Add(label.Label, state);
            }
            else if (IrSplitLowering.HoldsSplit(statement, compilation))
            {
                state++;
            }
        }

        return labelStates;
    }

    private static int GetLabelState(Dictionary<IrLabelSymbol, int> labelStates, IrLabelSymbol target) =>
        labelStates.TryGetValue(target, out var state)
            ? state
            : throw new InvalidOperationException($"Jump target '{target.Name}' has no state in linear form.");

    private static void CommitState(ImmutableArray<IrState>.Builder states,
        ImmutableArray<IrStatement>.Builder current, ref int state)
    {
        var statements = current.ToImmutable();
        current.Clear();
        states.Add(new IrState(state, statements, unreachableFrom: null, statements[0].Source));
        state++;
    }

    private static void EmitSplitStatement(IrStatement statement, ImmutableArray<IrState>.Builder states,
        ImmutableArray<IrStatement>.Builder current, ImmutableArray<IMethodSymbol>.Builder callFrames, ref int state,
        ref int warpSiteCount, Compilation compilation)
    {
        var (call, variable) = statement switch
        {
            IrExpressionStatement { Expression: IrCall s1Call } => (s1Call, (IrVariable?)null),
            IrLocalDeclaration { Variable: var target, Initializer: IrCall s2Call } => (s2Call, target),
            _ => throw new InvalidOperationException(
                "A split statement in the linear form must be a call statement or local declaration, " +
                $"not {statement.GetType().Name}.")
        };

        var split = call.Classification.Split;
        if (!split.IsSplit)
        {
            throw new InvalidOperationException("A split statement's top call does not split.");
        }

        switch (split.Kind)
        {
            case IrSplitKind.BlockBarrier:
            case IrSplitKind.GridBarrier:
                if (variable is not null)
                {
                    throw new InvalidOperationException("A barrier split statement cannot declare a variable.");
                }

                var suspendKind = split.Kind == IrSplitKind.GridBarrier
                    ? IrSuspendKind.GridBarrier
                    : IrSuspendKind.BlockBarrier;
                current.Add(new IrSuspend(suspendKind, state + 1, call.Source.AsSynthesized()));
                CommitState(states, current, ref state);
                break;
            case IrSplitKind.WarpShuffle:
                EmitWarpShuffle(statement, call, split.Shuffle, states, current, ref state, ref warpSiteCount,
                    compilation);
                break;
            case IrSplitKind.SuspendingCall:
                EmitSuspendingCall(statement, call, variable, states, current, callFrames, ref state);
                break;
            default:
                throw new InvalidOperationException($"Unknown split kind: {split.Kind}.");
        }
    }

    private static void EmitWarpShuffle(IrStatement statement, IrCall call, WarpShuffleKind shuffleKind,
        ImmutableArray<IrState>.Builder states, ImmutableArray<IrStatement>.Builder current, ref int state,
        ref int warpSiteCount, Compilation compilation)
    {
        if (shuffleKind == WarpShuffleKind.None)
        {
            throw new InvalidOperationException("A warp shuffle split has WarpShuffleKind.None.");
        }

        var shape = WarpShuffleShape.ForKind(shuffleKind);
        var site = warpSiteCount++;
        var place = SourcePlace.Format(call.Source.ToLocation(compilation));
        var intType = compilation.GetSpecialType(SpecialType.System_Int32);
        var uintType = compilation.GetSpecialType(SpecialType.System_UInt32);

        var publishArgs = BuildPublishArguments(call, shape, intType, uintType);
        var publishCall = new IrRuntimeCall(IrRuntimeFunction.WarpPublish, shuffleKind, site, publishArgs, place,
            compilation.GetSpecialType(SpecialType.System_Void), call.Source.AsSynthesized());
        current.Add(new IrExpressionStatement(publishCall, call.Source.AsSynthesized()));
        current.Add(new IrSuspend(IrSuspendKind.WarpPublish, state + 1, call.Source.AsSynthesized()));
        CommitState(states, current, ref state);

        var readArgs = BuildReadArguments(call, shape, intType, uintType);
        var readCall = new IrRuntimeCall(IrRuntimeFunction.WarpRead, shuffleKind, site, readArgs, place, call.Type,
            call.Source);
        var rewritten = statement switch
        {
            IrExpressionStatement expressionStatement => (IrStatement)expressionStatement.Update(readCall),
            IrLocalDeclaration declaration => declaration.Update(readCall),
            _ => throw new InvalidOperationException(
                $"Unexpected statement kind for warp shuffle read: {statement.GetType().Name}.")
        };
        current.Add(rewritten);
    }

    private static void EmitSuspendingCall(IrStatement statement, IrCall call, IrVariable? variable,
        ImmutableArray<IrState>.Builder states, ImmutableArray<IrStatement>.Builder current,
        ImmutableArray<IMethodSymbol>.Builder callFrames, ref int state)
    {
        current.Add(new IrStateJump(condition: null, jumpIfTrue: false, state + 1, call.Source.AsSynthesized()));
        CommitState(states, current, ref state);

        var frame = callFrames.Count;
        callFrames.Add(call.Method);

        IrVariableReference? resultTarget = null;
        if (variable is not null)
        {
            if (variable.Kind != IrVariableKind.Temporary)
            {
                throw new InvalidOperationException(
                    $"A suspending call declaration must target a temporary variable, not {variable.Kind}.");
            }

            resultTarget = new IrVariableReference(variable, isDeclaration: true, IrCategory.Location, default,
                call.Source);
        }

        var resume = new IrResumeCall(call, frame, resultTarget, state, call.Source);
        current.Add(IrStatement.WithCommentsBefore(resume, statement.CommentsBefore));
    }

    private static ImmutableArray<IrRuntimeArgument> BuildPublishArguments(IrCall call, WarpShuffleShape shape,
        INamedTypeSymbol intType, INamedTypeSymbol uintType)
    {
        var mask = shape.HasMask
            ? MaskArgument(call, uintType)
            : new IrRuntimeArgument(new IrConstant(0, null, intType, call.Source.AsSynthesized()), RefKind.None);
        var value = shape.HasValue
            ? ArgumentAt(call, WarpShuffleShape.ShuffleValuePosition)
            : new IrRuntimeArgument(new IrConstant(0, null, intType, call.Source.AsSynthesized()), RefKind.None);
        return [mask, value];
    }

    private static ImmutableArray<IrRuntimeArgument> BuildReadArguments(IrCall call, WarpShuffleShape shape,
        INamedTypeSymbol intType, INamedTypeSymbol uintType)
    {
        var builder = ImmutableArray.CreateBuilder<IrRuntimeArgument>(4);
        if (shape.HasMask)
        {
            var argument = call.Arguments[WarpShuffleShape.ShuffleMaskPosition];
            var value = argument.Kind == IrArgumentKind.DefaultValue
                ? new IrConstant(0xFFFFFFFFu, null, uintType, argument.Value.Source.AsSynthesized(),
                    literalText: "0xFFFFFFFFu")
                : ArgumentCloner.Instance.VisitExpression(argument.Value);
            builder.Add(new IrRuntimeArgument(value, argument.Parameter.RefKind));
        }

        if (shape.HasValue)
        {
            var argument = call.Arguments[WarpShuffleShape.ShuffleValuePosition];
            builder.Add(new IrRuntimeArgument(ArgumentCloner.Instance.VisitExpression(argument.Value),
                argument.Parameter.RefKind));
        }

        if (shape.HasLane || shape.PredicateArgument is not null)
        {
            var argument = call.Arguments[WarpShuffleShape.ShuffleLanePosition];
            builder.Add(new IrRuntimeArgument(argument.Value, argument.Parameter.RefKind));
        }

        if (shape.HasWidth)
        {
            var argument = call.Arguments[WarpShuffleShape.ShuffleWidthPosition];
            var value = argument.Kind == IrArgumentKind.DefaultValue
                ? new IrConstant(32, null, intType, argument.Value.Source.AsSynthesized())
                : argument.Value;
            builder.Add(new IrRuntimeArgument(value, argument.Parameter.RefKind));
        }

        return builder.ToImmutable();
    }

    private static IrRuntimeArgument MaskArgument(IrCall call, INamedTypeSymbol uintType)
    {
        var argument = call.Arguments[WarpShuffleShape.ShuffleMaskPosition];
        if (argument.Kind == IrArgumentKind.DefaultValue)
        {
            var literal = new IrConstant(0xFFFFFFFFu, null, uintType, argument.Value.Source.AsSynthesized(),
                literalText: "0xFFFFFFFFu");
            return new IrRuntimeArgument(literal, argument.Parameter.RefKind);
        }

        return new IrRuntimeArgument(argument.Value, argument.Parameter.RefKind);
    }

    private static IrRuntimeArgument ArgumentAt(IrCall call, int position)
    {
        var argument = call.Arguments[position];
        return new IrRuntimeArgument(argument.Value, argument.Parameter.RefKind);
    }

    // Clones mask and value argument subtrees so WarpPublish and WarpRead never share IR node instances.
    private sealed class ArgumentCloner : IrRewriter
    {
        public static ArgumentCloner Instance { get; } = new();

        public override IrExpression VisitExpression(IrExpression expression) => expression switch
        {
            IrConstant constant => IrExpression.WithParentheses(
                new IrConstant(constant.Value, constant.Origin, constant.Type, constant.Source,
                    constant.LiteralText),
                constant.Parentheses),
            IrVariableReference reference => IrExpression.WithParentheses(
                new IrVariableReference(reference.Variable, reference.IsDeclaration, reference.Category,
                    reference.ConstantValue, reference.Source),
                reference.Parentheses),
            IrFieldReference { Receiver: null } field => IrExpression.WithParentheses(
                new IrFieldReference(null, field.Field, field.Category, field.ConstantValue, field.Source),
                field.Parentheses),
            IrDefaultValue or IrDiscard or IrInitializedObject or IrCall or IrOpaqueExpression or IrSequence
                or IrRuntimeCall => throw new InvalidOperationException(
                    $"Cannot clone expression of kind {expression.GetType().Name} in a warp collective argument."),
            _ => base.VisitExpression(expression)
        };
    }
}
