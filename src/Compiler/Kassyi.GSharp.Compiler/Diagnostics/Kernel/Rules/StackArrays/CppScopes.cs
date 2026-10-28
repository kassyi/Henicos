namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.StackArrays;

/// <summary>
/// The C++ lifetime of a declaration or of a write target: the innermost scope that the emitted C++ braces around it.
/// </summary>
/// <remarks>
/// A scope is a block, a switch section, the body statement of <c>if</c> / <c>else</c> / <c>for</c> / <c>foreach</c> /
/// <c>while</c> / <c>do</c> / <c>using</c> / <c>fixed</c> / <c>lock</c> (each braced in the C++), a <c>for</c> statement
/// for its own declarations, and a lambda or a function for its parameters.
/// </remarks>
internal static class CppScopes
{
    // What a parent node does to the lifetime of what its child declares or allocates
    private enum ScopeEnd
    {
        // The parent does not end it; look further out
        None,

        // The parent is the scope
        Parent,

        // The child itself is the scope (a body statement that the C++ braces)
        Child
    }

    /// <summary>The innermost node whose end ends the C++ lifetime of what is declared at or allocated by <paramref name="node"/>.</summary>
    public static SyntaxNode GetScope(SyntaxNode node)
    {
        var current = node;
        for (var parent = node.Parent; parent != null; current = parent, parent = parent.Parent)
        {
            switch (EndOf(parent, current))
            {
                case ScopeEnd.Parent:
                    return parent;
                case ScopeEnd.Child:
                    return current;
            }
        }

        return current;
    }

    // One row per parent kind
    private static ScopeEnd EndOf(SyntaxNode parent, SyntaxNode child) => parent switch
    {
        BlockSyntax or SwitchSectionSyntax => ScopeEnd.Parent,
        ForStatementSyntax forStatement => forStatement.Statement == child ? ScopeEnd.Child : ScopeEnd.Parent,
        IfStatementSyntax ifStatement => BodyEnd(ifStatement.Statement, child),
        ElseClauseSyntax elseClause => BodyEnd(elseClause.Statement, child),
        CommonForEachStatementSyntax forEach => BodyEnd(forEach.Statement, child),
        WhileStatementSyntax whileStatement => BodyEnd(whileStatement.Statement, child),
        DoStatementSyntax doStatement => BodyEnd(doStatement.Statement, child),
        UsingStatementSyntax usingStatement => BodyEnd(usingStatement.Statement, child),
        FixedStatementSyntax fixedStatement => BodyEnd(fixedStatement.Statement, child),
        LockStatementSyntax lockStatement => BodyEnd(lockStatement.Statement, child),
        AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax
            or AccessorDeclarationSyntax => ScopeEnd.Parent,
        _ => ScopeEnd.None
    };

    // The body statement of a statement is a scope of its own; the condition or header is not
    private static ScopeEnd BodyEnd(StatementSyntax body, SyntaxNode child) => body == child ? ScopeEnd.Child : ScopeEnd.None;

    /// <summary>A value parameter lives as long as the body of its function: the scope of the locals declared at its top.</summary>
    public static SyntaxNode BodyScope(SyntaxNode parameter)
    {
        var function = GetScope(parameter);
        return function switch
        {
            BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody ?? function,
            LocalFunctionStatementSyntax localFunction =>
                (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody ?? function,
            AnonymousFunctionExpressionSyntax lambda => lambda.Body,
            _ => function
        };
    }

    /// <summary>
    /// The scope that a write to <paramref name="target"/> lives in, or null for memory outside the method (a parameter
    /// passed by reference, a field of a reference, an element of an array the method did not create, a ref local, *p).
    /// </summary>
    public static SyntaxNode? TargetScope(SyntaxNode target, SemanticModel model, StorageGatherer gatherer)
    {
        var current = AssignmentPairs.Unwrap(target);
        switch (current)
        {
            case SingleVariableDesignationSyntax designation:
                return GetScope(designation);
            case MemberAccessExpressionSyntax member when gatherer.TypeOf(member.Expression) is { IsValueType: true }:
                return TargetScope(member.Expression, model, gatherer);
            case ElementAccessExpressionSyntax element:
                return StorageGatherer.TryFirst(gatherer.StorageOf(element.Expression), out var storage) ? storage.Scope : null;
        }

        return model.GetSymbol(current) switch
        {
            ILocalSymbol { IsRef: false } local when local.TryGetDeclaringSyntax<SyntaxNode>(out var declaration) =>
                declaration is CommonForEachStatementSyntax ? declaration : GetScope(declaration),
            IParameterSymbol { RefKind: RefKind.None } parameter
                when parameter.TryGetDeclaringSyntax<SyntaxNode>(out var declaration) => BodyScope(declaration),
            _ => null
        };
    }

    public static bool IsProperAncestor(SyntaxNode outer, SyntaxNode inner)
    {
        for (var current = inner.Parent; current != null; current = current.Parent)
        {
            if (current == outer)
            {
                return true;
            }
        }

        return false;
    }
}
