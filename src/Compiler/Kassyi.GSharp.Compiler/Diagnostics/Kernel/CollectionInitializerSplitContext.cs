namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Finds a call that splits the CPU state machine inside a collection initializer where C# evaluates another value,
/// an <c>Add</c> call or a constructor before it (GSH0062).
/// </summary>
/// <remarks>
/// [WHY] The intermediate representation does not take a collection initializer apart, so a statement that holds one
/// with a split goes back to the syntax passes, which hoist the split to the start of the statement. C# evaluates the
/// constructor arguments, the constructor, each earlier element with its <c>Add</c> call and each earlier member
/// assignment first; the CPU paths would run them after the split, and CUDA, which does not split, would not. This is
/// a stopgap until the CPU paths run one stack per GPU thread (#2105). A split that is the first value of a creation
/// that the statement evaluates first (a local initializer, an assignment to a local, <c>return</c>, <c>=&gt;</c>) is
/// split as written.
/// </remarks>
internal static class CollectionInitializerSplitContext
{
    /// <summary>
    /// The GSH0062 context of a split that C# evaluates after another part of a collection initializer.
    /// </summary>
    public const string Context =
        "in a collection initializer where C# evaluates another value, an Add call or a constructor before it";

    /// <summary>
    /// Whether <paramref name="call"/> (of the user's tree) stands in a collection initializer where C# evaluates
    /// another value, an <c>Add</c> call or a constructor before it.
    /// </summary>
    public static bool IsReordered(InvocationExpressionSyntax call, SemanticModel model)
    {
        var insideCollection = false;
        SyntaxNode child = call;
        for (var ancestor = call.Parent;
             ancestor is not (null or StatementSyntax or MemberDeclarationSyntax or AnonymousFunctionExpressionSyntax);
             child = ancestor, ancestor = ancestor.Parent)
        {
            switch (ancestor)
            {
                // An earlier element, with its Add call
                case InitializerExpressionSyntax collection
                    when collection.IsKind(SyntaxKind.CollectionInitializerExpression):
                    insideCollection = true;
                    if (IndexOf(collection, child) > 0)
                    {
                        return true;
                    }

                    break;
                // An earlier argument of the same Add, unless it is a constant
                case InitializerExpressionSyntax element
                    when element.IsKind(SyntaxKind.ComplexElementInitializerExpression):
                    insideCollection = true;
                    if (HasEarlierValue(element, child, model))
                    {
                        return true;
                    }

                    break;
                // An earlier member assignment of the object that holds the collection
                case InitializerExpressionSyntax members
                    when insideCollection && members.IsKind(SyntaxKind.ObjectInitializerExpression):
                    if (IndexOf(members, child) > 0)
                    {
                        return true;
                    }

                    break;
                case BaseObjectCreationExpressionSyntax creation when insideCollection && child == creation.Initializer:
                    if (creation.ArgumentList is { Arguments.Count: > 0 } ||
                        model.GetSymbol(creation) is IMethodSymbol { IsImplicitlyDeclared: false })
                    {
                        return true;
                    }

                    // The creation of a member or an element: the initializer around it decides
                    if (IsMemberOrElement(creation))
                    {
                        break;
                    }

                    return !StandsAlone(creation, model);
            }
        }

        return false;
    }

    // The value of a member assignment in an object initializer, or an element of a collection initializer
    private static bool IsMemberOrElement(BaseObjectCreationExpressionSyntax creation) =>
        creation.Parent switch
        {
            AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax members } =>
                members.IsKind(SyntaxKind.ObjectInitializerExpression),
            InitializerExpressionSyntax elements =>
                elements.IsKind(SyntaxKind.CollectionInitializerExpression) ||
                elements.IsKind(SyntaxKind.ComplexElementInitializerExpression),
            _ => false
        };

    private static int IndexOf(InitializerExpressionSyntax initializer, SyntaxNode child) =>
        child is ExpressionSyntax expression ? initializer.Expressions.IndexOf(expression) : -1;

    // Whether an argument before child in { a, b } is not a constant
    private static bool HasEarlierValue(InitializerExpressionSyntax element, SyntaxNode child, SemanticModel model)
    {
        var index = IndexOf(element, child);
        for (var i = 0; i < index; i++)
        {
            if (!model.GetConstantValue(element.Expressions[i]).HasValue)
            {
                return true;
            }
        }

        return false;
    }

    // Whether the statement evaluates creation first: the value of the first declarator of a local declaration, the
    // right side of an assignment statement to a local or a by-value parameter, the value of a return or of a =>
    private static bool StandsAlone(BaseObjectCreationExpressionSyntax creation, SemanticModel model) =>
        creation.Parent switch
        {
            EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax declarator and
                {
                    Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax } declaration
                }
            } => declaration.Variables[0] == declarator,
            AssignmentExpressionSyntax { Parent: ExpressionStatementSyntax } assignment =>
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assignment.Left is IdentifierNameSyntax &&
                model.GetSymbol(assignment.Left) is ILocalSymbol { RefKind: RefKind.None } or
                    IParameterSymbol { RefKind: RefKind.None },
            ReturnStatementSyntax or ArrowExpressionClauseSyntax => true,
            _ => false
        };
}
