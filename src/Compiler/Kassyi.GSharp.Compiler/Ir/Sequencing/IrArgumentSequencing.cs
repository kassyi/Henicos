using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;

namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// The sequence of the arguments of a call or a constructor, as <see cref="EvaluationOrder.BeginCall"/> writes them:
/// the arguments the caller wrote, in the C# order of evaluation, go into temporaries up to the last one that takes
/// part in an interference (each one, when they are passed in another order than written), then the call reads the
/// temporaries in parameter order.
/// </summary>
/// <remarks>
/// <para>
/// The operands are the receiver of an extension method called on it in the reduced form of a device function that
/// G# emits (the first argument of the static call), then the written arguments by
/// <see cref="IrArgument.EvaluationIndex"/>; default values are not operands. An argument passed as a location is
/// counted by what evaluating the location reads (a call does not write it before the others run), and an argument
/// the call checks for null (a non-nullable array or a struct that holds one, not proven) may throw.
/// </para>
/// <para>
/// A temporary of an argument passed by value is declared as the parameter type, the conversion included; one of a
/// location is a reference. The receiver of an instance call stays in place, so the call is left as written when an
/// argument that would move into a temporary interferes with it.
/// </para>
/// </remarks>
internal static class IrArgumentSequencing
{
    /// <summary>
    /// The plan of the arguments of <paramref name="arguments"/>, from the arguments before they are rewritten: the
    /// operand index of each temporary is a position in <paramref name="operands"/>.
    /// </summary>
    /// <param name="arguments">The arguments in parameter order.</param>
    /// <param name="operands">The positions in <paramref name="arguments"/> of the operands, in C# order.</param>
    /// <param name="hasExtensionReceiver">Whether the first operand is the receiver of a reduced extension
    /// call.</param>
    /// <param name="instanceReceiver">The receiver that C++ evaluates before the arguments, if any.</param>
    /// <param name="spillAll">Whether every operand goes into a temporary (another order than written).</param>
    /// <param name="isCall">Whether the arguments are those of a call rather than of a brace initialization.</param>
    /// <param name="context">The context of the pass.</param>
    public static IrSequencePlan Plan(ImmutableArray<IrArgument> arguments, int[] operands, bool hasExtensionReceiver,
        IrExpression? instanceReceiver, bool spillAll, bool isCall, IrSequenceContext context,
        IrExpression? callReceiver = null, IMethodSymbol? callMethod = null)
    {
        var receiverOffset = callReceiver is not null ? 1 : 0;
        var total = receiverOffset + operands.Length;
        if (total < 2 && !spillAll)
        {
            return IrSequencePlan.None;
        }

        var effects = new ExpressionEffects[total];
        if (callReceiver is not null)
        {
            effects[0] = EffectsOfCallReceiver(callReceiver, callMethod, context);
        }

        for (var index = 0; index < operands.Length; index++)
        {
            var argument = arguments[operands[index]];
            effects[index + receiverOffset] = hasExtensionReceiver && index == 0
                ? EffectsOfReceiver(argument, context)
                : EffectsOfArgument(argument, context);
        }

        var last = spillAll ? total - 1 : OperandEvaluationOrder.LastSpilledOperand(effects);
        if (last < 0 || (instanceReceiver is not null && !spillAll &&
                         InterferesWithReceiver(instanceReceiver, effects, last, context)))
        {
            return IrSequencePlan.None;
        }

        var temps = ImmutableArray.CreateBuilder<IrSequenceTempPlan>();
        for (var index = 0; index <= last; index++)
        {
            if (callReceiver is not null && index == 0)
            {
                if (!effects[0].IsEmpty)
                {
                    temps.Add(new IrSequenceTempPlan(0, KindOfCallReceiver(callReceiver, callMethod, context)));
                }

                continue;
            }

            var argOperandIndex = index - receiverOffset;
            var receiver = hasExtensionReceiver && argOperandIndex == 0;
            if ((spillAll && !receiver) || !effects[index].IsEmpty)
            {
                var argument = arguments[operands[argOperandIndex]];
                temps.Add(new IrSequenceTempPlan(index,
                    receiver ? KindOfReceiver(argument) : KindOfArgument(argument, isCall)));
            }
        }

        return new IrSequencePlan(temps.ToImmutable());
    }

    /// <summary>
    /// <paramref name="rewritten"/> (the arguments after their own sequences) with the planned operands replaced by
    /// reads of temporaries that <paramref name="builder"/> declares in the C# order of evaluation.
    /// </summary>
    public static ImmutableArray<IrArgument> Apply(ImmutableArray<IrArgument> rewritten, int[] operands,
        IrSequencePlan plan, IrSequenceBuilder builder) =>
        Apply(null, rewritten, operands, plan, builder, hasCallReceiver: false).Arguments;

    /// <summary>
    /// <paramref name="rewrittenReceiver"/> and <paramref name="rewritten"/> with the planned operands replaced by
    /// reads of temporaries that <paramref name="builder"/> declares in the C# order of evaluation.
    /// </summary>
    public static (IrExpression? Receiver, ImmutableArray<IrArgument> Arguments) Apply(
        IrExpression? rewrittenReceiver,
        ImmutableArray<IrArgument> rewritten,
        int[] operands,
        IrSequencePlan plan,
        IrSequenceBuilder builder,
        bool hasCallReceiver)
    {
        var receiver = rewrittenReceiver;
        var result = rewritten.ToBuilder();
        var receiverOffset = hasCallReceiver ? 1 : 0;
        foreach (var temp in plan.Temps)
        {
            if (hasCallReceiver && temp.OperandIndex == 0)
            {
                if (receiver is not null)
                {
                    receiver = SpillCallReceiver(receiver, temp.Kind, builder);
                }

                continue;
            }

            var position = operands[temp.OperandIndex - receiverOffset];
            var argument = rewritten[position];
            var value = argument.Value;
            IrExpression read = temp.Kind switch
            {
                IrSequenceTempKind.Reference => builder.SpillReference(value),
                IrSequenceTempKind.ReadOnlyReference => builder.SpillReference(value, RefKind.In),
                IrSequenceTempKind.ConvertedValue => builder.Spill(value, value.Type, RefKind.None,
                    DeclaredFormOf(argument.Parameter.Type), value.Category),
                _ => throw new InvalidOperationException($"An argument has no temporary of kind {temp.Kind}.")
            };
            result[position] = new IrArgument(argument.Parameter, read, argument.Kind, argument.EvaluationIndex);
        }

        return (receiver, result.MoveToImmutable());
    }

    /// <summary>
    /// The positions in <paramref name="arguments"/> of the arguments the caller wrote, the receiver of a reduced
    /// extension call first, in the C# order of evaluation.
    /// </summary>
    /// <param name="arguments">The arguments in parameter order.</param>
    /// <param name="withExtensionReceiver">Whether the receiver of a reduced extension call is an operand.</param>
    public static int[] OperandsOf(ImmutableArray<IrArgument> arguments, bool withExtensionReceiver)
    {
        var count = 0;
        foreach (var argument in arguments)
        {
            if (IsOperand(argument, withExtensionReceiver))
            {
                count++;
            }
        }

        var operands = new int[count];
        var next = 0;
        for (var position = 0; position < arguments.Length; position++)
        {
            if (IsOperand(arguments[position], withExtensionReceiver))
            {
                operands[next++] = position;
            }
        }

        SortByEvaluationIndex(operands, arguments);
        return operands;
    }

    /// <summary>
    /// The parameter of each written argument in <paramref name="operands"/>, counted from
    /// <paramref name="firstParameter"/>.
    /// </summary>
    public static int[] ParameterOrdinalsOf(ImmutableArray<IrArgument> arguments, int[] operands, int firstParameter)
    {
        var ordinals = new int[operands.Length];
        for (var index = 0; index < operands.Length; index++)
        {
            ordinals[index] = arguments[operands[index]].Parameter.Ordinal - firstParameter;
        }

        return ordinals;
    }

    /// <summary>
    /// How the syntax path declares the temporary of a value passed to a parameter of <paramref name="parameterType"/>:
    /// named for a type C++ can spell (an enum included), deduced for a pointer or a type without a direct C++ type.
    /// </summary>
    public static IrDeclaredTypeForm DeclaredFormOf(ITypeSymbol parameterType) =>
        parameterType is not IPointerTypeSymbol && CppTypeMapping.HasDirectCppType(parameterType)
            ? IrDeclaredTypeForm.Named
            : IrDeclaredTypeForm.Deduced;

    private static bool IsOperand(IrArgument argument, bool withExtensionReceiver) => argument.Kind switch
    {
        IrArgumentKind.Explicit => true,
        IrArgumentKind.ExtensionReceiver => withExtensionReceiver,
        _ => false
    };

    // Stable insertion sort by EvaluationIndex; avoids the closure allocation of Array.Sort with a lambda
    private static void SortByEvaluationIndex(int[] operands, ImmutableArray<IrArgument> arguments)
    {
        for (var index = 1; index < operands.Length; index++)
        {
            var current = operands[index];
            var key = arguments[current].EvaluationIndex;
            var previous = index - 1;
            while (previous >= 0 && arguments[operands[previous]].EvaluationIndex > key)
            {
                operands[previous + 1] = operands[previous];
                previous--;
            }

            operands[previous + 1] = current;
        }
    }

    private static bool IsLocation(IrExpression value) =>
        value.Category is IrCategory.Location or IrCategory.ReadOnlyLocation;

    // The receiver of a reduced extension call: its location when it is passed by reference unconverted
    private static ExpressionEffects EffectsOfReceiver(IrArgument receiver, IrSequenceContext context) =>
        receiver.Parameter.RefKind != RefKind.None && IsLocation(receiver.Value)
            ? IrEffects.OfLocation(receiver.Value, context.Compilation, false, IrTrapReading.Decided)
            : IrEffects.Of(receiver.Value, context.Compilation, IrTrapReading.Decided);

    // The receiver of an emitted struct instance device function: a value when copied or a temporary, otherwise its
    // location.
    private static ExpressionEffects EffectsOfCallReceiver(IrExpression receiver, IMethodSymbol? method,
        IrSequenceContext context) =>
        IsLocation(receiver) && !IrReadOnlyReceivers.IsCopiedBeforeTheCall(receiver, method, context.Decisions.Method)
            ? IrEffects.OfLocation(receiver, context.Compilation, false, IrTrapReading.Decided)
            : IrEffects.Of(receiver, context.Compilation, IrTrapReading.Decided);

    private static IrSequenceTempKind KindOfCallReceiver(IrExpression receiver, IMethodSymbol? method,
        IrSequenceContext context)
    {
        if (!IsLocation(receiver) ||
            IrReadOnlyReceivers.IsCopiedBeforeTheCall(receiver, method, context.Decisions.Method))
        {
            return IrSequenceTempKind.ConvertedValue;
        }

        return receiver.Category == IrCategory.ReadOnlyLocation
            ? IrSequenceTempKind.ReadOnlyReference
            : IrSequenceTempKind.Reference;
    }

    private static IrExpression SpillCallReceiver(IrExpression receiver, IrSequenceTempKind kind,
        IrSequenceBuilder builder) =>
        kind switch
        {
            IrSequenceTempKind.Reference => builder.SpillReference(receiver),
            IrSequenceTempKind.ReadOnlyReference => builder.SpillReference(receiver, RefKind.In),
            IrSequenceTempKind.ConvertedValue => builder.Spill(receiver, receiver.Type, RefKind.None,
                IrDeclaredTypeForm.Deduced, receiver.Category),
            _ => throw new InvalidOperationException($"A receiver has no temporary of kind {kind}.")
        };

    // An argument, which may throw for the null check of its parameter
    private static ExpressionEffects EffectsOfArgument(IrArgument argument, IrSequenceContext context)
    {
        var effects = IsLocation(argument.Value)
            ? IrEffects.OfLocation(argument.Value, context.Compilation, false, IrTrapReading.Decided)
            : IrEffects.Of(argument.Value, context.Compilation, IrTrapReading.Decided);
        return effects.WithThrow(ChecksArgument(argument, context.Decisions));
    }

    // Whether the call checks the argument for null: the parameter asks for it and the argument is not proven
    // (ArgumentNullCheck.ChecksArgument), whatever the callee
    private static bool ChecksArgument(IrArgument argument, IrDecisionContext context) =>
        ArgumentNullCheck.GetArgumentCheckKind(argument.Parameter) switch
        {
            ArgumentCheckKind.None => false,
            ArgumentCheckKind.Array => !IrNonNullProof.IsProvenNonNull(argument.Value, context),
            _ => !IrNonNullProof.IsProvenStruct(argument.Value, context)
        };

    // Whether an argument up to last interferes with the receiver, which the sequence would evaluate after it. As in
    // EvaluationOrder.InterferesWithReceiver; no call reaches it yet: the calls the pass sequences with an instance
    // receiver are written by other rules, and the BCL mappings are static
    private static bool InterferesWithReceiver(IrExpression receiver, ExpressionEffects[] effects, int last,
        IrSequenceContext context)
    {
        var receiverEffects = IrEffects.OfLocation(receiver, context.Compilation, false, IrTrapReading.Decided);
        for (var index = 0; index <= last; index++)
        {
            if (!effects[index].IsEmpty && IrSequenceGate.Interferes(receiverEffects, effects[index]))
            {
                return true;
            }
        }

        return false;
    }

    // A receiver passed by reference unconverted, or a stack array, is bound by reference
    private static IrSequenceTempKind KindOfReceiver(IrArgument receiver) =>
        (receiver.Parameter.RefKind != RefKind.None && IsLocation(receiver.Value)) ||
        IrNonNullProof.IsStackArrayReference(receiver.Value)
            ? IrSequenceTempKind.Reference
            : IrSequenceTempKind.ConvertedValue;

    // A location passed to a call is bound by reference; a brace initialization binds each by-reference argument
    private static IrSequenceTempKind KindOfArgument(IrArgument argument, bool isCall)
    {
        if (isCall)
        {
            return argument.Value.Category switch
            {
                IrCategory.Location => IrSequenceTempKind.Reference,
                IrCategory.ReadOnlyLocation => IrSequenceTempKind.ReadOnlyReference,
                _ => IrSequenceTempKind.ConvertedValue
            };
        }

        return argument.Parameter.RefKind != RefKind.None
            ? IrSequenceTempKind.Reference
            : IrSequenceTempKind.ConvertedValue;
    }
}
