namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.StackArrays;

/// <summary>
/// The targets and values of an assignment: a tuple or a deconstructing declaration on the left pairs element by element,
/// and a target is an expression or the designation of a declared local.
/// </summary>
internal static class AssignmentPairs
{
    public static List<(SyntaxNode Target, ExpressionSyntax Value)> Pairs(AssignmentExpressionSyntax assignment)
    {
        var pairs = new List<(SyntaxNode, ExpressionSyntax)>();
        AddPairs(assignment.Left, assignment.Right, pairs);
        return pairs;

        static void AddPairs(SyntaxNode target, ExpressionSyntax value, List<(SyntaxNode, ExpressionSyntax)> pairs)
        {
            switch (target)
            {
                case TupleExpressionSyntax targets:
                    for (var i = 0; i < targets.Arguments.Count; i++)
                    {
                        AddPairs(targets.Arguments[i].Expression, ElementOf(value, i), pairs);
                    }

                    break;
                case DeclarationExpressionSyntax declaration:
                    AddPairs(declaration.Designation, value, pairs);
                    break;
                case ParenthesizedVariableDesignationSyntax designations:
                    for (var i = 0; i < designations.Variables.Count; i++)
                    {
                        AddPairs(designations.Variables[i], ElementOf(value, i), pairs);
                    }

                    break;
                case DiscardDesignationSyntax:
                    break;
                default:
                    pairs.Add((target, value));
                    break;
            }
        }

        static ExpressionSyntax ElementOf(ExpressionSyntax value, int index) =>
            value is TupleExpressionSyntax values && index < values.Arguments.Count
                ? values.Arguments[index].Expression
                : value;
    }

    /// <summary>A target without its parentheses; the designation of a single declared local (<c>var x</c>).</summary>
    public static SyntaxNode Unwrap(SyntaxNode node)
    {
        while (true)
        {
            switch (node)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    node = parenthesized.Expression;
                    break;
                case DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax designation }:
                    return designation;
                default:
                    return node;
            }
        }
    }

    /// <summary>The local that a name or a designation stands for.</summary>
    public static ILocalSymbol? LocalOf(SyntaxNode node, SemanticModel model) => node is SingleVariableDesignationSyntax designation
        ? model.GetDeclaredSymbol(designation) as ILocalSymbol
        : model.GetSymbol(node) as ILocalSymbol;
}
