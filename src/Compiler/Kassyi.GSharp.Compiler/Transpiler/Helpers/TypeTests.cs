namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// What device code does with <c>x is T</c> and <c>x as T</c>: a type test of a value that is not a reference or a
/// nullable is decided by its static type alone, so it is the constant C# gives it (the operand is still evaluated); a
/// test of any other operand, and every <c>as</c>, needs run-time type information and a heap object (GSH0003).
/// </summary>
/// <remarks>
/// A value type has no run-time type that differs from its static type. <c>v is T</c> holds when <c>T</c> is the type
/// itself, its nullable (<c>int is int?</c>), or a type that boxes it (an interface it implements, <c>object</c>,
/// <c>ValueType</c>); <c>int is long</c> is false, as it is in C#, since the test looks at the boxed value.
/// </remarks>
internal static class TypeTests
{
    /// <summary>The GSH0003 text for <c>as</c>, which converts to a reference type or a nullable.</summary>
    public const string AsConversion =
        "'as' conversion (a reference conversion needs run-time type information and a heap object)";

    /// <summary>The GSH0003 text for an <c>is</c> test whose operand is a reference or a nullable.</summary>
    public const string ReferenceOperand =
        "'is' type test of a reference or nullable operand (a run-time type test needs run-time type information)";

    /// <summary>
    /// The constant that <paramref name="node"/>, an <c>is</c> expression, has; <see langword="null"/> when its operand
    /// is a reference, a nullable or a type parameter, which no static type decides.
    /// </summary>
    public static bool? GetConstantResult(BinaryExpressionSyntax node, SemanticModel model)
    {
        if (model.GetTypeSymbol(node.Left) is not { IsValueType: true, TypeKind: not TypeKind.TypeParameter } operand ||
            operand.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ||
            model.GetTypeSymbol(node.Right) is not { } target)
        {
            return null;
        }

        var underlying = target is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
            named
            ? named.TypeArguments[0]
            : target;
        if (underlying.IsEqualTo(operand))
        {
            return true;
        }

        var conversion = model.Compilation.ClassifyConversion(operand, target);
        return conversion.IsBoxing || conversion.IsIdentity;
    }

    /// <summary>
    /// Writes <c>((void)(&lt;operand&gt;), &lt;constant&gt;)</c>: C# evaluates the operand, and the test is decided
    /// by its type.
    /// </summary>
    public static void AppendConstant(BinaryExpressionSyntax node, bool value, ref TranspileContext context)
    {
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.Append("(void)");
            using (context.Emitter.Writer.Parentheses())
            {
                context.Visitor.Visit(node.Left);
            }

            context.Emitter.Writer.Append(value ? ",true" : ",false");
        }
    }
}
