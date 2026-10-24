namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Turns context accesses to ref struct locals back into locals, and renames the accesses to locals that share their
/// name with another hoisted local (see <see cref="ManagedCSharpTranspiler"/>).
/// </summary>
internal sealed class ContextLocalRewriter(
    HashSet<ILocalSymbol> unhoistedSymbols,
    Dictionary<ILocalSymbol, string> renamedLocals,
    SemanticModel semanticModel,
    string kernelName) : CSharpSyntaxRewriter
{
    /// <summary>
    /// Matches <c>__gsharp_ctx-&gt;X</c>, the context access that <see cref="ContextHoister"/> emits.
    /// </summary>
    internal static bool IsContextPointerAccess(SyntaxNode? node) =>
        node is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.Text: GSharpInternalNames.StateMachine.Context }
        } memberAccess &&
        memberAccess.IsKind(SyntaxKind.PointerMemberAccessExpression);

    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) =>
        VisitCreation(node, base.VisitObjectCreationExpression(node));

    public override SyntaxNode?
        VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node) =>
        VisitCreation(node, base.VisitImplicitObjectCreationExpression(node));

    private SyntaxNode? VisitCreation(BaseObjectCreationExpressionSyntax node, SyntaxNode? visited) =>
        visited is BaseObjectCreationExpressionSyntax rewritten &&
        semanticModel.GetTypeSymbol(node).IsSharedMemoryType()
            ? ManagedCSharpTranspiler.AddSharedMemorySiteKey(node, rewritten, semanticModel)
            : visited;

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var visited = base.VisitMemberAccessExpression(node);
        if (!IsContextPointerAccess(visited) ||
            visited is not MemberAccessExpressionSyntax memberAccess)
        {
            return visited;
        }

        var symbol = semanticModel.GetSymbol(memberAccess.Name) as ILocalSymbol ??
                     semanticModel.GetDeclaredSymbol<ILocalSymbol>(memberAccess.Name);
        if (symbol == null)
        {
            return memberAccess;
        }

        SimpleNameSyntax name = renamedLocals.TryGetValue(symbol, out var renamed)
            ? SyntaxFactory.IdentifierName(renamed)
            : memberAccess.Name;
        return unhoistedSymbols.Contains(symbol)
            ? name.WithTriviaFrom(memberAccess)
            : memberAccess.WithName(name);
    }

    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (node.Expression is AssignmentExpressionSyntax
            {
                Left: MemberAccessExpressionSyntax memberAccess
            } assignment &&
            assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
            IsContextPointerAccess(memberAccess))
        {
            // Only the declarator-derived initialization resolves through GetDeclaredSymbol; later assignments stay
            var declaredSymbol = semanticModel.GetDeclaredSymbol<ILocalSymbol>(memberAccess.Name);
            if (declaredSymbol != null && unhoistedSymbols.Contains(declaredSymbol))
            {
                if (node.Parent is BlockSyntax or SwitchSectionSyntax)
                {
                    return null;
                }

#pragma warning disable RS0030 // Statement text for a human-readable internal-error report only
                throw new InvalidOperationException(
                    $"Cannot remove ref-struct initialization statement in kernel '{kernelName}' for local '{declaredSymbol.Name}': statement '{node.ToFullString().Trim()}' is not inside a block or switch section.");
#pragma warning restore RS0030
            }
        }

        return base.VisitExpressionStatement(node);
    }
}
