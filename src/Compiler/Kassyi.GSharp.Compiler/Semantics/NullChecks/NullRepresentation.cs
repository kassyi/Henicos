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

        var isNullableStruct = GetNullForm(model.GetTypeSymbol(operand)) == NullForm.NullableValue;

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

        var castResult = CoalesceCastsToResult(leftType, rightType, resultType);

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
        var valueIsUnderlying = CoalesceAssignmentReadsUnderlying(targetType, valueType);
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
