using Kassyi.GSharp.Compiler.Analysis.Checks;
using Microsoft.CodeAnalysis.Operations;
using Names = Kassyi.GSharp.Compiler.GSharpInternalNames.IntegerSemantics;

namespace Kassyi.GSharp.Compiler.Semantics;

/// <summary>
/// The managed form of an int, long or nint <c>/</c> or <c>%</c> (and <c>/=</c>, <c>%=</c>) that may divide the minimum of
/// the type by -1 (#718): .NET throws <see cref="OverflowException"/> there, while G# gives the GPU result (MinValue,
/// and 0 for the remainder) on every path. The managed path calls a <c>ManagedGpuContext</c> function instead, in
/// kernel bodies and, through the managed version of a device function (#711), in device functions. A division that
/// cannot reach that case (<see cref="DivisionCheckRule.MayDivideMinimumByMinusOne"/>) keeps the C# operator, and so
/// does a division by 0, which .NET throws as C# does.
/// </summary>
internal static class ManagedDivision
{
    /// <summary>
    /// The form of the division or compound assignment <paramref name="node"/> on the managed path; null when it keeps
    /// the C# operator.
    /// </summary>
    public static ManagedDivisionPlan? Plan(SyntaxNode node, SemanticModel model) => node switch
    {
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.DivideExpression) ||
                                           binary.IsKind(SyntaxKind.ModuloExpression) => PlanBinary(binary, model),
        AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.DivideAssignmentExpression) ||
                                                   assignment.IsKind(SyntaxKind.ModuloAssignmentExpression) =>
            PlanCompound(assignment, model),
        _ => null
    };

    /// <summary>
    /// The form of a binary division or remainder (<paramref name="kind"/>) on the managed path, from its operator
    /// properties and operand ranges; null when it keeps the C# operator. Shared by the managed rewriter and the IR
    /// decision pass.
    /// </summary>
    public static ManagedDivisionPlan? PlanBinary(BinaryOperatorKind kind, bool isBuiltIn, bool isChecked,
        bool hasConstantValue, ITypeSymbol? operatorType, IntegerRange? dividend, IntegerRange? divisor)
    {
        if (kind is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder) ||
            !isBuiltIn || isChecked || hasConstantValue ||
            operatorType is not { } opType ||
            IntegerSemantics.Canonical(opType.SpecialType) is not
                (var type and (SpecialType.System_Int32 or SpecialType.System_Int64)) ||
            !MayNeedHelper(type, dividend, divisor))
        {
            return null;
        }

        return DecideBinary(kind == BinaryOperatorKind.Divide, type);
    }

    private static ManagedDivisionPlan? PlanBinary(BinaryExpressionSyntax node, SemanticModel model)
    {
        if (model.GetSafeOperation(node) is not IBinaryOperation operation ||
            operation.Syntax.RawKind != node.RawKind ||
            operation is not { OperatorMethod: null, IsLifted: false, IsChecked: false } ||
            operation.ConstantValue.HasValue ||
            operation.Type is not { } operationType ||
            IntegerSemantics.Canonical(operationType.SpecialType) is not
                (var type and (SpecialType.System_Int32 or SpecialType.System_Int64)))
        {
            return null;
        }

        return PlanBinary(
            operation.OperatorKind,
            isBuiltIn: true,
            isChecked: false,
            hasConstantValue: false,
            operationType,
            OperationRanges.Of(operation.LeftOperand),
            OperationRanges.Of(operation.RightOperand));
    }

    // The operator type of x /= y is the promotion of the target and the value; only an int or long target can hold
    // the minimum of the operator type, so the result needs no narrowing
    private static ManagedDivisionPlan? PlanCompound(AssignmentExpressionSyntax node, SemanticModel model)
    {
        if (model.GetSafeOperation(node) is not ICompoundAssignmentOperation operation ||
            operation.Syntax.RawKind != node.RawKind ||
            operation is not { OperatorMethod: null, IsLifted: false, IsChecked: false } ||
            IntegerSemantics.GetCompoundOperatorType(operation) is not
                (var type and (SpecialType.System_Int32 or SpecialType.System_Int64)) ||
            IntegerSemantics.Canonical(operation.Target.Type?.SpecialType ?? SpecialType.None) != type ||
            !MayNeedHelper(type, OperationRanges.Of(operation.Target), OperationRanges.Of(operation.Value)))
        {
            return null;
        }

        if (operation.Value.Syntax is not ExpressionSyntax value ||
            operation.Target.Syntax is not ExpressionSyntax target)
        {
            return null;
        }

        // C# finds the location, reads the target, then evaluates the value (C# spec §12.21.4). A value that writes the
        // variable of the target (x /= (x = 5)) must not run before that read
        var valueWrites = ExpressionEffects.Of(value, model).Writes;
        ManagedDivisionTarget targetKind;
        var valueWritesTarget = false;
        var repeatableProperty = false;
        var receiverKind = ManagedPropertyReceiver.None;
        ITypeSymbol? receiverType = null;
        ITypeSymbol? propertyType = null;

        if (operation.Target is IPropertyReferenceOperation { Property.ReturnsByRef: false } property)
        {
            targetKind = ManagedDivisionTarget.Property;
            repeatableProperty = IsRepeatableTarget(property, valueWrites, model);
            if (!repeatableProperty)
            {
                // The rewriter writes the getter and the setter over the receiver of a written <receiver>.<name>; a
                // target that is not one (a conditional access is not an assignment target in C# at all) keeps the
                // operator
                if (!property.Arguments.IsEmpty ||
                    property.Syntax is not MemberAccessExpressionSyntax ||
                    property.Instance is not { Type: { } instanceType } receiver ||
                    property.Property is not { GetMethod: not null, SetMethod.IsInitOnly: false })
                {
                    receiverKind = ManagedPropertyReceiver.None;
                }
                else if (instanceType.IsReferenceType)
                {
                    receiverKind = ManagedPropertyReceiver.Object;
                    receiverType = instanceType;
                    propertyType = property.Type;
                }
                else if (instanceType.IsValueType && instanceType.TypeKind != TypeKind.TypeParameter &&
                         IsReferenceable(receiver))
                {
                    receiverKind = ManagedPropertyReceiver.Variable;
                    receiverType = instanceType;
                    propertyType = property.Type;
                }
            }
        }
        else if (!IsReferenceable(operation.Target))
        {
            targetKind = ManagedDivisionTarget.Other;
        }
        else
        {
            valueWritesTarget = valueWrites.Overlaps(ExpressionEffects.RootOf(target, model));
            targetKind = operation.Target is ILocalReferenceOperation { Local.RefKind: RefKind.None } or
                                             IParameterReferenceOperation { Parameter.RefKind: RefKind.None }
                ? ManagedDivisionTarget.ByValueVariable
                : ManagedDivisionTarget.Location;
        }

        var facts = new ManagedCompoundDivisionFacts(
            Divide: operation.OperatorKind == BinaryOperatorKind.Divide,
            Type: type,
            Target: targetKind,
            ValueWritesTarget: valueWritesTarget,
            RepeatableProperty: repeatableProperty,
            Receiver: receiverKind,
            ReceiverType: receiverType,
            PropertyType: propertyType);
        return DecideCompound(facts);
    }

    /// <summary>
    /// Whether the division or remainder in <paramref name="type"/> may divide the minimum of the type by -1.
    /// </summary>
    public static bool MayNeedHelper(SpecialType type, IntegerRange? dividend, IntegerRange? divisor) =>
        type is SpecialType.System_Int32 or SpecialType.System_Int64 &&
        IntegerSemantics.MayDivideMinimumByMinusOne(dividend, divisor, type);

    /// <summary>
    /// The plan of an int or long <c>/</c> or <c>%</c> that may divide the minimum of the type by -1.
    /// </summary>
    public static ManagedDivisionPlan DecideBinary(bool divide, SpecialType type)
    {
        if (type is not (SpecialType.System_Int32 or SpecialType.System_Int64))
        {
            throw new InvalidOperationException($"Type {type} is not an integer type that can divide minimum by -1.");
        }

        var helper = Pick(divide, type, Names.DivideInt32, Names.DivideInt64, Names.RemainderInt32,
            Names.RemainderInt64);
        return new ManagedDivisionPlan(helper, ManagedDivisionForm.Call);
    }

    /// <summary>
    /// The plan of an int or long <c>/=</c> or <c>%=</c> that may divide the minimum of the type by -1; null when the
    /// operator stays.
    /// </summary>
    public static ManagedDivisionPlan? DecideCompound(in ManagedCompoundDivisionFacts facts)
    {
        var helper = Pick(facts.Divide, facts.Type, Names.DivideInt32, Names.DivideInt64, Names.RemainderInt32,
            Names.RemainderInt64);

        switch (facts.Target)
        {
            // A property or indexer: get once, divide, set once, as the compound assignment does. The receiver and
            // the arguments are written twice (for the get and for the set) when they neither write nor throw and the
            // value does not write what they read. Otherwise a property (not an indexer) binds its receiver once: a
            // value type by reference to its variable, a reference type by its value, as C# evaluates the receiver
            // once and reads the property before the value
            case ManagedDivisionTarget.Property:
                if (facts.RepeatableProperty)
                {
                    return new ManagedDivisionPlan(helper, ManagedDivisionForm.Reassign);
                }

                switch (facts.Receiver)
                {
                    case ManagedPropertyReceiver.Object:
                        if (facts.ReceiverType == null || facts.PropertyType == null)
                        {
                            throw new InvalidOperationException(
                                "Property of object requires ReceiverType and PropertyType.");
                        }

                        return new ManagedDivisionPlan(
                            facts.Divide ? Names.DividePropertyStoreOf : Names.RemainderPropertyStoreOf,
                            ManagedDivisionForm.PropertyOfObject,
                            Names.ReadPropertyOf,
                            facts.ReceiverType,
                            facts.PropertyType);

                    case ManagedPropertyReceiver.Variable:
                        if (facts.ReceiverType == null || facts.PropertyType == null)
                        {
                            throw new InvalidOperationException(
                                "Property of variable requires ReceiverType and PropertyType.");
                        }

                        return new ManagedDivisionPlan(
                            facts.Divide ? Names.DividePropertyStore : Names.RemainderPropertyStore,
                            ManagedDivisionForm.PropertyByReference,
                            Names.ReadProperty,
                            facts.ReceiverType,
                            facts.PropertyType);

                    case ManagedPropertyReceiver.None:
                    default:
                        return null;
                }

            case ManagedDivisionTarget.Location:
            case ManagedDivisionTarget.ByValueVariable:
                if (!facts.ValueWritesTarget)
                {
                    var assign = Pick(facts.Divide, facts.Type, Names.DivideAssignInt32, Names.DivideAssignInt64,
                        Names.RemainderAssignInt32, Names.RemainderAssignInt64);
                    return new ManagedDivisionPlan(assign, ManagedDivisionForm.AssignByReference);
                }

                // The value writes the target's variable: a local or a by-value parameter is read as the first
                // argument, before the value; a location in memory is found once and read before the value through a
                // reference
                if (facts.Target == ManagedDivisionTarget.ByValueVariable)
                {
                    return new ManagedDivisionPlan(helper, ManagedDivisionForm.Reassign);
                }

                var store = Pick(facts.Divide, facts.Type, Names.DivideStoreInt32, Names.DivideStoreInt64,
                    Names.RemainderStoreInt32, Names.RemainderStoreInt64);
                var read = facts.Type == SpecialType.System_Int32 ? Names.ReadReferenceInt32 : Names.ReadReferenceInt64;
                return new ManagedDivisionPlan(store, ManagedDivisionForm.ReadFirst, read);

            case ManagedDivisionTarget.Other:
            default:
                return null;
        }
    }

    private static bool IsRepeatableTarget(IPropertyReferenceOperation property, VariableSet valueWrites,
        SemanticModel model)
    {
        if (property.Instance is { } instance && !IsRepeatable(instance, valueWrites, model))
        {
            return false;
        }

        foreach (var argument in property.Arguments)
        {
            if (!IsRepeatable(argument.Value, valueWrites, model))
            {
                return false;
            }
        }

        return true;
    }

    // A location that ref can take: a local, a parameter, a field, an array element, a ref-returning property, indexer
    // or call
    private static bool IsReferenceable(IOperation target) => target switch
    {
        ILocalReferenceOperation or IParameterReferenceOperation or IArrayElementReferenceOperation => true,
        IFieldReferenceOperation { Field.IsReadOnly: false } => true,
        IPropertyReferenceOperation { Property.ReturnsByRef: true } => true,
        IInvocationOperation { TargetMethod.ReturnsByRef: true } => true,
        _ => false
    };

    // An expression written twice: it writes nothing, throws nothing, and reads nothing that the value writes
    private static bool IsRepeatable(IOperation operation, VariableSet valueWrites, SemanticModel model)
    {
        if (operation.Syntax is not ExpressionSyntax expression)
        {
            return false;
        }

        var effects = ExpressionEffects.Of(expression, model);
        return effects.Writes.IsEmpty &&
               !valueWrites.Overlaps(effects.Reads) &&
               !ExpressionTraps.MayTrap(expression, model);
    }

    private static string Pick(bool divide, SpecialType type, string divide32, string divide64, string remainder32,
        string remainder64) =>
        (divide, type == SpecialType.System_Int32) switch
        {
            (true, true) => divide32,
            (true, false) => divide64,
            (false, true) => remainder32,
            (false, false) => remainder64
        };
}

/// <summary>
/// How the managed path writes a division that may divide the minimum by -1 (<see cref="ManagedDivision"/>).
/// </summary>
internal enum ManagedDivisionForm
{
    /// <summary><c>ManagedGpuContext.&lt;helper&gt;(&lt;left&gt;, &lt;right&gt;)</c>.</summary>
    Call,

    /// <summary>
    /// <c>ManagedGpuContext.&lt;helper&gt;(ref &lt;target&gt;, &lt;value&gt;)</c>, which stores and returns the result.
    /// </summary>
    AssignByReference,

    /// <summary>
    /// <c>&lt;target&gt; = ManagedGpuContext.&lt;helper&gt;(&lt;target&gt;, &lt;value&gt;)</c>, for a property, an
    /// indexer, or a local or by-value parameter whose value writes it.
    /// </summary>
    Reassign,

    /// <summary>
    /// <c>ManagedGpuContext.&lt;helper&gt;(ref ManagedGpuContext.&lt;reader&gt;(ref &lt;target&gt;, out var c), c,
    /// &lt;value&gt;)</c>: a location in memory found once and read before a value that writes it, as
    /// <c>ref var t = ref &lt;target&gt;; t = t / &lt;value&gt;;</c> does.
    /// </summary>
    ReadFirst,

    /// <summary>
    /// <c>ManagedGpuContext.&lt;helper&gt;(ref ManagedGpuContext.&lt;reader&gt;(ref &lt;receiver&gt;, getter,
    /// out var c), c, &lt;value&gt;, setter)</c>: the property of a value type found through its variable once, read
    /// before the value, and set through the same variable.
    /// </summary>
    PropertyByReference,

    /// <summary>
    /// <c>ManagedGpuContext.&lt;helper&gt;(ManagedGpuContext.&lt;reader&gt;(&lt;receiver&gt;, getter, out var c), c,
    /// &lt;value&gt;, setter)</c>: the same for the object a reference type holds when the compound assignment starts.
    /// </summary>
    PropertyOfObject
}

/// <summary>The <c>ManagedGpuContext</c> function of a division and the form it is written in.</summary>
/// <param name="Helper">The name of the function (<c>__gsharp_div_i32</c>, <c>__gsharp_div_assign_i32</c>,
/// ...).</param>
/// <param name="Form">How the call is written.</param>
/// <param name="Reader">
/// The function that reads the target first (the forms <see cref="ManagedDivisionForm.ReadFirst"/>,
/// <see cref="ManagedDivisionForm.PropertyByReference"/> and <see cref="ManagedDivisionForm.PropertyOfObject"/>), or
/// null.
/// </param>
/// <param name="ReceiverType">The type of the receiver of a property that is bound once, or null.</param>
/// <param name="ValueType">The type of that property, or null.</param>
internal readonly record struct ManagedDivisionPlan(
    string Helper,
    ManagedDivisionForm Form,
    string? Reader = null,
    ITypeSymbol? ReceiverType = null,
    ITypeSymbol? ValueType = null);

/// <summary>What the target of a <c>/=</c> or <c>%=</c> is, for <see cref="ManagedDivision.DecideCompound"/>.</summary>
internal enum ManagedDivisionTarget
{
    /// <summary>A target that <c>ref</c> cannot take and that is not a property: the operator stays.</summary>
    Other,

    /// <summary>
    /// A location that <c>ref</c> can take other than a by-value local or parameter: a field that is not read-only, an
    /// array element, a ref local or parameter, a ref-returning property, indexer or call.
    /// </summary>
    Location,

    /// <summary>A local or parameter that is not a reference (<c>RefKind.None</c>).</summary>
    ByValueVariable,

    /// <summary>A property or indexer that does not return by reference.</summary>
    Property
}

/// <summary>
/// How the receiver of a property target is bound once (<see cref="ManagedDivision.DecideCompound"/>).
/// </summary>
internal enum ManagedPropertyReceiver
{
    /// <summary>The property cannot be bound once (an indexer, a conditional access, no getter or an init setter, a
    /// receiver of no type or of a value that ref cannot take): the operator stays.</summary>
    None,

    /// <summary>A reference type: the object it holds when the compound assignment starts.</summary>
    Object,

    /// <summary>A value type that is not a type parameter, through its variable.</summary>
    Variable
}

/// <summary>The facts of a <c>/=</c> or <c>%=</c> that may divide the minimum by -1.</summary>
/// <param name="Divide"><c>/=</c> rather than <c>%=</c>.</param>
/// <param name="Type">The operator type, <see cref="SpecialType.System_Int32"/> or
/// <see cref="SpecialType.System_Int64"/>.</param>
/// <param name="Target">What the target is.</param>
/// <param name="ValueWritesTarget">
/// For a <see cref="ManagedDivisionTarget.Location"/> or <see cref="ManagedDivisionTarget.ByValueVariable"/> target:
/// whether the value writes the variable the target is in (C# reads the target before the value). False otherwise.
/// </param>
/// <param name="RepeatableProperty">
/// For a <see cref="ManagedDivisionTarget.Property"/> target: whether its receiver and arguments write nothing, throw
/// nothing and read nothing the value writes, so they may be written twice. False otherwise.
/// </param>
/// <param name="Receiver">
/// For a property that is not repeatable: how its receiver is bound once. <see cref="ManagedPropertyReceiver.None"/>
/// otherwise.
/// </param>
/// <param name="ReceiverType">The receiver type when <paramref name="Receiver"/> is not None; otherwise null.</param>
/// <param name="PropertyType">The property type when <paramref name="Receiver"/> is not None; otherwise null.</param>
internal readonly record struct ManagedCompoundDivisionFacts(
    bool Divide,
    SpecialType Type,
    ManagedDivisionTarget Target,
    bool ValueWritesTarget,
    bool RepeatableProperty,
    ManagedPropertyReceiver Receiver,
    ITypeSymbol? ReceiverType,
    ITypeSymbol? PropertyType);
