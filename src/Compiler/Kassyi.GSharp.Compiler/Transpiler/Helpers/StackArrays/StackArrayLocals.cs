namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.StackArrays;

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
    /// The element count of the stack array <paramref name="local"/>: the constant size of its
    /// <c>stackalloc</c> / <c>new T[N]</c>, or the length of its initializer.
    /// </summary>
    internal static bool TryGetLength(ILocalSymbol local, Compilation compilation, out int length)
    {
        length = 0;
        if (!IsStackArray(local, out var declarator) ||
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

        if (size != null &&
            compilation.GetCachedSemanticModel(declarator.SyntaxTree).GetSafeConstantValue(size) is
                { HasValue: true, Value: int constant })
        {
            length = constant;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The element count of the stack array that <paramref name="expression"/> names: the constant size of its
    /// <c>stackalloc</c> / <c>new T[N]</c>, or the length of its initializer.
    /// </summary>
    public static bool TryGetLength(ExpressionSyntax expression, SemanticModel semanticModel, out int length)
    {
        length = 0;
        return TryGetStackArrayLocal(expression, semanticModel, out var local) &&
               TryGetLength(local, semanticModel.Compilation, out length);
    }

    /// <summary>
    /// Writes <paramref name="expression"/> as a span: a stack array local becomes
    /// <c>__gsharp_stackalloc_span(name)</c>, a <c>GSharpArray&lt;T&gt;</c> over it, for the receiver of a span member and
    /// for a template parameter that deduces <c>GSharpArray&lt;T&gt;</c> (StackLinq), which a C++ array does not convert to.
    /// </summary>
    public static void AppendView(ExpressionSyntax expression, ref TranspileContext context)
    {
        if (!NeedsView(expression, context.Query.SemanticModel))
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

    /// <summary>
    /// Whether <paramref name="local"/> is a stack array local that is a C++ array, which a span needs a view for.
    /// An empty span local is already a <c>GSharpArray&lt;T&gt;</c>.
    /// </summary>
    internal static bool NeedsView(ILocalSymbol local, Compilation compilation) =>
        IsStackArray(local, out _) &&
        !(TryGetLength(local, compilation, out var length) && length == 0);

    /// <summary>
    /// Whether <paramref name="expression"/> names a stack array local that is a C++ array, which a span needs
    /// <see cref="AppendView"/> for. An empty span local is already a <c>GSharpArray&lt;T&gt;</c>
    /// (<c>VariableDeclarationTranspilerRule</c>), so it is written as it is.
    /// </summary>
    public static bool NeedsView(ExpressionSyntax expression, SemanticModel semanticModel) =>
        TryGetStackArrayLocal(expression, semanticModel, out var local) &&
        NeedsView(local, semanticModel.Compilation);

    /// <summary>
    /// Whether <paramref name="expression"/> (parentheses and <c>Gpu.NotNull</c> aside) names a stack array local.
    /// </summary>
    public static bool IsStackArrayReference(ExpressionSyntax expression, SemanticModel semanticModel) =>
        TryGetStackArrayLocal(expression, semanticModel, out var local) &&
        IsStackArray(local, out _);

    private static bool TryGetStackArrayLocal(ExpressionSyntax expression, SemanticModel semanticModel,
        [NotNullWhen(true)] out ILocalSymbol? local)
    {
        // Parentheses and Gpu.NotNull(a), which returns its argument, name the same stack array
        while (true)
        {
            if (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }
            else if (expression is InvocationExpressionSyntax { ArgumentList.Arguments: [{ } only] } call &&
                     semanticModel.GetSymbol(call) is IMethodSymbol method &&
                     NonNullProof.IsNotNullMethod(method))
            {
                expression = only.Expression;
            }
            else
            {
                break;
            }
        }

        local = expression is IdentifierNameSyntax
            ? semanticModel.GetSymbol(expression) as ILocalSymbol
            : null;
        return local != null;
    }
}
