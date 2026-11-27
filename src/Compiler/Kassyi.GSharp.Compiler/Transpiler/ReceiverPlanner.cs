using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Plans how a struct instance <c>[GpuDeviceFunction]</c> call or a reduced <c>[GpuDeviceFunction]</c> extension call
/// passes its receiver, following C# §12.6.6.1 / §12.8.10.2.
/// </summary>
internal static class ReceiverPlanner
{
    /// <summary>Plans the receiver of <paramref name="node"/> calling struct instance method <paramref name="method"/>.</summary>
    public static ReceiverPlan PlanInstanceCall(InvocationExpressionSyntax node, IMethodSymbol method,
        ref TranspileContext context) =>
        PlanInstanceCall(node, method, context.Query.SemanticModel, context.Emitter.CurrentStructType,
            context.Emitter.StructSelf.Form);

    /// <summary>
    /// Plans the receiver of <paramref name="node"/> calling struct instance method <paramref name="method"/> under
    /// <paramref name="model"/>, <paramref name="currentStruct"/>, and <paramref name="selfForm"/>.
    /// </summary>
    public static ReceiverPlan PlanInstanceCall(InvocationExpressionSyntax node, IMethodSymbol method,
        SemanticModel model, INamedTypeSymbol? currentStruct, SelfForm selfForm)
    {
        var expression = node.Expression.UnwrapParentheses() is MemberAccessExpressionSyntax access
            ? access.Expression
            : null;
        var readOnlyCallee = method.IsReadOnly;
        var operation = model.GetSafeOperation(node) as IInvocationOperation;
        var receiverOp = operation?.Instance ?? (expression != null ? model.GetSafeOperation(expression) : null);

        if ((receiverOp is IInstanceReferenceOperation &&
             expression?.UnwrapParentheses() is null or ThisExpressionSyntax) ||
            (receiverOp == null && expression == null))
        {
            var isReadOnlySelf = (receiverOp != null && ReadOnlyReceivers.IsReadOnlyVariable(receiverOp)) ||
                                 (selfForm == SelfForm.MemberFunction && IsConstMemberGetter(receiverOp, model, node));
            if (isReadOnlySelf && !readOnlyCallee)
            {
                return new ReceiverPlan(
                    ReceiverForm.Copy,
                    expression,
                    selfForm,
                    PropertyAccessorCall.CopyTypeOf(method.ContainingType, currentStruct, model.Compilation),
                    readOnlyCallee,
                    IsSelf: true);
            }

            return new ReceiverPlan(
                ReceiverForm.Self,
                expression,
                selfForm,
                CopyType: null,
                readOnlyCallee,
                IsSelf: true);
        }

        if (!receiverOp.IsVariable())
        {
            return new ReceiverPlan(
                ReceiverForm.Temporary,
                expression,
                selfForm,
                PropertyAccessorCall.CopyTypeOf(method.ContainingType, currentStruct, model.Compilation),
                readOnlyCallee);
        }

        var isReadOnlyReceiver = receiverOp is not null &&
                                 (ReadOnlyReceivers.IsReadOnlyVariable(receiverOp) ||
                                  (selfForm == SelfForm.MemberFunction &&
                                   IsMemberChainOnThis(receiverOp) &&
                                   IsConstMemberGetter(receiverOp, model, node)));
        if (isReadOnlyReceiver && !readOnlyCallee)
        {
            return new ReceiverPlan(
                ReceiverForm.Copy,
                expression,
                selfForm,
                PropertyAccessorCall.CopyTypeOf(method.ContainingType, currentStruct, model.Compilation),
                readOnlyCallee);
        }

        return new ReceiverPlan(
            ReceiverForm.Address,
            expression,
            selfForm,
            CopyType: null,
            readOnlyCallee);
    }

    /// <summary>Plans the receiver of a reduced extension call on <paramref name="receiver"/>.</summary>
    public static ReceiverPlan PlanExtensionCall(ExpressionSyntax receiver, IMethodSymbol extension,
        SemanticModel model)
    {
        var thisParameter = extension.Parameters[0];
        if (thisParameter.RefKind == RefKind.None)
        {
            return new ReceiverPlan(
                ReceiverForm.Value,
                receiver,
                default,
                CopyType: null,
                ReadOnlyCallee: true,
                IsExtensionReceiver: true,
                ValueType: thisParameter.Type);
        }

        if (thisParameter.RefKind == RefKind.Ref)
        {
            return new ReceiverPlan(
                ReceiverForm.Address,
                receiver,
                default,
                CopyType: null,
                ReadOnlyCallee: false,
                IsExtensionReceiver: true);
        }

        var receiverOp = model.GetSafeOperation(receiver);
        if (receiverOp.IsVariable())
        {
            return new ReceiverPlan(
                ReceiverForm.Address,
                receiver,
                default,
                CopyType: null,
                ReadOnlyCallee: true,
                IsExtensionReceiver: true);
        }

        return new ReceiverPlan(
            ReceiverForm.Temporary,
            receiver,
            default,
            CopyType: null,
            ReadOnlyCallee: true,
            IsExtensionReceiver: true);
    }

    private static bool IsConstMemberGetter(IOperation? receiverOp, SemanticModel model, SyntaxNode fallbackNode)
    {
        var enclosing = receiverOp?.SemanticModel?.GetEnclosingSymbol(receiverOp.Syntax.SpanStart) ??
                        model.GetEnclosingSymbol(fallbackNode.SpanStart);
        return enclosing is IMethodSymbol
               {
                   MethodKind: MethodKind.PropertyGet,
                   AssociatedSymbol: IPropertySymbol property
               } &&
               PropertyAccessorCall.IsConstGetter(property, model.Compilation);
    }

    private static bool IsMemberChainOnThis(IOperation? operation) =>
        operation switch
        {
            IParenthesizedOperation parenthesized => IsMemberChainOnThis(parenthesized.Operand),
            IInstanceReferenceOperation => true,
            IFieldReferenceOperation { Field: { IsStatic: false, ContainingType.IsValueType: true }, Instance: { } instance } =>
                IsMemberChainOnThis(instance),
            IParameterReferenceOperation { Parameter.ContainingSymbol: IMethodSymbol ctor }
                when ctor.IsPrimaryConstructor() => true,
            IInlineArrayAccessOperation inlineArray => IsMemberChainOnThis(inlineArray.Instance),
            // [WHY] ConditionalExpressionTranspilerRule folds constant-condition ref conditionals to the selected branch,
            // while a runtime ref conditional promotes (flag ? &constMember : &local) to const T* in C++.
            IConditionalOperation { IsRef: true } conditional => conditional.Condition.ConstantValue switch
            {
                { HasValue: true, Value: true } => IsMemberChainOnThis(conditional.WhenTrue),
                { HasValue: true, Value: false } =>
                    conditional.WhenFalse is { } falseBranch && IsMemberChainOnThis(falseBranch),
                _ => IsMemberChainOnThis(conditional.WhenTrue) ||
                     (conditional.WhenFalse is { } whenFalse && IsMemberChainOnThis(whenFalse))
            },
            _ => false
        };
}
