namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The locals that the transpiler declares as a C++ stack array (<c>VariableDeclarationTranspilerRule</c>): a local
/// declaration of an array or span type whose initializer creates the array (<c>new T[N]</c>, <c>stackalloc</c>, an array
/// initializer or a collection expression).
/// </summary>
internal static class StackArrayLocals
{
    /// <summary>Whether <paramref name="local"/> is declared as a stack array, and its declarator.</summary>
    public static bool IsStackArray(ILocalSymbol? local, [NotNullWhen(true)] out VariableDeclaratorSyntax? declarator)
    {
        declarator = null;
        return local != null &&
               local.TryGetDeclaringSyntax(out declarator) &&
               declarator is { Initializer.Value: { } value, Parent.Parent: LocalDeclarationStatementSyntax } &&
               InitializationSyntaxExtractor.TryGetArrayElementType(local.Type, out _, out _) &&
               InitializationSyntaxExtractor.TryGetArrayInitialization(value, out _, out _, out _);
    }

    /// <summary>
    /// The element count of the stack array that <paramref name="expression"/> names: the constant size of its
    /// <c>stackalloc</c> / <c>new T[N]</c>, or the length of its initializer.
    /// </summary>
    public static bool TryGetLength(ExpressionSyntax expression, SemanticModel semanticModel, out int length)
    {
        length = 0;
        if (!IsStackArrayReference(expression, semanticModel, out var declarator) ||
            declarator.Initializer is not { } initializer ||
            !InitializationSyntaxExtractor.TryGetArrayInitialization(initializer.Value, out var count, out var size,
                out _))
        {
            return false;
        }

        if (count is { } elements)
        {
            length = elements;
            return true;
        }

        if (size != null && semanticModel.GetSafeConstantValue(size) is { HasValue: true, Value: int constant })
        {
            length = constant;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Writes <paramref name="expression"/> as a span: a stack array local becomes
    /// <c>__gsharp_stackalloc_span(name)</c>, a <c>GSharpArray&lt;T&gt;</c> over it, for the receiver of a span member and
    /// for a template parameter that deduces <c>GSharpArray&lt;T&gt;</c> (StackLinq), which a C++ array does not convert to.
    /// </summary>
    public static void AppendView(ExpressionSyntax expression, ref TranspileContext context)
    {
        // An empty span local is already a GSharpArray<T> (VariableDeclarationTranspilerRule)
        if (!IsStackArrayReference(expression, context.Query.SemanticModel) ||
            TryGetLength(expression, context.Query.SemanticModel, out var length) && length == 0)
        {
            context.Visitor.Visit(expression);
            return;
        }

        context.Emitter.Writer.Append(GSharpInternalNames.Prelude.StackAllocSpan);
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(expression);
        }
    }

    /// <summary>Whether <paramref name="expression"/> (parentheses aside) names a stack array local.</summary>
    public static bool IsStackArrayReference(ExpressionSyntax expression, SemanticModel semanticModel) =>
        IsStackArrayReference(expression, semanticModel, out _);

    private static bool IsStackArrayReference(ExpressionSyntax expression, SemanticModel semanticModel,
        [NotNullWhen(true)] out VariableDeclaratorSyntax? declarator)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        declarator = null;
        return expression is IdentifierNameSyntax &&
               IsStackArray(semanticModel.GetSymbol(expression) as ILocalSymbol, out declarator);
    }
}
