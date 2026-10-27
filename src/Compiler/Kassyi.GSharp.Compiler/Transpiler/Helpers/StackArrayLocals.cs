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

    /// <summary>Whether <paramref name="expression"/> (parentheses aside) names a stack array local.</summary>
    public static bool IsStackArrayReference(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression is IdentifierNameSyntax &&
               IsStackArray(semanticModel.GetSymbol(expression) as ILocalSymbol, out _);
    }
}
