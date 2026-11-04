namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// How C# <c>null</c> is represented in the emitted C++: an array-like value is null when its <c>data</c> pointer is
/// <c>nullptr</c>, and a <c>Nullable&lt;T&gt;</c> exposes <c>HasValue</c> / <c>Value</c>.
/// </summary>
internal static class NullRepresentation
{
    /// <summary>
    /// Whether a <c>null</c> test on <paramref name="type"/> becomes a <c>data</c> pointer or <c>HasValue</c> test: an
    /// array-like type or a <c>Nullable&lt;T&gt;</c>.
    /// </summary>
    public static bool IsNullComparable([NotNullWhen(true)] ITypeSymbol? type) =>
        type != null && (type.IsArrayLikeType() || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);

    /// <inheritdoc cref="IsNullComparable"/>
    public static bool IsArrayNullComparable([NotNullWhen(true)] ITypeSymbol? type) => IsNullComparable(type);

    /// <summary>
    /// Emits <c>(&lt;operand&gt;.data == nullptr)</c> (or <c>(!&lt;operand&gt;.HasValue)</c> for a
    /// <c>System_Nullable&lt;T&gt;</c>), or the negated test when <paramref name="isNull"/> is false.
    /// </summary>
    /// <remarks>
    /// A non-empty stack array local is a C++ array, which has no <c>data</c> member; C# never gives null for it either
    /// (it is created by its declaration and cannot be reassigned, GSH0091), so the test is the constant. An empty stack
    /// span (<c>stackalloc T[0]</c>, <c>[]</c>) is emitted as <c>GSharpArray&lt;T&gt;{}</c> whose <c>data</c> is
    /// <c>nullptr</c>, matching C# where an empty span equals <c>null</c>.
    /// </remarks>
    public static void AppendNullTest(ExpressionSyntax operand, bool isNull, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        operand = SkipNullPreservingConversions(operand, model);
        if (StackArrayLocals.IsStackArrayReference(operand, model) &&
            !(StackArrayLocals.TryGetLength(operand, model, out var length) && length == 0))
        {
            context.Emitter.Writer.Append(isNull ? "false" : "true");
            return;
        }

        var isNullableStruct = model.GetTypeSymbol(operand) is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
        };

        using (context.Emitter.Writer.Parentheses())
        {
            if (isNullableStruct && isNull)
            {
                context.Emitter.Writer.Append('!');
            }

            // .data and .HasValue bind tighter than any operator, so an operand that is not a primary expression is parenthesized
            if (operand is IdentifierNameSyntax or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax
                or InvocationExpressionSyntax or ThisExpressionSyntax)
            {
                context.Visitor.Visit(operand);
            }
            else
            {
                using (context.Emitter.Writer.Parentheses())
                {
                    context.Visitor.Visit(operand);
                }
            }

            context.Emitter.Writer.Append(isNullableStruct
                ? ".HasValue"
                : isNull ? ".data == nullptr" : ".data != nullptr");
        }
    }

    /// <inheritdoc cref="AppendNullTest"/>
    public static void AppendArrayNullTest(ExpressionSyntax operand, bool isNull, ref TranspileContext context) =>
        AppendNullTest(operand, isNull, ref context);

    // Parentheses, and casts from one array-like type to another ((int[])a), do not change whether a value is null
    private static ExpressionSyntax SkipNullPreservingConversions(ExpressionSyntax operand, SemanticModel model)
    {
        while (true)
        {
            switch (operand)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    operand = parenthesized.Expression;
                    break;
                case CastExpressionSyntax cast when IsNullComparable(model.GetTypeSymbol(cast)) &&
                                                    IsNullComparable(model.GetTypeSymbol(cast.Expression)):
                    operand = cast.Expression;
                    break;
                default:
                    return operand;
            }
        }
    }

    /// <summary>
    /// Emits <c>&lt;left&gt; ?? &lt;right&gt;</c>:
    /// <c>([&amp;]() { auto tmp = &lt;left&gt;; return tmp.HasValue ? tmp.Value : &lt;right&gt;; })()</c>, casting both
    /// branches to the C# result type when the C++ conditional operator would otherwise promote or narrow.
    /// </summary>
    public static void AppendCoalesce(ExpressionSyntax left, ExpressionSyntax right, ref TranspileContext context,
        ITypeSymbol? resultType = null)
    {
        const string temp = GSharpInternalNames.Lowering.CoalesceTemp;
        var model = context.Query.SemanticModel;
        var leftType = model.GetTypeSymbol(left);
        var rightType = model.GetTypeSymbol(right);
        var unwrappedLeft = leftType is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1
        } nullableLeft
            ? nullableLeft.TypeArguments[0]
            : leftType;
        var castResult = resultType != null &&
                         !(leftType.IsEqualTo(resultType) && rightType.IsEqualTo(resultType)) &&
                         !(unwrappedLeft.IsEqualTo(resultType) && rightType.IsEqualTo(resultType));

        using (context.Emitter.Writer.ImmediatelyInvokedLambda())
        {
            context.Emitter.Writer.Append($"auto {temp} = ");
            context.Visitor.Visit(left);
            context.Emitter.Writer.AppendLine(";");

            context.Emitter.Writer.Append($"return {temp}.HasValue ? ");
            if (castResult && resultType != null)
            {
                AppendStaticCast(ref context, resultType);
                context.Emitter.Writer.Append($"{temp}.Value) : ");
                AppendStaticCast(ref context, resultType);
                context.Visitor.Visit(right);
                context.Emitter.Writer.Append(')');
            }
            else
            {
                context.Emitter.Writer.Append($"{temp}.Value : ");
                context.Visitor.Visit(right);
            }

            context.Emitter.Writer.AppendLine(";");
        }
    }

    private static void AppendStaticCast(ref TranspileContext context, ITypeSymbol type)
    {
        context.Emitter.Writer.Append("static_cast<");
        context.Emitter.Dialect.AppendType(ref context, type);
        context.Emitter.Writer.Append(">(");
    }

    /// <summary>
    /// Emits <c>&lt;left&gt; ??= &lt;right&gt;</c>:
    /// <c>([&amp;]() -> decltype(auto) {
    /// auto&amp; t = &lt;left&gt;; return !t.HasValue ? (t = &lt;right&gt;) : t.Value;
    /// })()</c>.
    /// </summary>
    public static void AppendCoalesceAssignment(ExpressionSyntax left, ExpressionSyntax right,
        ref TranspileContext context)
    {
        const string target = GSharpInternalNames.Lowering.CoalesceTarget;
        using (context.Emitter.Writer.ImmediatelyInvokedLambda("decltype(auto)"))
        {
            context.Emitter.Writer.Append($"auto& {target} = ");
            context.Visitor.Visit(left);
            context.Emitter.Writer.AppendLine(";");

            context.Emitter.Writer.Append($"return !{target}.HasValue ? ");
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Writer.Append($"{target} = ");
                context.Visitor.Visit(right);
            }

            context.Emitter.Writer.AppendLine($" : {target}.Value;");
        }
    }
}
