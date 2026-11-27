namespace Kassyi.GSharp.Compiler.Semantics;

/// <summary>How a value of a type is null in the emitted C++.</summary>
internal enum NullForm
{
    /// <summary>The type has no null of its own in C++ (a reference to a class, a pointer, ...).</summary>
    None,
    /// <summary>An array or span: data == nullptr.</summary>
    ArrayView,
    /// <summary>A <c>Nullable&lt;T&gt;</c>: !HasValue.</summary>
    NullableValue
}

/// <summary>
/// How C# <c>null</c> is represented in the emitted C++: an array-like value is null when its <c>data</c> pointer is
/// <c>nullptr</c>, and a <c>Nullable&lt;T&gt;</c> exposes <c>HasValue</c> / <c>Value</c>.
/// </summary>
internal static class NullRepresentation
{
    /// <summary>How a value of <paramref name="type"/> is represented as null in C++.</summary>
    public static NullForm GetNullForm(ITypeSymbol? type)
    {
        if (type == null)
        {
            return NullForm.None;
        }

        if (type.IsArrayLikeType())
        {
            return NullForm.ArrayView;
        }

        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return NullForm.NullableValue;
        }

        return NullForm.None;
    }

    /// <summary>
    /// Whether a <c>null</c> test on <paramref name="type"/> becomes a <c>data</c> pointer or <c>HasValue</c> test: an
    /// array-like type or a <c>Nullable&lt;T&gt;</c>.
    /// </summary>
    public static bool IsNullComparable([NotNullWhen(true)] ITypeSymbol? type) =>
        GetNullForm(type) != NullForm.None;

    /// <summary>
    /// Whether a cast of a value of <paramref name="operandType"/> to <paramref name="castType"/> keeps whether it is
    /// null: both types are null-comparable.
    /// </summary>
    public static bool PreservesNull(ITypeSymbol? castType, ITypeSymbol? operandType) =>
        IsNullComparable(castType) && IsNullComparable(operandType);

    /// <summary>
    /// Whether the two branches of a <c>??</c> whose left is not an array are cast to
    /// <paramref name="resultType"/>: they are, unless the left (or its underlying type) and the right already have
    /// that type.
    /// </summary>
    public static bool CoalesceCastsToResult(ITypeSymbol? leftType, ITypeSymbol? rightType, ITypeSymbol? resultType)
    {
        if (resultType == null)
        {
            return false;
        }

        var unwrappedLeft = leftType is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1
        } nullableLeft
            ? nullableLeft.TypeArguments[0]
            : leftType;

        return !(leftType.IsEqualTo(resultType) && rightType.IsEqualTo(resultType)) &&
               !(unwrappedLeft.IsEqualTo(resultType) && rightType.IsEqualTo(resultType));
    }

    /// <summary>
    /// Whether a <c>??=</c> on a <paramref name="targetType"/> target yields its underlying type
    /// <paramref name="valueType"/>, so that the result reads <c>.Value</c>.
    /// </summary>
    public static bool CoalesceAssignmentReadsUnderlying(ITypeSymbol targetType, ITypeSymbol valueType) =>
        !targetType.IsArrayLikeType() && !valueType.IsEqualTo(targetType) &&
        valueType.IsEqualTo(targetType.UnwrapNullable());

    /// <summary>
    /// The operand that <paramref name="node"/> compares with the <c>null</c> literal when it is written as a null test
    /// (<c>NullRepresentationEmitter.AppendNullTest</c>): <c>x == null</c>, <c>null != x</c> and the like for an <paramref name="node"/>
    /// whose other operand is <see cref="IsNullComparable"/>; <see langword="null"/> otherwise. The test is written
    /// whatever operator C# binds (<c>Span&lt;T&gt;</c> compares through its user-defined <c>==</c>).
    /// </summary>
    public static ExpressionSyntax? GetNullTestOperand(BinaryExpressionSyntax node, SemanticModel model)
    {
        if (!node.IsKind(SyntaxKind.EqualsExpression) && !node.IsKind(SyntaxKind.NotEqualsExpression))
        {
            return null;
        }

        if (IsNullValue(node.Left, model) && IsNullComparable(model.GetTypeSymbol(node.Right)))
        {
            return node.Right;
        }

        return IsNullValue(node.Right, model) && IsNullComparable(model.GetTypeSymbol(node.Left))
            ? node.Left
            : null;
    }

    // null, the default literal and default(T) of a null-comparable type: n == default is the lifted comparison with
    // null, the HasValue test of n, and arr == default compares the array with null, as C# converts the literal to
    // object there. The caller checks that the other operand is null-comparable, so the literal needs no type here.
    private static bool IsNullValue(ExpressionSyntax operand, SemanticModel model) =>
        operand.IsKind(SyntaxKind.NullLiteralExpression, SyntaxKind.DefaultLiteralExpression) ||
        (operand is DefaultExpressionSyntax && IsNullComparable(model.GetTypeSymbol(operand)));

    // Parentheses, the null-forgiving operator (a!) and casts from one array-like type to another ((int[])a) do not
    // change whether a value is null
    internal static ExpressionSyntax SkipNullPreservingConversions(ExpressionSyntax operand, SemanticModel model)
    {
        while (true)
        {
            switch (operand)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    operand = parenthesized.Expression;
                    break;
                case PostfixUnaryExpressionSyntax suppressed
                    when suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    operand = suppressed.Operand;
                    break;
                case CastExpressionSyntax cast when PreservesNull(model.GetTypeSymbol(cast),
                                                    model.GetTypeSymbol(cast.Expression)):
                    operand = cast.Expression;
                    break;
                default:
                    return operand;
            }
        }
    }
}
