using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The conversions of C# numeric promotion (C# spec §12.4.7) and of the branches of a conditional (§12.18) that the
/// usual arithmetic conversions of C++ do not make on their own, so the emitter writes them (#671).
/// </summary>
/// <remarks>
/// <para>
/// C# converts <see langword="uint"/> mixed with <see langword="int"/>, <see langword="short"/> or
/// <see langword="sbyte"/> to <see langword="long"/>; C++ converts <c>unsigned int</c> mixed with <c>int</c> to
/// <c>unsigned int</c>, so <c>7u + -8</c> is <c>-1</c> in C# and <c>4294967295</c> in C++. The type C# picks is read
/// from Roslyn (the implicit conversions on the operands of the bound operation), the type C++ picks is computed from the
/// C# types of the operands through the dialect type map, and the operands are converted only where the two differ.
/// </para>
/// <para>
/// [WHY] Only where they differ: writing every implicit conversion (<c>(int)b1+(int)b2</c>, <c>f*(float)i</c>) gives the
/// same values, but makes the output noisy where C++ already agrees with C#.
/// </para>
/// </remarks>
internal static class ArithmeticConversions
{
    /// <summary>
    /// The type C# converts the operands of <paramref name="operation"/> to, when the usual arithmetic conversions of C++
    /// would pick another one (<see langword="uint"/> mixed with <see langword="int"/>, <see langword="short"/> or
    /// <see langword="sbyte"/>: <see langword="long"/> in C#, <c>unsigned int</c> in C++).
    /// </summary>
    /// <remarks>
    /// Covers the arithmetic, bitwise and comparison operators. The shifts promote each operand on its own in both
    /// languages, and a user-defined or lifted operator has no C++ arithmetic conversion to compare with.
    /// </remarks>
    public static bool TryGetPromotedType(IBinaryOperation operation, [NotNullWhen(true)] out ITypeSymbol? type)
    {
        type = null;
        if (operation.OperatorMethod != null || operation.IsLifted || !IsPromotingOperator(operation.OperatorKind) ||
            operation.LeftOperand.Type is not { } left || operation.RightOperand.Type is not { } right ||
            left.SpecialType != right.SpecialType ||
            GetSourceType(operation.LeftOperand) is not { } leftSource ||
            GetSourceType(operation.RightOperand) is not { } rightSource)
        {
            return false;
        }

        var cpp = UsualArithmeticConversion(leftSource.SpecialType, rightSource.SpecialType);
        if (cpp == SpecialType.None || cpp == left.SpecialType)
        {
            return false;
        }

        type = left;
        return true;
    }

    /// <summary>
    /// The type C# converts the operand of <paramref name="operation"/> to, when the integral promotions of C++ would
    /// keep another one (<see langword="uint"/> under unary minus: <see langword="long"/> in C# (C# spec §12.4.7.2),
    /// <c>unsigned int</c> in C++).
    /// </summary>
    public static bool TryGetPromotedType(IUnaryOperation operation, [NotNullWhen(true)] out ITypeSymbol? type)
    {
        type = null;
        if (operation.OperatorMethod != null || operation.IsLifted ||
            operation.OperatorKind is not (UnaryOperatorKind.Minus or UnaryOperatorKind.Plus or
                UnaryOperatorKind.BitwiseNegation) ||
            operation.Type is not { } result || !result.SpecialType.IsArithmetic() ||
            GetSourceType(operation.Operand) is not { } source || !source.SpecialType.IsArithmetic() ||
            PromoteIntegral(source.SpecialType) == result.SpecialType)
        {
            return false;
        }

        type = result;
        return true;
    }

    /// <summary>
    /// The type of <paramref name="operation"/>, which C# converts both branches to, when C++ would give the
    /// conditional another one: a target-typed <c>c ? u : n</c> (C# spec §12.18) is <see langword="long"/> or
    /// <see langword="double"/> in C#, and <c>unsigned int</c> in C++, which converts branches of different types with
    /// the usual arithmetic conversions ([expr.cond]).
    /// </summary>
    public static bool TryGetPromotedType(IConditionalOperation operation, [NotNullWhen(true)] out ITypeSymbol? type)
    {
        type = null;
        if (operation.IsRef || operation.WhenFalse is not { } whenFalse ||
            operation.Type is not { } result || !result.SpecialType.IsArithmetic() ||
            GetSourceType(operation.WhenTrue) is not { } trueSource || GetSourceType(whenFalse) is not { } falseSource)
        {
            return false;
        }

        // Branches of the same type keep it, without the integral promotions
        var cpp = trueSource.SpecialType == falseSource.SpecialType
            ? trueSource.SpecialType
            : UsualArithmeticConversion(trueSource.SpecialType, falseSource.SpecialType);
        if (cpp == SpecialType.None || cpp == result.SpecialType)
        {
            return false;
        }

        type = result;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="operand"/> (an operand of an operation whose operands C# converts to
    /// <paramref name="type"/>) has another C# type before that conversion, so that the emitter converts it.
    /// </summary>
    public static bool IsConverted(IOperation operand, ITypeSymbol type) =>
        GetSourceType(operand) is not { } source || source.SpecialType != type.SpecialType;

    /// <summary>
    /// Writes <paramref name="syntax"/> (the operand <paramref name="operand"/>) converted to <paramref name="type"/>: a
    /// constant as the converted value (<c>(-8LL)</c>), anything else as <c>(&lt;type&gt;)&lt;operand&gt;</c>, with the
    /// operand parenthesized when the cast would bind tighter than it; the operand as it is when it already has that
    /// type.
    /// </summary>
    /// <remarks>
    /// <paramref name="syntax"/> is the node that is written: in a lowered tree it is a rewritten copy of the operand that
    /// <paramref name="operand"/> was bound from.
    /// </remarks>
    public static void AppendOperand(IOperation operand, ExpressionSyntax syntax, ITypeSymbol type,
        ref TranspileContext context)
    {
        if (!IsConverted(operand, type))
        {
            context.Visitor.Visit(syntax);
            return;
        }

        if (operand.ConstantValue is { HasValue: true, Value: { } value })
        {
            ConstantAtUse.Append(syntax, type, value, ref context);
            return;
        }

        AppendConversion(type, ref context);
        if (!BindsTighterThanCast(syntax))
        {
            using (context.Emitter.Writer.Parentheses())
            {
                context.Visitor.Visit(syntax);
            }

            return;
        }

        context.Visitor.Visit(syntax);
    }

    /// <summary>Writes the cast to <paramref name="type"/>: <c>(long long)</c>.</summary>
    public static void AppendConversion(ITypeSymbol type, ref TranspileContext context)
    {
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Dialect.AppendType(ref context, type);
        }
    }

    private static bool IsPromotingOperator(BinaryOperatorKind kind) => kind is
        BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply or
        BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder or
        BinaryOperatorKind.And or BinaryOperatorKind.Or or BinaryOperatorKind.ExclusiveOr or
        BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals or
        BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or
        BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual;

    // The C# type of an operand before the implicit conversion that the binding put on it; an explicit cast is the
    // user's own and stays
    private static ITypeSymbol? GetSourceType(IOperation operand) =>
        operand is IConversionOperation { IsImplicit: true, Operand.Type: { } source } ? source : operand.Type;

    // The C++ type, as the C# special type that maps to it, that the usual arithmetic conversions ([expr.arith.conv])
    // give operands of these C# types; None when one is not an arithmetic type of the dialect type map
    private static SpecialType UsualArithmeticConversion(SpecialType left, SpecialType right)
    {
        if (!left.IsArithmetic() || !right.IsArithmetic())
        {
            return SpecialType.None;
        }

        if (left == SpecialType.System_Double || right == SpecialType.System_Double)
        {
            return SpecialType.System_Double;
        }

        if (left == SpecialType.System_Single || right == SpecialType.System_Single)
        {
            return SpecialType.System_Single;
        }

        left = PromoteIntegral(left);
        right = PromoteIntegral(right);
        if (left == right)
        {
            return left;
        }

        // Same signedness: the greater rank. Otherwise the unsigned type when its rank is not lower; else the signed
        // type, which is long long here and holds every unsigned int (the third C++ case, the unsigned counterpart of
        // the signed type, needs a signed type as wide as the unsigned one of lower rank, which the type map has not)
        var leftSigned = IsSigned(left);
        if (leftSigned == IsSigned(right))
        {
            return GetRank(left) >= GetRank(right) ? left : right;
        }

        var (unsigned, signed) = leftSigned ? (right, left) : (left, right);
        return GetRank(unsigned) >= GetRank(signed) ? unsigned : signed;
    }

    // The integral promotions ([conv.prom]): every type narrower than int (char16_t included) becomes int
    private static SpecialType PromoteIntegral(SpecialType type) => type is
        SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
        SpecialType.System_Char
            ? SpecialType.System_Int32
            : type;

    private static bool IsSigned(SpecialType promoted) =>
        promoted is SpecialType.System_Int32 or SpecialType.System_Int64;

    // The integer conversion rank of a promoted type: int and unsigned int, then long long and unsigned long long
    private static int GetRank(SpecialType promoted) =>
        promoted is SpecialType.System_Int64 or SpecialType.System_UInt64 ? 2 : 1;

    // A form whose C++ a cast in front applies to as a whole: a primary or postfix expression, a prefix operator, a cast
    private static bool BindsTighterThanCast(ExpressionSyntax syntax) => syntax is
        IdentifierNameSyntax or LiteralExpressionSyntax or MemberAccessExpressionSyntax or
        ElementAccessExpressionSyntax or InvocationExpressionSyntax or ParenthesizedExpressionSyntax or
        PostfixUnaryExpressionSyntax or PrefixUnaryExpressionSyntax or CastExpressionSyntax or ThisExpressionSyntax;
}
