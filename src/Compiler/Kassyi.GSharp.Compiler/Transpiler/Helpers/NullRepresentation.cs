namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// How C# <c>null</c> is represented in the emitted C++: an array-like value is null when its <c>data</c> pointer is
/// <c>nullptr</c>, and a <c>Nullable&lt;T&gt;</c> exposes <c>HasValue</c> / <c>Value</c>.
/// </summary>
internal static class NullRepresentation
{
    /// <summary>
    /// Whether a <c>null</c> test on <paramref name="type"/> becomes a <c>data</c> pointer test: an array-like type or a
    /// nullable array-like value type.
    /// </summary>
    public static bool IsArrayNullComparable([NotNullWhen(true)] ITypeSymbol? type) =>
        type != null && (type.IsArrayLikeType() || type.IsNullableArrayLike());

    /// <summary>
    /// Emits <c>(&lt;operand&gt;.data == nullptr)</c>, or <c>!=</c> when <paramref name="isNull"/> is false.
    /// </summary>
    /// <remarks>
    /// A stack array local is a C++ array, which has no <c>data</c> member; C# never gives null for it either (it is
    /// created by its declaration and cannot be reassigned, GSH0091), so the test is the constant.
    /// </remarks>
    public static void AppendArrayNullTest(ExpressionSyntax operand, bool isNull, ref TranspileContext context)
    {
        operand = SkipNullPreservingConversions(operand, context.Query.SemanticModel);
        if (StackArrayLocals.IsStackArrayReference(operand, context.Query.SemanticModel))
        {
            context.Emitter.Writer.Append(isNull ? "false" : "true");
            return;
        }

        using (context.Emitter.Writer.Parentheses())
        {
            // .data binds tighter than any operator, so an operand that is not a primary expression is parenthesized
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

            context.Emitter.Writer.Append(isNull ? ".data == nullptr" : ".data != nullptr");
        }
    }

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
                case CastExpressionSyntax cast when IsArrayNullComparable(model.GetTypeSymbol(cast)) &&
                                                    IsArrayNullComparable(model.GetTypeSymbol(cast.Expression)):
                    operand = cast.Expression;
                    break;
                default:
                    return operand;
            }
        }
    }

    /// <summary>
    /// Emits <c>&lt;left&gt; ?? &lt;right&gt;</c>:
    /// <c>([&amp;]() { auto tmp = &lt;left&gt;; return tmp.HasValue ? tmp.Value : &lt;right&gt;; })()</c>.
    /// </summary>
    public static void AppendCoalesce(ExpressionSyntax left, ExpressionSyntax right, ref TranspileContext context)
    {
        const string temp = GSharpInternalNames.Lowering.CoalesceTemp;
        using (context.Emitter.Writer.ImmediatelyInvokedLambda())
        {
            context.Emitter.Writer.Append($"auto {temp} = ");
            context.Visitor.Visit(left);
            context.Emitter.Writer.AppendLine(";");

            context.Emitter.Writer.Append($"return {temp}.HasValue ? {temp}.Value : ");
            context.Visitor.Visit(right);
            context.Emitter.Writer.AppendLine(";");
        }
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
