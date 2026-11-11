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
        { Kind: OperationKind.None } => IsPointerVariable(operation.Syntax),
        _ => false
    };

    // [WHY] Roslyn 4.8 represents *p, p[i] and an element of a fixed-size buffer as an operation of kind None. C#
    // (§23.6.4, §23.6.6, §23.8.3) classifies each of them as a variable.
    private static bool IsPointerVariable(SyntaxNode syntax) =>
        syntax is ElementAccessExpressionSyntax or
            PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PointerIndirectionExpression };
}
