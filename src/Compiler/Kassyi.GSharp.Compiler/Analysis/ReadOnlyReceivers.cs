using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Classifies whether an <see cref="IOperation"/> receiver is a C# readonly variable (C# §12.6.6.1 / §12.8.10.2), so
/// a non-<see langword="readonly"/> struct method or mutating property getter must operate on a defensive copy.
/// </summary>
internal static class ReadOnlyReceivers
{
    /// <summary>
    /// Whether <paramref name="receiver"/> is a variable C# does not let a member write: an <see langword="in"/> or
    /// <see langword="ref readonly"/> parameter, a <see langword="ref readonly"/> local, a <see langword="readonly"/>
    /// field (outside a constructor of its declaring struct on <see langword="this"/>), a value-type field or inline
    /// array element of a readonly variable, <see langword="this"/> inside a <see langword="readonly"/> non-constructor
    /// member, a <see langword="ref readonly"/> call or property result, or a <see langword="ref"/> conditional whose
    /// reachable branch is a readonly variable.
    /// </summary>
    public static bool IsReadOnlyVariable(IOperation receiver) =>
        IsReadOnlyVariable(receiver, foldConstantRefCondition: true);

    private static bool IsReadOnlyVariable(IOperation receiver, bool foldConstantRefCondition) =>
        receiver switch
        {
            IParenthesizedOperation parenthesized => IsReadOnlyVariable(parenthesized.Operand, foldConstantRefCondition),
            IParameterReferenceOperation parameter =>
                parameter.Parameter.RefKind is RefKind.In or RefKind.RefReadOnlyParameter ||
                IsCapturedPrimaryConstructorParameterInReadOnlyMember(parameter),
            ILocalReferenceOperation local => local.Local.RefKind == RefKind.RefReadOnly,
            IFieldReferenceOperation { Field.IsReadOnly: true } field =>
                !IsWrittenInConstructor(field),
            IFieldReferenceOperation { Instance: { } instance, Field.ContainingType.IsValueType: true } =>
                IsReadOnlyVariable(instance, foldConstantRefCondition),
            // Roslyn binds [InlineArray] indexing on a ref conditional as ref readonly whenever either branch is
            // readonly, even when the condition is constant.
            IInlineArrayAccessOperation inlineArray =>
                IsReadOnlyVariable(inlineArray.Instance, foldConstantRefCondition: false),
            IInstanceReferenceOperation self =>
                self.SemanticModel?.GetEnclosingSymbol(self.Syntax.SpanStart) is IMethodSymbol
                {
                    IsReadOnly: true,
                    MethodKind: not MethodKind.Constructor
                },
            IInvocationOperation invocation => invocation.TargetMethod.RefKind == RefKind.RefReadOnly,
            IPropertyReferenceOperation reference => reference.Property.RefKind == RefKind.RefReadOnly,
            IConditionalOperation { IsRef: true } conditional => foldConstantRefCondition
                ? conditional.Condition.ConstantValue switch
                {
                    { HasValue: true, Value: true } => IsReadOnlyVariable(conditional.WhenTrue, foldConstantRefCondition),
                    { HasValue: true, Value: false } =>
                        conditional.WhenFalse is { } falseBranch &&
                        IsReadOnlyVariable(falseBranch, foldConstantRefCondition),
                    _ => IsReadOnlyVariable(conditional.WhenTrue, foldConstantRefCondition) ||
                         (conditional.WhenFalse is { } whenFalse &&
                          IsReadOnlyVariable(whenFalse, foldConstantRefCondition))
                }
                : IsReadOnlyVariable(conditional.WhenTrue, foldConstantRefCondition: false) ||
                  (conditional.WhenFalse is { } branch &&
                   IsReadOnlyVariable(branch, foldConstantRefCondition: false)),
            _ => false
        };

    // A readonly field of the struct, written through this in a constructor of the struct, is not readonly there.
    private static bool IsWrittenInConstructor(IFieldReferenceOperation field) =>
        field.Instance is IInstanceReferenceOperation &&
        field.SemanticModel?.GetEnclosingSymbol(field.Syntax.SpanStart) is IMethodSymbol
        {
            MethodKind: MethodKind.Constructor
        } constructor &&
        constructor.ContainingType.IsEqualTo(field.Field.ContainingType);

    private static bool IsCapturedPrimaryConstructorParameterInReadOnlyMember(IParameterReferenceOperation parameter) =>
        parameter.Parameter.ContainingSymbol is IMethodSymbol ctor &&
        ctor.IsPrimaryConstructor() &&
        parameter.SemanticModel?.GetEnclosingSymbol(parameter.Syntax.SpanStart) is IMethodSymbol
        {
            IsReadOnly: true,
            MethodKind: not MethodKind.Constructor
        };
}
