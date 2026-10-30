using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Which uses of a by-value parameter may change it, for the parameters that <see cref="LivenessRecorder"/> follows
/// (GSH0039, #965).
/// </summary>
internal static class ParameterChanges
{
    /// <summary>
    /// Whether the use of a parameter (or of its flow capture) may change it: as the target of an assignment or an
    /// increment (also of one of its fields), as a <c>ref</c> or <c>out</c> argument, as the receiver of a method that is
    /// not <c>readonly</c>, as an element of a deconstruction, or where a reference to it or its address is taken. A plain
    /// assignment of the parameter itself is written by the recorder.
    /// </summary>
    public static bool MayChange(IOperation reference)
    {
        var use = reference;
        while (use.Parent is IFieldReferenceOperation field && field.Instance == use)
        {
            use = field;
        }

        return use.Parent switch
        {
            IAssignmentOperation assignment => assignment.Target == use || assignment is ISimpleAssignmentOperation { IsRef: true },
            IIncrementOrDecrementOperation increment => increment.Target == use,
            IArgumentOperation argument => argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out,
            IInvocationOperation invocation => invocation.Instance == use && MayChangeReceiver(invocation.TargetMethod),
            IAddressOfOperation => true,
            ITupleOperation tuple => IsDeconstructionTarget(tuple),
            _ => false
        };
    }

    /// <summary>The parameter that <paramref name="value"/> is or is a field of (<c>p</c>, <c>p.X</c>, <c>p.X.Y</c>).</summary>
    public static IParameterSymbol? RootParameter(IOperation value, out bool isWhole)
    {
        isWhole = true;
        while (value is IFieldReferenceOperation { Instance: { } instance })
        {
            value = instance;
            isWhole = false;
        }

        return (value as IParameterReferenceOperation)?.Parameter;
    }

    /// <summary>An element of the left side of a deconstruction, <c>((a, b), c) = &lt;value&gt;</c>.</summary>
    public static bool IsDeconstructionTarget(ITupleOperation tuple)
    {
        IOperation current = tuple;
        while (current.Parent is ITupleOperation outer)
        {
            current = outer;
        }

        return current.Parent is IDeconstructionAssignmentOperation deconstruction && deconstruction.Target == current;
    }

    // A method that may change the struct it is called on
    private static bool MayChangeReceiver(IMethodSymbol method) =>
        !method.IsStatic && !method.IsReadOnly && method.ContainingType is { IsValueType: true, IsReadOnly: false };
}
