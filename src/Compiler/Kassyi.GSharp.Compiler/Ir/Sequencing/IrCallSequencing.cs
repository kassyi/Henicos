using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;

namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// The sequence of the arguments of a call that the standard call rule or the <c>[BclIntrinsicMap]</c> rule of the
/// syntax path writes (<see cref="CallTargetKind.DeviceFunction"/>, <see cref="CallTargetKind.DialectIntrinsic"/>,
/// <see cref="CallTargetKind.BclIntrinsicMap"/>), as <see cref="IrArgumentSequencing"/> plans it.
/// </summary>
/// <remarks>
/// <para>
/// A call of a device function or an intrinsic passes its arguments in parameter order whatever order they are
/// written in, so only an interference makes a sequence. A mapped BCL method moves every written argument into a
/// temporary when they are not written one per parameter in parameter order, and leaves its receiver out.
/// </para>
/// <para>
/// The reduced call <c>x.M(a)</c> of an extension method, and the instance call <c>s.M(a)</c> of a struct method,
/// that G# emits as device functions are static C++ calls whose receiver is the first operand (except uncopied
/// <see langword="this"/>); the receiver of any other reduced or instance call stays before the arguments. A call with
/// a <see langword="params"/> array, whose written arguments the IR holds as one array, and a call that another rule
/// writes are left as they are (<see cref="IrSequenceSupport.SkipsArgumentsOf"/>).
/// </para>
/// </remarks>
internal static class IrCallSequencing
{
    /// <summary>The plan of <paramref name="call"/>, from its arguments before they are rewritten.</summary>
    public static IrSequencePlan Plan(IrCall call, IrSequenceContext context)
    {
        if (!Covers(call))
        {
            return IrSequencePlan.None;
        }

        var shape = ShapeOf(call, context);

        // A mapped call out of parameter order spills every argument, as BeginCall does. The first parameter is 0 for
        // every mapping today (all static); an extension form would count from its receiver
        var spillAll = shape.IsMapped &&
                       !StructCreationClassifier.IsInParameterOrder(call.Arguments.Length - shape.FirstParameter,
                           IrArgumentSequencing.ParameterOrdinalsOf(call.Arguments, shape.Operands,
                               shape.FirstParameter));
        return IrArgumentSequencing.Plan(call.Arguments, shape.Operands, shape.ExtensionForm, shape.InstanceReceiver,
            spillAll, isCall: true, context, shape.SequencedReceiver, shape.CallMethod);
    }

    /// <summary>
    /// The sequence of <paramref name="call"/> over <paramref name="rewritten"/>, the call with its receiver and
    /// arguments rewritten.
    /// </summary>
    public static IrSequence Apply(IrCall call, IrSequencePlan plan, IrCall rewritten, IrSequenceContext context)
    {
        var shape = ShapeOf(call, context);
        var builder = new IrSequenceBuilder();
        var (receiver, arguments) = IrArgumentSequencing.Apply(rewritten.Receiver, rewritten.Arguments, shape.Operands,
            plan, builder, hasCallReceiver: shape.SequencedReceiver != null);
        return builder.Build(rewritten.Update(receiver, arguments, rewritten.Classification), call);
    }

    // A call whose arguments the syntax path orders in BeginCall: one of the three targets, without a params array
    private static bool Covers(IrCall call) =>
        call.Decided.Target is CallTargetKind.DeviceFunction or CallTargetKind.DialectIntrinsic or
            CallTargetKind.BclIntrinsicMap &&
        !IrSequenceSupport.SkipsArgumentsOf(call);

    private static CallShape ShapeOf(IrCall call, IrSequenceContext context)
    {
        var mapped = call.Decided.Target == CallTargetKind.BclIntrinsicMap;
        var reduced = call.Arguments is [{ Kind: IrArgumentKind.ExtensionReceiver } receiver, ..] ? receiver : default;
        var hasExtension = reduced.Kind == IrArgumentKind.ExtensionReceiver;
        var isEmittedDevice = !mapped &&
                              CppNameResolver.IsEmittedDeviceFunction(call.Method, context.Decisions.NativeDialect);
        var extensionForm = hasExtension && isEmittedDevice;
        var isThisReceiver = call.Receiver is IrVariableReference { Variable.Kind: IrVariableKind.This };
        var isCopiedThis = isThisReceiver &&
                           call.Receiver != null &&
                           IrReadOnlyReceivers.IsCopiedBeforeTheCall(call.Receiver, call.Method,
                               context.Decisions.Method);
        var uncopiedThis = isThisReceiver && !isCopiedThis;
        var sequencedReceiver = !hasExtension && isEmittedDevice && call.Receiver is { } callReceiver && !uncopiedThis
            ? callReceiver
            : null;
        var operands = IrArgumentSequencing.OperandsOf(call.Arguments, extensionForm);

        // The mapped rule passes no receiver; emitted device functions pass their receiver as operand 0 (except
        // uncopied this, which stays in place as __gsharp_this); any other call leaves its receiver before arguments.
        var instanceReceiver = mapped || extensionForm || sequencedReceiver != null || (isEmittedDevice && uncopiedThis)
            ? null
            : call.Receiver ?? (hasExtension ? reduced.Value : null);
        return new CallShape(operands, extensionForm, instanceReceiver, sequencedReceiver, call.Method, mapped,
            hasExtension ? 1 : 0);
    }

    private readonly record struct CallShape(int[] Operands, bool ExtensionForm, IrExpression? InstanceReceiver,
        IrExpression? SequencedReceiver, IMethodSymbol CallMethod, bool IsMapped, int FirstParameter);
}
