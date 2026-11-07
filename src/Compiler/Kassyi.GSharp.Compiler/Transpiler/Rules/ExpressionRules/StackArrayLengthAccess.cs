namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Writes <c>s.Length</c> of a stack array local as the size that its declaration gives.
/// </summary>
internal static class StackArrayLengthAccess
{
    /// <param name="Count">The size when it is known at compile time.</param>
    /// <param name="Expression">
    /// The size expression of the declaration, when <paramref name="Count"/> is not known.
    /// </param>
    private readonly record struct StackArraySize(int? Count, ExpressionSyntax? Expression);

    // s.Length of a stack array local: the size its declaration gives.
    internal static bool TryTranspile(MemberAccessExpressionSyntax node, ref TranspileContext context)
    {
        if (!TryGetStackArraySize(node, context.Query.SemanticModel, out var size))
        {
            return false;
        }

        WriteStackArraySize(size, ref context);
        return true;
    }

    private static bool TryGetStackArraySize(MemberAccessExpressionSyntax node, SemanticModel semanticModel,
        out StackArraySize size)
    {
        size = default;
        if (node.Name.Identifier.Text != "Length" ||
            semanticModel.GetSymbol(node.Expression) is not ILocalSymbol local ||
            !local.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var declarator) ||
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

    private static void WriteStackArraySize(in StackArraySize size, ref TranspileContext context)
    {
        if (size.Count is { } count)
        {
            context.Emitter.Writer.Append(count);
            return;
        }

        var expression = size.Expression ??
                         throw new InvalidOperationException("Stack array size without a count or an expression.");
        context.Visitor.Visit(expression);
    }
}
