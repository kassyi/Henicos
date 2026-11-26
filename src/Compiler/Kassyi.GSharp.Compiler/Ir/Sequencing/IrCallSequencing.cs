using Kassyi.GSharp.Compiler.Ir.Expressions;

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
/// The reduced call <c>x.M(a)</c> of an extension method that G# emits as a device function is the static call
/// <c>M(x, a)</c>, whose receiver is the first operand; the receiver of any other reduced call, as of an instance call,
/// stays before the arguments. A call with a <see langword="params"/> array, whose written arguments the IR holds as
/// one array, and a call that another rule writes are left as they are
/// (<see cref="IrSequenceSupport.SkipsArgumentsOf"/>).
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
            spillAll, isCall: true, context);
    }

    /// <summary>
    /// The sequence of <paramref name="call"/> over <paramref name="rewritten"/>, the call with its receiver and
    /// arguments rewritten.
    /// </summary>
    public static IrSequence Apply(IrCall call, IrSequencePlan plan, IrCall rewritten, IrSequenceContext context)
    {
        var builder = new IrSequenceBuilder();
        var arguments = IrArgumentSequencing.Apply(rewritten.Arguments, ShapeOf(call, context).Operands, plan,
            builder);
        return builder.Build(rewritten.Update(rewritten.Receiver, arguments, rewritten.Classification), call);
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
        var hasReceiver = reduced.Kind == IrArgumentKind.ExtensionReceiver;
        var extensionForm = hasReceiver && !mapped &&
                            CppNameResolver.IsEmittedDeviceFunction(call.Method, context.Decisions.NativeDialect);
        var operands = IrArgumentSequencing.OperandsOf(call.Arguments, extensionForm);

        // The mapped rule passes no receiver; the standard rule passes the receiver of an instance call or of another
        // reduced call
        var instanceReceiver = mapped || extensionForm
            ? null
            : call.Receiver ?? (hasReceiver ? reduced.Value : null);
        return new CallShape(operands, extensionForm, instanceReceiver, mapped, hasReceiver ? 1 : 0);
    }

    private readonly record struct CallShape(int[] Operands, bool ExtensionForm, IrExpression? InstanceReceiver,
        bool IsMapped, int FirstParameter);
}
