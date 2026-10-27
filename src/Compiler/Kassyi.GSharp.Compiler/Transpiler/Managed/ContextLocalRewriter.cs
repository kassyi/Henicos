namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Turns context accesses to ref struct locals back into locals, and renames the accesses to locals that share their
/// name with another hoisted local (see <see cref="ManagedContextFields"/>). Accesses are matched to declarations by
/// <see cref="DeclarationKey"/> (symbol and region).
/// </summary>
internal sealed class ContextLocalRewriter(
    HashSet<DeclarationKey> unhoistedKeys,
    Dictionary<DeclarationKey, string> renamedLocals,
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

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var visited = base.VisitMemberAccessExpression(node);
        if (!IsContextPointerAccess(visited) ||
            visited is not MemberAccessExpressionSyntax memberAccess)
        {
            return visited;
        }

        var key = LoweringIdentity.ForReference(memberAccess.Name, semanticModel);
        if (key.Symbol == null)
        {
            return memberAccess;
        }

        SimpleNameSyntax name = renamedLocals.TryGetValue(key, out var renamed)
            ? SyntaxFactory.IdentifierName(renamed)
            : memberAccess.Name;
        return unhoistedKeys.Contains(key)
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
            // Only the declarator-derived initialization resolves through GetDeclaredSymbol; later assignments stay.
            // The member name carries the declarator's span and region, so it keys like the declarator itself.
            var declaredKey = LoweringIdentity.ForDeclaration(memberAccess.Name, semanticModel);
            if (declaredKey.Symbol is { } declaredSymbol && unhoistedKeys.Contains(declaredKey) &&
                !PlacesDynamicSharedMemory(assignment.Right))
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

    // The initialization of a dynamic SharedMemory<T> stays where it is, as an assignment to the local that each resume
    // declares: it places the region in the order the kernel reaches the declarations, as on CUDA (#544)
    private bool PlacesDynamicSharedMemory(ExpressionSyntax value) =>
        value is BaseObjectCreationExpressionSyntax creation &&
        semanticModel.GetTypeSymbol(creation).IsSharedMemoryType() &&
        DynamicSharedMemoryAnalysis.TryGetCount(creation, semanticModel, out _, out var constantCount) &&
        constantCount == null;
}
