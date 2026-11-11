using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>Queries on Roslyn operations.</summary>
internal static class OperationExtensions
{
    /// <summary>
    /// Whether <paramref name="operation"/> is classified as a variable in C#: a local, a parameter, <c>this</c> of a
    /// struct, an array element, the result of a call or property that returns by reference, a ref conditional, a field
    /// of a variable (or a static field, or a field of a reference), an element of an inline array variable, or a
    /// pointer indirection, pointer element or fixed-size buffer element.
    /// </summary>
    // [WHY] C# (§12.8.7 Member access) classifies E.F of a struct as a variable only when E is one: a field of a value
    // (MakeCell(j).F, (a = b).F, new S { }.F) is a value, and an in argument of it is a copy, while Roslyn represents both
    // as an IFieldReferenceOperation
    public static bool IsVariable(this IOperation? operation) => operation switch
    {
        null => false,
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
        IConversionOperation => false,
        _ => IsPointerDereference(operation)
    };

    // [WHY] *p, p[i], the p of p->F and an element of a fixed-size buffer are variables in C# (23.6.4 to 23.6.6, 23.8.3),
    // whatever operation the Roslyn that runs the generator represents them with (today one of kind None), so they are
    // recognized by their syntax and the pointer type they read through
    private static bool IsPointerDereference(IOperation operation) => operation.Syntax switch
    {
        PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PointerIndirectionExpression } => true,
        ElementAccessExpressionSyntax access =>
            operation.SemanticModel?.GetTypeInfo(access.Expression).Type is IPointerTypeSymbol,
        _ => operation.Parent is IFieldReferenceOperation
             {
                 Syntax: MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.PointerMemberAccessExpression }
             } field &&
             field.Instance == operation
    };
}
