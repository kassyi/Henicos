namespace Kassyi.GSharp.Compiler.Semantics;

/// <summary>
/// Reads the declared element count or size expression of a stack array local for <c>Length</c> folding.
/// </summary>
internal static class StackArraySizeQuery
{
    /// <summary>
    /// The size that the declaration of <paramref name="local"/> gives, for its <c>Length</c>: the syntax path writes
    /// it in place of the member.
    /// </summary>
    internal static bool TryGetStackArraySize(ILocalSymbol local, Compilation compilation, out StackArraySize size)
    {
        size = default;
        return local.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var declarator) &&
               TryGetDeclaredSize(local, compilation.GetCachedSemanticModel(declarator.SyntaxTree), out size);
    }

    internal static bool TryGetStackArraySize(MemberAccessExpressionSyntax node, SemanticModel semanticModel,
        out StackArraySize size)
    {
        size = default;
        return node.Name.Identifier.Text == "Length" &&
               semanticModel.GetSymbol(node.Expression) is ILocalSymbol local &&
               TryGetDeclaredSize(local, semanticModel, out size);
    }

    private static bool TryGetDeclaredSize(ILocalSymbol local, SemanticModel semanticModel, out StackArraySize size)
    {
        size = default;
        if (!local.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var declarator) ||
            declarator.Initializer == null ||
            !InitializationSyntaxExtractor.TryGetArrayInitialization(declarator.Initializer.Value, out var count,
                out var sizeExpression, out _))
        {
            return false;
        }

        if (count.HasValue)
        {
            size = new StackArraySize(count, null);
            return true;
        }

        if (sizeExpression == null)
        {
            return false;
        }

        // A constant size is written as its value, as the declaration writes it: a const local it names is not
        // declared again when the state machine resumes
        size = semanticModel.GetSafeConstantValue(sizeExpression) is { HasValue: true, Value: int constant }
            ? new StackArraySize(constant, null)
            : new StackArraySize(null, sizeExpression);
        return true;
    }
}

/// <param name="Count">The size when it is known at compile time.</param>
/// <param name="Expression">
/// The size expression of the declaration, when <paramref name="Count"/> is not known.
/// </param>
internal readonly record struct StackArraySize(int? Count, ExpressionSyntax? Expression);
