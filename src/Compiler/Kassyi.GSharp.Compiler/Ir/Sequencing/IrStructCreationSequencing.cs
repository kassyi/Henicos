using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Sequencing;

/// <summary>
/// The sequence of the constructor arguments of a struct creation, by the kind the syntax path writes it as
/// (<see cref="StructCreationClassifier.Classify"/>), as <see cref="IrArgumentSequencing"/> plans it.
/// </summary>
/// <remarks>
/// <para>
/// A constructor function (a factory or a mapped constructor) is a call: its written arguments move into temporaries
/// when they interfere, and each of them when they are written in another order than the parameters. A brace
/// initialization <c>T{args}</c> evaluates its list from left to right, so it needs a sequence only when the arguments
/// are not written one per parameter in parameter order; then every written argument moves into a temporary, a
/// by-reference one as a reference. An unsupported constructor is reported by the syntax path.
/// </para>
/// <para>
/// The member assignments of an object initializer are written by another mechanism and stay as they are; only the
/// constructor arguments are sequenced.
/// </para>
/// </remarks>
internal static class IrStructCreationSequencing
{
    /// <summary>The plan of <paramref name="creation"/>, from its arguments before they are rewritten.</summary>
    public static IrSequencePlan Plan(IrStructCreation creation, IrSequenceContext context)
    {
        if (creation.Constructor is not { } constructor || IrSequenceSupport.SkipsArgumentsOf(creation))
        {
            return IrSequencePlan.None;
        }

        var operands = IrArgumentSequencing.OperandsOf(creation.Arguments, withExtensionReceiver: false);
        var ordinals = IrArgumentSequencing.ParameterOrdinalsOf(creation.Arguments, operands, 0);
        switch (StructCreationClassifier.Classify(creation.Type, constructor, context.Compilation,
                    context.Decisions.Dialect, out _))
        {
            case StructCreationKind.Factory or StructCreationKind.Mapped:
                return IrArgumentSequencing.Plan(creation.Arguments, operands, false, null,
                    !StructCreationClassifier.IsWrittenInParameterOrder(ordinals), isCall: true, context);
            case StructCreationKind.Braces
                when !StructCreationClassifier.IsInParameterOrder(constructor.Parameters.Length, ordinals):
                return IrArgumentSequencing.Plan(creation.Arguments, operands, false, null, spillAll: true,
                    isCall: false, context);
            default:
                return IrSequencePlan.None;
        }
    }

    /// <summary>
    /// The sequence of <paramref name="creation"/> over <paramref name="rewritten"/>, the creation with its arguments
    /// and member assignments rewritten.
    /// </summary>
    public static IrSequence Apply(IrStructCreation creation, IrSequencePlan plan, IrStructCreation rewritten)
    {
        var builder = new IrSequenceBuilder();
        var operands = IrArgumentSequencing.OperandsOf(creation.Arguments, withExtensionReceiver: false);
        var arguments = IrArgumentSequencing.Apply(rewritten.Arguments, operands, plan, builder);
        return builder.Build(rewritten.Update(arguments, rewritten.Initializers), creation);
    }
}
