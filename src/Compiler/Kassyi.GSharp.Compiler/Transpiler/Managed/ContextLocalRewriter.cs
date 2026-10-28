namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Turns context accesses to the locals that are declared again on every resume (<see cref="ResumeLocals"/>) back into
/// locals, and renames the accesses to locals that share their name with another hoisted local (see
/// <see cref="ManagedContextFields"/>). Accesses are matched to declarations by <see cref="DeclarationKey"/> (symbol and
/// region).
/// </summary>
/// <param name="unhoistedKeys">The declarations of the locals that are declared again on every resume.</param>
/// <param name="renamedLocals">The new names of the locals that share their name with another hoisted local.</param>
/// <param name="semanticModel">The semantic model of the method.</param>
/// <param name="kernelName">The C# name of the method, for internal-error reports.</param>
/// <param name="initialize">
/// The statement that replaces the initialization of an unhoisted local where it is declared, given its key, the
/// reference to the local and the rewritten initializer (<see cref="ResumeLocals.CreateInitialization"/>);
/// <see langword="null"/> from it, or no function, removes the initialization.
/// </param>
internal sealed class ContextLocalRewriter(
    HashSet<DeclarationKey> unhoistedKeys,
    Dictionary<DeclarationKey, string> renamedLocals,
    SemanticModel semanticModel,
    string kernelName,
    Func<DeclarationKey, SimpleNameSyntax, ExpressionSyntax, StatementSyntax?>? initialize = null) : CSharpSyntaxRewriter
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
            if (declaredKey.Symbol is { } declaredSymbol && unhoistedKeys.Contains(declaredKey))
            {
                if (node.Parent is BlockSyntax or SwitchSectionSyntax)
                {
                    if (initialize == null)
                    {
                        return null;
                    }

                    var value = Visit(assignment.Right) as ExpressionSyntax ?? assignment.Right;
                    SimpleNameSyntax name = renamedLocals.TryGetValue(declaredKey, out var renamed)
                        ? SyntaxFactory.IdentifierName(renamed).WithAdditionalAnnotations(memberAccess.Name.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
                        : memberAccess.Name.WithoutTrivia();
                    return initialize(declaredKey, name, value)?.WithTriviaFrom(node);
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
