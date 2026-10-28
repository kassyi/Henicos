namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Turns context accesses to ref struct locals back into locals, and renames the accesses to locals that share their
/// name with another hoisted local (see <see cref="ManagedContextFields"/>). Accesses are matched to declarations by
/// <see cref="DeclarationKey"/> (symbol and region). Labels that share their name with another label of the body are
/// renamed with their gotos (<c>__gsharp_label_&lt;ordinal&gt;_&lt;name&gt;</c>).
/// </summary>
internal sealed class ContextLocalRewriter(
    HashSet<DeclarationKey> unhoistedKeys,
    Dictionary<DeclarationKey, string> renamedLocals,
    SemanticModel semanticModel,
    string kernelName) : CSharpSyntaxRewriter
{
    private Dictionary<DeclarationKey, string>? _labelRenames;

    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        // The first call visits the whole body
        if (_labelRenames == null && node != null)
        {
            _labelRenames = CreateLabelRenames(node, semanticModel);
        }

        return base.Visit(node);
    }

    // [WHY] StateMachineLowerer keeps a label that no goto in another state reaches in its state, and lowering flattens
    // sibling scopes, so two labels of one name can end up in one block (CS0140) or one inside the other's block
    // (CS0158). Each label of a repeated name gets its own name; a goto finds its label by symbol (C# labels cannot
    // shadow each other, so the symbol is the one the source bound).
    private static Dictionary<DeclarationKey, string> CreateLabelRenames(SyntaxNode body, SemanticModel model)
    {
        // Lowering's own labels are unique by construction (a counter or a region in the name) and stay as they are
        var counts = LoweringIdentity.CountLabelNames(body, includeLoweringLabels: false);

        var renames = new Dictionary<DeclarationKey, string>();
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in body.DescendantNodesAndSelf())
        {
            if (node is not LabeledStatementSyntax labeled || LoweringIdentity.IsLoweringLabel(labeled) ||
                counts[labeled.Identifier.ValueText] < 2)
            {
                continue;
            }

            var name = labeled.Identifier.ValueText;
            var ordinal = ordinals.TryGetValue(name, out var previous) ? previous + 1 : 1;
            ordinals[name] = ordinal;
            renames[LoweringIdentity.ForDeclaration(labeled, model)] =
                $"{GSharpInternalNames.Lowering.ManagedLabelPrefix}{ordinal}_{name}";
        }

        return renames;
    }

    public override SyntaxNode? VisitLabeledStatement(LabeledStatementSyntax node)
    {
        var visited = (LabeledStatementSyntax?)base.VisitLabeledStatement(node) ?? node;
        return _labelRenames != null &&
               _labelRenames.TryGetValue(LoweringIdentity.ForDeclaration(node, semanticModel), out var renamed)
            ? visited.WithIdentifier(SyntaxFactory.Identifier(renamed).WithTriviaFrom(visited.Identifier))
            : visited;
    }

    public override SyntaxNode? VisitGotoStatement(GotoStatementSyntax node)
    {
        var visited = (GotoStatementSyntax?)base.VisitGotoStatement(node) ?? node;
        return node is { RawKind: (int)SyntaxKind.GotoStatement, Expression: IdentifierNameSyntax target } &&
               _labelRenames != null &&
               _labelRenames.TryGetValue(LoweringIdentity.ForReference(target, semanticModel), out var renamed)
            ? visited.WithExpression(SyntaxFactory.IdentifierName(renamed).WithTriviaFrom(target))
            : visited;
    }
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
