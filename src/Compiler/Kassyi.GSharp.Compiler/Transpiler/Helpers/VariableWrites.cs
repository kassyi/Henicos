namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Whether code of the member that declares a local or a parameter can change it after its declaration: an assignment
/// (also of one of its fields, or in a tuple), an increment, a <c>ref</c> / <c>out</c> argument, a <c>ref</c> or address
/// taken of it or of one of its fields, or a call of a struct member that is not readonly on it. Lambdas and local
/// functions of the member are included. An element of an array the variable holds is not the variable.
/// </summary>
internal static class VariableWrites
{
    private static readonly PerCompilationSymbolCache<ISymbol, bool> s_written = new();

    /// <summary>Whether <paramref name="variable"/> may change after its declaration.</summary>
    public static bool IsWritten(ISymbol variable, Compilation compilation)
    {
        return s_written.For(compilation).GetOrAdd(variable, v => FindWrite(v, compilation));
    }

    private static bool FindWrite(ISymbol variable, Compilation compilation)
    {
        var owner = variable.ContainingSymbol;
        while (owner is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
        {
            owner = owner.ContainingSymbol;
        }

        if (owner is not IMethodSymbol { DeclaringSyntaxReferences.Length: > 0 } method)
        {
            return true;
        }

        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var declaration = reference.GetSyntax();
            var model = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
            foreach (var node in declaration.DescendantNodes())
            {
                if (WritesVariable(node, variable, model))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool WritesVariable(SyntaxNode node, ISymbol variable, SemanticModel model) => node switch
    {
        AssignmentExpressionSyntax assignment => TargetsVariable(assignment.Left, variable, model),
        PrefixUnaryExpressionSyntax
        {
            RawKind: (int)SyntaxKind.PreIncrementExpression or (int)SyntaxKind.PreDecrementExpression or
            (int)SyntaxKind.AddressOfExpression
        } prefix => TargetsVariable(prefix.Operand, variable, model),
        PostfixUnaryExpressionSyntax
        {
            RawKind: (int)SyntaxKind.PostIncrementExpression or (int)SyntaxKind.PostDecrementExpression
        } postfix => TargetsVariable(postfix.Operand, variable, model),
        ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword } argument =>
            TargetsVariable(argument.Expression, variable, model),
        RefExpressionSyntax refExpression => TargetsVariable(refExpression.Expression, variable, model),
        // A struct member that is not readonly may write the fields of its receiver
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } invocation =>
            model.GetSymbol(invocation) is IMethodSymbol { IsStatic: false, IsReadOnly: false, ContainingType.IsValueType: true } &&
            TargetsVariable(member.Expression, variable, model),
        _ => false
    };

    // Whether target is the variable or a chain of fields from it (a tuple names each of its elements)
    private static bool TargetsVariable(ExpressionSyntax target, ISymbol variable, SemanticModel model)
    {
        target = SkipParentheses(target);
        if (target is TupleExpressionSyntax tuple)
        {
            foreach (var argument in tuple.Arguments)
            {
                if (TargetsVariable(argument.Expression, variable, model))
                {
                    return true;
                }
            }

            return false;
        }

        while (target is MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression } access)
        {
            target = SkipParentheses(access.Expression);
        }

        return target is IdentifierNameSyntax identifier &&
               identifier.Identifier.ValueText == variable.Name &&
               model.GetSymbol(identifier).IsEqualTo(variable);
    }

    private static ExpressionSyntax SkipParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }
}
