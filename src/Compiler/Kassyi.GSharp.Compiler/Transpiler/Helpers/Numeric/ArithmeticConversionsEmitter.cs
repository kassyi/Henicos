using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Numeric;

/// <summary>
/// Writes the C++ casts and converted operands chosen by <see cref="ArithmeticConversions"/> (#671).
/// </summary>
internal static class ArithmeticConversionsEmitter
{
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
        if (!ArithmeticConversions.IsConverted(operand, type))
        {
            context.Visitor.Visit(syntax);
            return;
        }

        if (operand.ConstantValue is { HasValue: true, Value: { } value })
        {
            ConstantAtUseEmitter.Append(syntax, type, value, ref context);
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

    /// <summary>
    /// Writes <paramref name="argument"/> as a value argument of <paramref name="parameterType"/>, emitting the cast when
    /// C# implicitly converts the argument from another primitive value type (#828).
    /// </summary>
    public static void AppendArgument(ArgumentSyntax argument, ITypeSymbol parameterType, ref TranspileContext context)
    {
        if (!parameterType.SpecialType.IsPrimitiveValue())
        {
            context.Visitor.Visit(argument);
            return;
        }

        var model = context.Query.SemanticModel;
        if (model.GetSafeOperation(argument) is IArgumentOperation { Value: { } value })
        {
            if (ArithmeticConversions.GetSourceType(value) is { } source && source.SpecialType.IsPrimitiveValue())
            {
                AppendOperand(value, argument.Expression, parameterType, ref context);
                return;
            }

            context.Visitor.Visit(argument);
            return;
        }

        if (model.GetTypeSymbol(argument.Expression) is { } exprType &&
            exprType.SpecialType.IsPrimitiveValue() &&
            exprType.SpecialType != parameterType.SpecialType)
        {
            if (model.GetSafeConstantValue(argument.Expression) is { HasValue: true, Value: { } constant })
            {
                ConstantAtUseEmitter.Append(argument.Expression, parameterType, constant, ref context);
                return;
            }

            AppendConversion(parameterType, ref context);
            if (!BindsTighterThanCast(argument.Expression))
            {
                using (context.Emitter.Writer.Parentheses())
                {
                    context.Visitor.Visit(argument.Expression);
                }

                return;
            }
        }

        context.Visitor.Visit(argument);
    }

    /// <summary>
    /// Writes <paramref name="syntax"/>, bound to <paramref name="operand"/>, converted to <paramref name="type"/> when
    /// C# converts it implicitly from another primitive value type (the arm of a switch expression, #710; an element of
    /// a braced list, #897); otherwise as it is, a user-defined conversion being written by its interceptor.
    /// </summary>
    /// <remarks>
    /// <paramref name="operand"/> is the converted value: the implicit conversion that the binding put on the
    /// expression, or the expression itself when it has no conversion.
    /// </remarks>
    public static void AppendImplicitlyConverted(IOperation operand, ExpressionSyntax syntax, ITypeSymbol type,
        ref TranspileContext context)
    {
        if (type.SpecialType.IsPrimitiveValue() &&
            operand is IConversionOperation { IsImplicit: true, OperatorMethod: null, Operand.Type: { } source } &&
            source.SpecialType.IsPrimitiveValue())
        {
            AppendOperand(operand, syntax, type, ref context);
            return;
        }

        context.Visitor.Visit(syntax);
    }

    /// <summary>
    /// Writes the element <paramref name="element"/> of a braced list (an array initializer, a collection expression)
    /// converted to the type C# converts it to, when that is another primitive value type (#897): C++ rejects a
    /// narrowing conversion in a braced list (<c>float w[2]={i,2*i}</c>), and C# allows it implicitly.
    /// </summary>
    public static void AppendElement(ExpressionSyntax element, ref TranspileContext context)
    {
        // [WHY] An integer constant that C# converts to another integer type fits it (C# spec §10.2.11), which is not a
        // narrowing conversion in C++ either; it keeps its written form (0x428a2f98)
        if (context.Query.SemanticModel.GetSafeOperation(element) is
            {
                Parent: IConversionOperation { IsImplicit: true, Type: { } target } conversion
            } operand &&
            !(operand.ConstantValue.HasValue && IsInteger(operand.Type) && IsInteger(target)))
        {
            AppendImplicitlyConverted(conversion, element, target, ref context);
            return;
        }

        context.Visitor.Visit(element);
    }

    private static bool IsInteger(ITypeSymbol? type) => type?.SpecialType is
        SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
        SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64;

    /// <summary>Writes the cast to <paramref name="type"/>: <c>(long long)</c>.</summary>
    public static void AppendConversion(ITypeSymbol type, ref TranspileContext context)
    {
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Dialect.AppendType(ref context, type);
        }
    }

    // A form whose C++ a cast in front applies to as a whole: a primary or postfix expression, a prefix operator, a cast
    private static bool BindsTighterThanCast(ExpressionSyntax syntax) => syntax is
        IdentifierNameSyntax or LiteralExpressionSyntax or MemberAccessExpressionSyntax or
        ElementAccessExpressionSyntax or InvocationExpressionSyntax or ParenthesizedExpressionSyntax or
        PostfixUnaryExpressionSyntax or PrefixUnaryExpressionSyntax or CastExpressionSyntax or ThisExpressionSyntax;
}
