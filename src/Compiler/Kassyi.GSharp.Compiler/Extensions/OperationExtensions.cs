using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>Queries on Roslyn operations.</summary>
internal static class OperationExtensions
{
    /// <summary>
    /// Whether <paramref name="operation"/> is classified as a variable in C#: a local, a parameter, <c>this</c> of a
    /// struct, an array element, the result of a call or property that returns by reference, a ref conditional, a field
    /// of a variable (or a static field, or a field of a reference), an element of an inline array variable, or a
    /// pointer indirection, pointer element or fixed-size buffer element. A constant (a <c>const</c> local or field, an
    /// enum member) is a value, not a variable.
    /// </summary>
    // [WHY] C# (§12.8.7 Member access) classifies E.F of a struct as a variable only when E is one: a field of a value
    // (MakeCell(j).F, (a = b).F, new S { }.F) is a value, and an in argument of it is a copy, while Roslyn represents both
    // as an IFieldReferenceOperation
    public static bool IsVariable(this IOperation? operation) => operation switch
    {
        null => false,
        // [WHY] C# classifies a constant as a value (§12.2.1): ref readonly takes a temporary for it (CS9193), and
        // in or ref of it does not compile (CS8156)
        ILocalReferenceOperation { Local.IsConst: true } or IFieldReferenceOperation { Field.IsConst: true } => false,
        ILocalReferenceOperation or IParameterReferenceOperation or IInstanceReferenceOperation
            or IArrayElementReferenceOperation => true,
        IFieldReferenceOperation field => field.Field.IsStatic ||
                                          field.Instance is null ||
                                          field.Instance.Type is { IsReferenceType: true } ||
                                          field.Instance.IsVariable(),
        IPropertyReferenceOperation property => property.Property.RefKind != RefKind.None,
        IInvocationOperation invocation => invocation.TargetMethod.RefKind != RefKind.None,
        IConditionalOperation { IsRef: true } => true,
        IInlineArrayAccessOperation inlineArray => inlineArray.Instance.IsVariable(),
        IParenthesizedOperation parenthesized => parenthesized.Operand.IsVariable(),
        IConversionOperation => false,
        _ => IsPointerDereference(operation)
    };

    // [WHY] *p, p[i], the p of p->F / p->M() / p->P and an element of a fixed-size buffer are variables in C#
    // (23.6.4 to 23.6.6, 23.8.3), whatever operation the Roslyn that runs the generator represents them with (today
    // one of kind None), so they are recognized by their syntax and the pointer type they read through
    private static bool IsPointerDereference(IOperation operation) => operation.Syntax switch
    {
        PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PointerIndirectionExpression } => true,
        ElementAccessExpressionSyntax access =>
            operation.SemanticModel?.GetTypeInfo(access.Expression).Type is IPointerTypeSymbol,
        _ => IsPointerMemberAccessReceiver(operation)
    };

    private static bool IsPointerMemberAccessReceiver(IOperation operation)
    {
        var current = operation;
        while (current.Parent is IParenthesizedOperation parenthesized)
        {
            current = parenthesized;
        }

        return current.Parent switch
        {
            IFieldReferenceOperation
            {
                Syntax: MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.PointerMemberAccessExpression }
            } field => field.Instance == current,
            IPropertyReferenceOperation
            {
                Syntax: MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.PointerMemberAccessExpression }
            } property => property.Instance == current,
            IEventReferenceOperation
            {
                Syntax: MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.PointerMemberAccessExpression }
            } @event => @event.Instance == current,
            IInvocationOperation
            {
                Syntax: InvocationExpressionSyntax invocationSyntax
            } invocation => invocation.Instance == current &&
                             invocationSyntax.Expression.UnwrapParentheses() is MemberAccessExpressionSyntax
                             {
                                 RawKind: (int)SyntaxKind.PointerMemberAccessExpression
                             },
            _ => false
        };
    }

    /// <summary>
    /// The user-defined operator that <paramref name="operation"/> calls by itself: the method of a unary or binary
    /// operator, an increment or decrement, or a compound assignment (for a user-defined <c>&amp;&amp;</c> or
    /// <c>||</c>, its <c>&amp;</c> or <c>|</c>); <see langword="null"/> for a built-in operator or another operation.
    /// </summary>
    public static IMethodSymbol? GetUserDefinedOperator(this IOperation? operation)
    {
        var method = operation switch
        {
            IBinaryOperation binary => binary.OperatorMethod,
            IUnaryOperation unary => unary.OperatorMethod,
            IIncrementOrDecrementOperation step => step.OperatorMethod,
            ICompoundAssignmentOperation compound => compound.OperatorMethod,
            _ => null
        };
        return method is { MethodKind: MethodKind.UserDefinedOperator } ? method : null;
    }
}
