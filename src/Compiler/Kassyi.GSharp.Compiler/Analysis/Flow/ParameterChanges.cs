using Microsoft.CodeAnalysis.FlowAnalysis;
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
    /// increment (also of one of its fields), as a <c>ref</c> or <c>out</c> argument, as the receiver of a method or
    /// property accessor that is not <c>readonly</c>, as an element of a deconstruction, or where a reference to it or its
    /// address is taken. A write through a <c>ref</c> field changes the variable it refers to, not the parameter; only
    /// rebinding the field (<c>p.F = ref v</c>) does. A plain assignment of the parameter itself is written by the recorder.
    /// </summary>
    public static bool MayChange(IOperation reference)
    {
        var use = reference;
        while (use.Parent is IFieldReferenceOperation field && field.Instance == use)
        {
            if (field.Field.RefKind != RefKind.None)
            {
                return field.Parent is ISimpleAssignmentOperation { IsRef: true } rebinding && rebinding.Target == field;
            }

            use = field;
        }

        return use.Parent switch
        {
            IAssignmentOperation assignment => assignment.Target == use || assignment is ISimpleAssignmentOperation { IsRef: true },
            IIncrementOrDecrementOperation increment => increment.Target == use,
            IArgumentOperation argument => argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out,
            IInvocationOperation invocation => invocation.Instance == use && MayChangeReceiver(invocation.TargetMethod),
            IPropertyReferenceOperation property => property.Instance == use && MayChangeThroughAccessors(property, property.Property),
            IAddressOfOperation => true,
            ITupleOperation tuple => IsDeconstructionTarget(tuple),
            _ => false
        };
    }

    /// <summary>
    /// Whether the use of the flow capture of a property of a parameter (<c>p.P</c>, the target that the graph captures
    /// before a conditional value) may change the parameter: through the accessor that the use calls.
    /// </summary>
    public static bool MayChangeThroughCapturedProperty(IFlowCaptureReferenceOperation reference, IPropertySymbol property) =>
        MayChangeThroughAccessors(reference, property);

    /// <summary>
    /// Whether the use of the flow capture of a <c>ref</c> field of a parameter (<c>p.Ref</c>) may change the parameter:
    /// only when the field itself is rebound (<c>p.Ref = ref v</c>), not when a value is written through it (#1004 F9).
    /// </summary>
    public static bool MayChangeCapturedRefField(IFlowCaptureReferenceOperation reference) =>
        reference.Parent is ISimpleAssignmentOperation { IsRef: true } assignment && assignment.Target == reference;

    /// <summary>
    /// The parameter that <paramref name="value"/> is, is a field of (<c>p</c>, <c>p.X</c>, <c>p.X.Y</c>), is a direct
    /// <c>ref</c> field of (<c>p.Ref</c>, <c>p.X.Ref</c>) or is the receiver of <paramref name="property"/> of
    /// (<c>p.P</c>, <c>p.X.P</c>); none for a member accessed through a <c>ref</c> field (<c>p.Ref.Y</c>).
    /// </summary>
    public static IParameterSymbol? RootParameter(
        IOperation value,
        out bool isWhole,
        out bool isRefField,
        out IPropertySymbol? property)
    {
        property = null;
        isRefField = false;
        if (value is IPropertyReferenceOperation { Instance: { } receiver } propertyReference)
        {
            property = propertyReference.Property;
            value = receiver;
        }

        isWhole = property == null;
        while (value is IFieldReferenceOperation { Instance: { } instance } field)
        {
            if (field.Field.RefKind != RefKind.None)
            {
                if (!isWhole)
                {
                    return null;
                }

                isRefField = true;
            }

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

    // use (p.P, or its flow capture) = v calls the setter, use += v and use++ the getter and the setter, any other use the
    // getter; a property that returns a reference has no setter (p.Ref = v writes through the getter's reference)
    private static bool MayChangeThroughAccessors(IOperation use, IPropertySymbol property)
    {
        var (getter, setter) = use.Parent switch
        {
            _ when property.RefKind != RefKind.None => (true, false),
            ISimpleAssignmentOperation assignment when assignment.Target == use => (false, true),
            IAssignmentOperation assignment when assignment.Target == use => (true, true),
            IIncrementOrDecrementOperation increment when increment.Target == use => (true, true),
            ITupleOperation tuple when IsDeconstructionTarget(tuple) => (false, true),
            _ => (true, false)
        };

        return (getter && property.GetMethod is { } get && MayChangeReceiver(get)) ||
               (setter && property.SetMethod is { } set && MayChangeReceiver(set));
    }

    // A method that may change the struct it is called on
    private static bool MayChangeReceiver(IMethodSymbol method) =>
        !method.IsStatic && !method.IsReadOnly && method.ContainingType is { IsValueType: true, IsReadOnly: false };
}
