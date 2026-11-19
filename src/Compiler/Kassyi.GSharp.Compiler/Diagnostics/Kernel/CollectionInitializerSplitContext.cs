namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Finds a call that splits the CPU state machine in the initializer of a creation that holds a collection
/// initializer, where C# evaluates another value, an <c>Add</c> call or a constructor before it (GSH0062).
/// </summary>
/// <remarks>
/// [WHY] The intermediate representation does not take a collection initializer apart, so a statement with a split in
/// the initializer of a creation that holds one, anywhere, goes back to the syntax passes, which hoist the split to the
/// start of the statement. C# evaluates the constructor arguments, the constructor, each earlier element with its
/// <c>Add</c> call and each earlier member assignment of that creation first; the CPU paths would run them after the
/// split, and CUDA, which does not split, would not. This is a stopgap until the CPU paths run one stack per GPU thread
/// (#2105). A split that is the first value of a creation that the statement evaluates first (a local initializer, an
/// assignment to a local, <c>return</c>, <c>=&gt;</c>) is split as written. A split in the constructor arguments of
/// such a creation is not decided here (#2185).
/// </remarks>
internal static class CollectionInitializerSplitContext
{
    /// <summary>
    /// The GSH0062 context of a split that C# evaluates after another part of a creation with a collection initializer.
    /// </summary>
    public const string Context =
        "in the initializer of a creation with a collection initializer, where C# evaluates another value, an Add " +
        "call or a constructor before it";

    /// <summary>
    /// Whether <paramref name="call"/> (of the user's tree) stands in the initializer of a creation that holds a
    /// collection initializer, where C# evaluates another value, an <c>Add</c> call or a constructor before it.
    /// </summary>
    public static bool IsReordered(InvocationExpressionSyntax call, SemanticModel model)
    {
        if (FindOpaqueCreation(call) is not { } opaque)
        {
            return false;
        }

        // Every initializer and creation from the call up to the opaque creation, which the statement evaluates as one
        SyntaxNode child = call;
        for (var ancestor = call.Parent; ancestor != null; child = ancestor, ancestor = ancestor.Parent)
        {
            if (IsReorderedAt(ancestor, child, opaque, model))
            {
                return true;
            }

            if (ancestor == opaque)
            {
                return false;
            }
        }

        return false;
    }

    // The outermost creation around the call, entered through its initializer, whose initializer holds a collection
    // initializer anywhere: the node the intermediate representation keeps opaque
    private static BaseObjectCreationExpressionSyntax? FindOpaqueCreation(InvocationExpressionSyntax call)
    {
        BaseObjectCreationExpressionSyntax? opaque = null;
        SyntaxNode child = call;
        for (var ancestor = call.Parent;
             ancestor is not (null or StatementSyntax or MemberDeclarationSyntax or AnonymousFunctionExpressionSyntax);
             child = ancestor, ancestor = ancestor.Parent)
        {
            if (ancestor is BaseObjectCreationExpressionSyntax { Initializer: { } initializer } creation &&
                child == initializer &&
                HoldsCollectionInitializer(initializer))
            {
                opaque = creation;
            }
        }

        return opaque;
    }

    private static bool HoldsCollectionInitializer(InitializerExpressionSyntax initializer) =>
        initializer
            .DescendantNodesAndSelf()
            .AsStackEnumerable()
            .Any(static node => node.IsKind(SyntaxKind.CollectionInitializerExpression));

    private static bool IsReorderedAt(SyntaxNode ancestor, SyntaxNode child,
        BaseObjectCreationExpressionSyntax opaque, SemanticModel model) =>
        ancestor switch
        {
            InitializerExpressionSyntax collection when collection.IsKind(SyntaxKind.CollectionInitializerExpression) =>
                IsAfterEarlierElement(collection, child),
            InitializerExpressionSyntax element when element.IsKind(SyntaxKind.ComplexElementInitializerExpression) =>
                HasEarlierArgument(element, child, model),
            InitializerExpressionSyntax members when members.IsKind(SyntaxKind.ObjectInitializerExpression) =>
                IsAfterEarlierMember(members, child),
            BaseObjectCreationExpressionSyntax creation when child == creation.Initializer =>
                IsReorderedByCreation(creation, creation == opaque, model),
            _ => false
        };

    // An earlier element, with its Add call
    private static bool IsAfterEarlierElement(InitializerExpressionSyntax collection, SyntaxNode child) =>
        IndexOf(collection, child) > 0;

    // An earlier argument of the same Add, unless it is a constant
    private static bool HasEarlierArgument(InitializerExpressionSyntax element, SyntaxNode child, SemanticModel model)
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

    // An earlier member assignment of the object being built
    private static bool IsAfterEarlierMember(InitializerExpressionSyntax members, SyntaxNode child) =>
        IndexOf(members, child) > 0;

    // The constructor arguments or a declared constructor; for the opaque creation, anything the statement evaluates
    // before it; a creation inside it that is not a member value or an element, something else of the opaque creation
    private static bool IsReorderedByCreation(BaseObjectCreationExpressionSyntax creation, bool isOpaque,
        SemanticModel model)
    {
        if (creation.ArgumentList is { Arguments.Count: > 0 } ||
            model.GetSymbol(creation) is IMethodSymbol { IsImplicitlyDeclared: false })
        {
            return true;
        }

        return isOpaque ? !StandsAlone(creation, model) : !IsMemberOrElement(creation);
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
