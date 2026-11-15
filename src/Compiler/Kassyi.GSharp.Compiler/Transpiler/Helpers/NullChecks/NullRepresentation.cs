namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;

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

    /// <summary>
    /// The operand that <paramref name="node"/> compares with the <c>null</c> literal when it is written as a null test
    /// (<see cref="AppendNullTest"/>): <c>x == null</c>, <c>null != x</c> and the like for an <paramref name="node"/>
    /// whose other operand is <see cref="IsNullComparable"/>; <see langword="null"/> otherwise. The test is written
    /// whatever operator C# binds (<c>Span&lt;T&gt;</c> compares through its user-defined <c>==</c>).
    /// </summary>
    public static ExpressionSyntax? GetNullTestOperand(BinaryExpressionSyntax node, SemanticModel model)
    {
        if (!node.IsKind(SyntaxKind.EqualsExpression) && !node.IsKind(SyntaxKind.NotEqualsExpression))
        {
            return null;
        }

        if (node.Left.IsKind(SyntaxKind.NullLiteralExpression) && IsNullComparable(model.GetTypeSymbol(node.Right)))
        {
            return node.Right;
        }

        return node.Right.IsKind(SyntaxKind.NullLiteralExpression) && IsNullComparable(model.GetTypeSymbol(node.Left))
            ? node.Left
            : null;
    }

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
        if (StackArrayLocals.NeedsView(operand, model))
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

    // Parentheses, the null-forgiving operator (a!) and casts from one array-like type to another ((int[])a) do not
    // change whether a value is null
    private static ExpressionSyntax SkipNullPreservingConversions(ExpressionSyntax operand, SemanticModel model)
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
    /// branches to the C# result type when the C++ conditional operator would otherwise promote or narrow. An array
    /// on the left is tested by its <c>data</c> pointer and is the result itself (#880).
    /// </summary>
    public static void AppendCoalesce(ExpressionSyntax left, ExpressionSyntax right, ref TranspileContext context,
        ITypeSymbol? resultType = null)
    {
        const string temp = GSharpInternalNames.Lowering.CoalesceTemp;
        var model = context.Query.SemanticModel;
        var leftType = model.GetTypeSymbol(left);
        var rightType = model.GetTypeSymbol(right);
        if (leftType != null && leftType.IsArrayLikeType())
        {
            AppendArrayCoalesce(left, right, ref context);
            return;
        }

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

    // ([&]() { auto tmp = <left>; return tmp.data != nullptr ? tmp : <right>; }()); a non-empty stack array is never
    // null (AppendNullTest), so it is the result and the right side is not evaluated, as in C#
    private static void AppendArrayCoalesce(ExpressionSyntax left, ExpressionSyntax right, ref TranspileContext context)
    {
        const string temp = GSharpInternalNames.Lowering.CoalesceTemp;
        var model = context.Query.SemanticModel;
        var operand = SkipNullPreservingConversions(left, model);
        if (StackArrayLocals.NeedsView(operand, model))
        {
            context.Visitor.Visit(left);
            return;
        }

        using (context.Emitter.Writer.ImmediatelyInvokedLambda())
        {
            context.Emitter.Writer.Append($"auto {temp} = ");
            context.Visitor.Visit(left);
            context.Emitter.Writer.AppendLine(";");
            context.Emitter.Writer.Append($"return {temp}.data != nullptr ? {temp} : ");
            context.Visitor.Visit(right);
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
    /// <c>([&amp;]() {
    /// auto&amp; t = &lt;left&gt;; return !t.HasValue ? (t = &lt;right&gt;).Value : t.Value;
    /// })()</c>, whose value has the type C# gives it: the underlying type when <paramref name="node"/> is a
    /// <c>Nullable&lt;T&gt;</c> target whose right side converts to <c>T</c> (#1101), the nullable type otherwise
    /// (<c>!t.HasValue ? (t = &lt;right&gt;) : t</c>). The lambda returns a copy, as C# reads the value once.
    /// </summary>
    /// <remarks>
    /// An array is tested by its <c>data</c> pointer and is the value itself (#880):
    /// <c>return t.data == nullptr ? (t = &lt;right&gt;) : t;</c>.
    /// </remarks>
    public static void AppendCoalesceAssignment(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        const string target = GSharpInternalNames.Lowering.CoalesceTarget;
        var model = context.Query.SemanticModel;
        // No type, no shape: writing the nullable form for an unresolved type would fail the C++ build far from the cause
        var targetType = model.GetTypeSymbol(node.Left) ?? throw new InvalidOperationException(
            $"The type of the ??= target '{node.Left}' did not resolve.");
        var valueType = model.GetTypeSymbol(node) ?? throw new InvalidOperationException(
            $"The type of the ??= expression '{node}' did not resolve.");
        var isArray = targetType.IsArrayLikeType();
        var valueIsUnderlying = !isArray && !valueType.IsEqualTo(targetType) &&
                                valueType.IsEqualTo(targetType.UnwrapNullable());
        var nullTest = isArray ? $"{target}.data == nullptr" : $"!{target}.HasValue";
        var valueSuffix = valueIsUnderlying ? ".Value" : "";

        using (context.Emitter.Writer.ImmediatelyInvokedLambda(isArray ? "decltype(auto)" : null))
        {
            context.Emitter.Writer.Append($"auto& {target} = ");
            context.Visitor.Visit(node.Left);
            context.Emitter.Writer.AppendLine(";");

            context.Emitter.Writer.Append($"return {nullTest} ? ");
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Writer.Append($"{target} = ");
                context.Visitor.Visit(node.Right);
            }

            context.Emitter.Writer.AppendLine($"{valueSuffix} : {target}{valueSuffix};");
        }
    }
}
