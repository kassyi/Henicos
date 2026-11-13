using Kassyi.GSharp.Compiler.Analysis.Checks;
using Microsoft.CodeAnalysis.Operations;
using Names = Kassyi.GSharp.Compiler.GSharpInternalNames.IntegerSemantics;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The managed form of an int or long <c>/</c> or <c>%</c> (and <c>/=</c>, <c>%=</c>) that may divide the minimum of
/// the type by -1 (#718): .NET throws <see cref="OverflowException"/> there, while G# gives the GPU result (MinValue,
/// and 0 for the remainder) on every path. The managed path calls a <c>ManagedGpuContext</c> function instead, in kernel
/// bodies and, through the managed version of a device function (#711), in device functions. A division that cannot
/// reach that case (<see cref="DivisionCheckRule.MayDivideMinimumByMinusOne"/>) keeps the C# operator, and so does a
/// division by 0, which .NET throws as C# does.
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

    private static ManagedDivisionPlan? PlanBinary(BinaryExpressionSyntax node, SemanticModel model)
    {
        if (model.GetSafeOperation(node) is not IBinaryOperation operation ||
            operation.Syntax.RawKind != node.RawKind ||
            operation is not { OperatorMethod: null, IsLifted: false, IsChecked: false } ||
            operation.ConstantValue.HasValue ||
            operation.Type?.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_Int64) ||
            !MayDivideMinimumByMinusOne(operation.Type.SpecialType, operation.LeftOperand, operation.RightOperand))
        {
            return null;
        }

        var helper = Pick(operation.OperatorKind == BinaryOperatorKind.Divide, operation.Type.SpecialType,
            Names.DivideInt32, Names.DivideInt64, Names.RemainderInt32, Names.RemainderInt64);
        return new ManagedDivisionPlan(helper, ManagedDivisionForm.Call);
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
            operation.Target.Type?.SpecialType != type ||
            !MayDivideMinimumByMinusOne(type, operation.Target, operation.Value))
        {
            return null;
        }

        // [WHY] C# reads the target before it evaluates the value; the call reads it after, through the reference, so a
        // value that writes something (x /= (x = 5)) keeps the C# operator
        if (operation.Value.Syntax is not ExpressionSyntax value || !ExpressionEffects.Of(value, model).Writes.IsEmpty)
        {
            return null;
        }

        var divide = operation.OperatorKind == BinaryOperatorKind.Divide;
        if (IsReferenceable(operation.Target))
        {
            var assign = Pick(divide, type, Names.DivideAssignInt32, Names.DivideAssignInt64,
                Names.RemainderAssignInt32, Names.RemainderAssignInt64);
            return new ManagedDivisionPlan(assign, ManagedDivisionForm.AssignByReference);
        }

        // A property: get once, divide, set once, as the compound assignment does; its receiver is written twice, so it
        // must neither write nor throw
        if (operation.Target is IPropertyReferenceOperation { Instance: var instance } &&
            (instance is null || IsRepeatable(instance, model)))
        {
            var helper = Pick(divide, type, Names.DivideInt32, Names.DivideInt64, Names.RemainderInt32,
                Names.RemainderInt64);
            return new ManagedDivisionPlan(helper, ManagedDivisionForm.Reassign);
        }

        return null;
    }

    private static bool MayDivideMinimumByMinusOne(SpecialType type, IOperation dividend, IOperation divisor)
    {
        var range = IntegerRange.OfType(type) ?? throw new InvalidOperationException($"{type} has no range.");
        return DivisionCheckRule.MayDivideMinimumByMinusOne(
            type,
            OperationRanges.Of(dividend) ?? range,
            OperationRanges.Of(divisor) ?? range
        );
    }

    // A location that ref can take: a local, a parameter, a field, an array element, a ref-returning property or indexer
    private static bool IsReferenceable(IOperation target) => target switch
    {
        ILocalReferenceOperation or IParameterReferenceOperation or IArrayElementReferenceOperation => true,
        IFieldReferenceOperation { Field.IsReadOnly: false } => true,
        IPropertyReferenceOperation { Property.ReturnsByRef: true } => true,
        _ => false
    };

    private static bool IsRepeatable(IOperation instance, SemanticModel model) =>
        instance.Syntax is ExpressionSyntax receiver &&
        ExpressionEffects.Of(receiver, model).Writes.IsEmpty &&
        !ExpressionTraps.MayTrap(receiver, model);

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
    /// <c>&lt;target&gt; = ManagedGpuContext.&lt;helper&gt;(&lt;target&gt;, &lt;value&gt;)</c>, for a property.
    /// </summary>
    Reassign
}

/// <summary>The <c>ManagedGpuContext</c> function of a division and the form it is written in.</summary>
/// <param name="Helper">The name of the function (<c>__gsharp_div_i32</c>, <c>__gsharp_div_assign_i32</c>, ...).</param>
/// <param name="Form">How the call is written.</param>
internal readonly record struct ManagedDivisionPlan(string Helper, ManagedDivisionForm Form);
