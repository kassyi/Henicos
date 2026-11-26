using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

/// <summary>
/// Whether a read of a <c>GpuBigIntExpr</c> local keeps its value (GSH0005): it is evaluated, compiled, disposed,
/// handed to a <c>[TakesOwnership]</c> parameter through its node, or passed on to code the rule does not follow. Only
/// a read that leads nowhere drops it: a member read, a discard, or a copy into a local or a pattern that drops it in
/// turn (<see cref="GpuBigIntExprLocals.AddCopy"/>).
/// </summary>
internal static class GpuBigIntExprUses
{
    private const string NodeProperty = "Node";
    private const string ValueProperty = "Value";
    private const string FitMethod = "Fit";

    private enum Step
    {
        Up,
        Kept,
        Dropped
    }

    /// <summary>Whether <paramref name="reference"/> keeps the value of its local.</summary>
    public static bool KeepsValue(ILocalReferenceOperation reference, GpuBigIntExprLocals locals)
    {
        if (reference.Parent is IDeclarationExpressionOperation ||
            (reference.Parent is ISimpleAssignmentOperation assignment &&
             ReferenceEquals(assignment.Target, reference)))
        {
            return false;
        }

        IOperation current = reference;
        while (current.Parent is { } parent)
        {
            var step = IsWrapper(parent, current, locals.Types)
                ? Step.Up
                : ClassifyUser(parent, current, reference.Local, locals);
            if (step != Step.Up)
            {
                return step == Step.Kept;
            }

            current = parent;
        }

        return false;
    }

    // An operation whose value is still the expression (or a new expression built from it)
    private static bool IsWrapper(IOperation parent, IOperation current, GpuBigIntExprTypes types) =>
        parent switch
        {
            IConversionOperation => true,
            IConditionalOperation conditional =>
                ReferenceEquals(conditional.WhenTrue, current) || ReferenceEquals(conditional.WhenFalse, current),
            ISwitchExpressionArmOperation arm => ReferenceEquals(arm.Value, current),
            ISwitchExpressionOperation => current is ISwitchExpressionArmOperation,
            ICoalesceOperation coalesce =>
                ReferenceEquals(coalesce.Value, current) || ReferenceEquals(coalesce.WhenNull, current),
            IPropertyReferenceOperation { Property: { Name: ValueProperty } property } nullableValue =>
                ReferenceEquals(nullableValue.Instance, current) && IsNullableMember(property),
            IInvocationOperation invocation when ReferenceEquals(invocation.Instance, current) =>
                (invocation.TargetMethod.Name == nameof(Nullable<int>.GetValueOrDefault) &&
                 IsNullableMember(invocation.TargetMethod)) ||
                IsFit(invocation, types),
            IBinaryOperation { OperatorMethod.Parameters: [var left, var right] } binary =>
                (ReferenceEquals(binary.LeftOperand, current) && types.Matches(left.Type)) ||
                (ReferenceEquals(binary.RightOperand, current) && types.Matches(right.Type)),
            IUnaryOperation { OperatorMethod.Parameters: [var operand] } unary =>
                ReferenceEquals(unary.Operand, current) && types.Matches(operand.Type),
            _ => false
        };

    // An operation that reads the expression without being one
    private static Step ClassifyUser(IOperation parent, IOperation current, ILocalSymbol local,
        GpuBigIntExprLocals locals) =>
        parent switch
        {
            IInvocationOperation invocation when ReferenceEquals(invocation.Instance, current) =>
                KeptIf(IsConsumingMethodName(invocation.TargetMethod.Name)),
            IConditionalAccessOperation access when ReferenceEquals(access.Operation, current) =>
                ClassifyConditionalAccess(access, locals.Types),
            IUsingOperation usingOperation when ReferenceEquals(usingOperation.Resources, current) => Step.Kept,
            IPropertyReferenceOperation { Property.Name: NodeProperty } node
                when ReferenceEquals(node.Instance, current) =>
                KeptIf(IsNodeReferenceConsumed(node)),
            IMethodReferenceOperation => Step.Kept,
            IMemberReferenceOperation member when ReferenceEquals(member.Instance, current) => Step.Dropped,
            IArgumentOperation argument when ReferenceEquals(argument.Value, current) =>
                KeptIf(argument.Parameter is not { RefKind: RefKind.Out }),
            ISimpleAssignmentOperation assignment when ReferenceEquals(assignment.Value, current) =>
                ClassifyStore(assignment.Target, local, locals),
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } initializer
                when ReferenceEquals(initializer.Value, current) =>
                ClassifyInitialized(declarator, local, locals),
            IIsPatternOperation test when ReferenceEquals(test.Value, current) =>
                CopyIntoPattern(test.Pattern, local, locals),
            ISwitchExpressionOperation switchExpression when ReferenceEquals(switchExpression.Value, current) =>
                CopyIntoArms(switchExpression, local, locals),
            ISwitchOperation switchStatement when ReferenceEquals(switchStatement.Value, current) =>
                CopyIntoCases(switchStatement, local, locals),

            // A tuple, a collection, a field initializer, a return...: the value goes where the rule does not follow
            _ => Step.Kept
        };

    private static Step KeptIf(bool kept) => kept ? Step.Kept : Step.Dropped;

    private static Step ClassifyStore(IOperation target, ILocalSymbol local, GpuBigIntExprLocals locals) =>
        target switch
        {
            IPropertyReferenceOperation { Property.Name: ValueProperty } => Step.Kept,
            IParameterReferenceOperation { Parameter.RefKind: RefKind.Out or RefKind.Ref } => Step.Kept,
            ILocalReferenceOperation { Local: var targetLocal } when locals.IsTracked(targetLocal) =>
                Copy(targetLocal, local, locals),
            ILocalReferenceOperation => Step.Kept,

            // A discard drops it; a field, property or element is reported at the assignment itself
            _ => Step.Dropped
        };

    private static Step ClassifyInitialized(IVariableDeclaratorOperation declarator, ILocalSymbol local,
        GpuBigIntExprLocals locals)
    {
        if (IsUsingResource(declarator))
        {
            return Step.Kept;
        }

        return locals.IsTracked(declarator.Symbol) ? Copy(declarator.Symbol, local, locals) : Step.Kept;
    }

    private static Step Copy(ILocalSymbol target, ILocalSymbol source, GpuBigIntExprLocals locals)
    {
        locals.AddCopy(target, source);
        return Step.Dropped;
    }

    private static Step CopyIntoArms(ISwitchExpressionOperation switchExpression, ILocalSymbol local,
        GpuBigIntExprLocals locals)
    {
        foreach (var arm in switchExpression.Arms)
        {
            CopyIntoPattern(arm.Pattern, local, locals);
        }

        return Step.Dropped;
    }

    private static Step CopyIntoCases(ISwitchOperation switchStatement, ILocalSymbol local,
        GpuBigIntExprLocals locals)
    {
        foreach (var switchCase in switchStatement.Cases)
        {
            foreach (var clause in switchCase.Clauses)
            {
                if (clause is IPatternCaseClauseOperation patternClause)
                {
                    CopyIntoPattern(patternClause.Pattern, local, locals);
                }
            }
        }

        return Step.Dropped;
    }

    private static Step CopyIntoPattern(IPatternOperation pattern, ILocalSymbol local, GpuBigIntExprLocals locals)
    {
        switch (pattern)
        {
            case IDeclarationPatternOperation { DeclaredSymbol: ILocalSymbol declared }
                when locals.IsTracked(declared):
                locals.AddCopy(declared, local);
                break;

            case IRecursivePatternOperation { DeclaredSymbol: ILocalSymbol declared }
                when locals.IsTracked(declared):
                locals.AddCopy(declared, local);
                break;

            case IBinaryPatternOperation binary:
                CopyIntoPattern(binary.LeftPattern, local, locals);
                CopyIntoPattern(binary.RightPattern, local, locals);
                break;
        }

        return Step.Dropped;
    }

    private static Step ClassifyConditionalAccess(IConditionalAccessOperation access, GpuBigIntExprTypes types)
    {
        if (IsConsumingConditionalAccess(access.WhenNotNull, types) ||
            (access.WhenNotNull is IPropertyReferenceOperation { Property.Name: NodeProperty } node &&
             ReachesConditionalInstance(node.Instance, types) &&
             IsNodeReferenceConsumed(access)))
        {
            return Step.Kept;
        }

        return types.Matches(access.Type) ? Step.Up : Step.Dropped;
    }

    private static bool IsConsumingConditionalAccess(IOperation whenNotNull, GpuBigIntExprTypes types)
    {
        if (whenNotNull is IInvocationOperation invocation)
        {
            if (IsConsumingMethodName(invocation.TargetMethod.Name) &&
                ReachesConditionalInstance(invocation.Instance, types))
            {
                return true;
            }

            if (IsConsumingNodeMethodName(invocation.TargetMethod.Name) &&
                UnwrapConversions(invocation.Instance) is IPropertyReferenceOperation
                {
                    Property.Name: NodeProperty
                } node &&
                ReachesConditionalInstance(node.Instance, types))
            {
                return true;
            }
        }

        return whenNotNull is IConditionalAccessOperation inner &&
               UnwrapConversions(inner.Operation) is IPropertyReferenceOperation
               {
                   Property.Name: NodeProperty
               } innerNode &&
               ReachesConditionalInstance(innerNode.Instance, types) &&
               IsConsumingNodeConditionalAccess(inner.WhenNotNull);
    }

    private static bool IsConsumingNodeConditionalAccess(IOperation whenNotNull) =>
        whenNotNull is IInvocationOperation invocation &&
        IsConsumingNodeMethodName(invocation.TargetMethod.Name) &&
        UnwrapConversions(invocation.Instance) is IConditionalAccessInstanceOperation;

    private static IOperation? UnwrapConversions(IOperation? operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static bool ReachesConditionalInstance(IOperation? operation, GpuBigIntExprTypes types)
    {
        while (true)
        {
            switch (operation)
            {
                case IConditionalAccessInstanceOperation:
                    return true;
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    break;
                case IInvocationOperation invocation when IsFit(invocation, types):
                    operation = invocation.Instance;
                    break;
                default:
                    return false;
            }
        }
    }

    private static bool IsNodeReferenceConsumed(IOperation nodeOperation)
    {
        var current = nodeOperation;
        while (current.Parent is IConversionOperation conversion)
        {
            current = conversion;
        }

        return current.Parent switch
        {
            IArgumentOperation argument when ReferenceEquals(argument.Value, current) =>
                argument.Parameter is { RefKind: not RefKind.Out } parameter &&
                parameter.HasAttribute(KnownAttributes.TakesOwnership),
            IInvocationOperation invocation when ReferenceEquals(invocation.Instance, current) =>
                IsConsumingNodeMethodName(invocation.TargetMethod.Name),
            IConditionalAccessOperation access when ReferenceEquals(access.Operation, current) =>
                IsConsumingNodeConditionalAccess(access.WhenNotNull),
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } initializer
                when ReferenceEquals(initializer.Value, current) =>
                IsUsingResource(declarator),
            _ => false
        };
    }

    private static bool IsFit(IInvocationOperation invocation, GpuBigIntExprTypes types) =>
        invocation.TargetMethod.Name == FitMethod && types.Matches(invocation.TargetMethod.ReturnType);

    private static bool IsNullableMember(ISymbol member) =>
        member.ContainingType?.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static bool IsConsumingNodeMethodName(string name) =>
        name is "Evaluate" or "Accept" or "Dispose" or "DisposeAsync";

    private static bool IsConsumingMethodName(string name) =>
        name is "Evaluate" or "CompileToGraph" or "EvaluateWithCudaGraph" or "EvaluateWithCudaGraphAsync"
            or "Dispose" or "DisposeAsync";

    /// <summary>Whether <paramref name="declarator"/> is a resource of a <c>using</c> statement or declaration.
    /// </summary>
    public static bool IsUsingResource(IVariableDeclaratorOperation declarator) =>
        declarator.Parent?.Parent?.Parent is IUsingOperation or IUsingDeclarationOperation;
}
