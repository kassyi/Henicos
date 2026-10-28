namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Identifies a declaration instance across AST lowering: its Roslyn symbol (or syntax name when no semantic model is
/// available) together with the 1-based expansion region ordinal (<c>0</c> outside expansions).
/// </summary>
public readonly record struct DeclarationKey(ISymbol? Symbol, string Name, int Region)
{
    public bool Equals(DeclarationKey other)
    {
        if (Region != other.Region)
        {
            return false;
        }

        if (Symbol is not null && other.Symbol is not null)
        {
            return Symbol.IsEqualTo(other.Symbol);
        }

        if (Symbol is null && other.Symbol is null)
        {
            return string.Equals(Name, other.Name, StringComparison.Ordinal);
        }

        return false;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var symbolHash = Symbol is not null
                ? SymbolEqualityComparer.Default.GetHashCode(Symbol)
                : StringComparer.Ordinal.GetHashCode(Name ?? string.Empty);
            return (Region * 397) ^ symbolHash;
        }
    }
}

/// <summary>
/// Resolves declaration and reference identities (<see cref="DeclarationKey"/>) and reads the copy (region) ordinal
/// stamped onto lowered syntax nodes by pre-lowering and inline expansions.
/// </summary>
public static class LoweringIdentity
{
    /// <summary>
    /// Creates the <see cref="DeclarationKey"/> for a declaration syntax node (<see cref="VariableDeclaratorSyntax"/>,
    /// <see cref="LabeledStatementSyntax"/>, <see cref="ParameterSyntax"/>, <see cref="SingleVariableDesignationSyntax"/>,
    /// <see cref="ForEachStatementSyntax"/>, <see cref="LocalFunctionStatementSyntax"/>, or
    /// <see cref="CatchDeclarationSyntax"/>), or for a <see cref="SimpleNameSyntax"/> that lowering synthesized with a
    /// declarator's span (the member name of a hoisted local's initialization, <c>__gsharp_ctx-&gt;x = …</c>).
    /// </summary>
    public static DeclarationKey ForDeclaration(SyntaxNode declaration, SemanticModel? model)
    {
        var name = GetDeclaredName(declaration);
        var symbol = model?.GetDeclaredSymbol<ISymbol>(declaration)?.OriginalDefinition;
        var region = GetRegion(declaration);
        return new DeclarationKey(symbol, name, region);
    }

    /// <summary>
    /// Creates the <see cref="DeclarationKey"/> that <paramref name="reference"/> binds to, falling back to
    /// <see cref="SemanticModelExtensions.GetDeclaredSymbol{T}"/> for references synthesized from a declaration span
    /// (such as spilled <c>out var</c> arguments).
    /// </summary>
    public static DeclarationKey ForReference(SimpleNameSyntax reference, SemanticModel? model)
    {
        var symbol = model != null
            ? (model.GetSymbol(reference) ?? model.GetDeclaredSymbol<ISymbol>(reference))?.OriginalDefinition
            : null;
        var region = GetRegion(reference);
        return new DeclarationKey(symbol, reference.Identifier.ValueText, region);
    }

    /// <summary>
    /// Returns the 1-based region number recorded in the <see cref="LoweringAnnotations.OriginalSpanAnnotationKind"/>
    /// annotation of <paramref name="node"/>, or <c>0</c> when the node has no region.
    /// </summary>
    public static int GetRegion(SyntaxNode node)
    {
        if (!node.HasAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
        {
            return 0;
        }

        foreach (var ann in node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
        {
            if (LoweringAnnotations.TryParseOriginalSpanData(ann.Data, out _, out _, out _, out var region))
            {
                return region > 0 ? region : 0;
            }
        }

        return 0;
    }

    /// <summary>
    /// Whether <paramref name="labeled"/> is a label that lowering added (<c>__gsharp_</c>): GSH0055 rejects <c>__</c> in
    /// user identifiers, so the prefix tells the two kinds apart.
    /// </summary>
    internal static bool IsLoweringLabel(LabeledStatementSyntax labeled) =>
        labeled.Identifier.ValueText.StartsWith(GSharpInternalNames.Lowering.LabelPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Counts the labels of each name that <paramref name="body"/> declares, nested ones included, and lowering's own
    /// labels (<see cref="IsLoweringLabel"/>) only when <paramref name="includeLoweringLabels"/> is set.
    /// </summary>
    internal static Dictionary<string, int> CountLabelNames(SyntaxNode body, bool includeLoweringLabels)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in body.DescendantNodesAndSelf())
        {
            if (node is LabeledStatementSyntax labeled && (includeLoweringLabels || !IsLoweringLabel(labeled)))
            {
                var name = labeled.Identifier.ValueText;
                counts[name] = counts.TryGetValue(name, out var count) ? count + 1 : 1;
            }
        }

        return counts;
    }

    private static string GetDeclaredName(SyntaxNode declaration) => declaration switch
    {
        ParameterSyntax p => p.Identifier.ValueText,
        VariableDeclaratorSyntax v => v.Identifier.ValueText,
        SingleVariableDesignationSyntax d => d.Identifier.ValueText,
        ForEachStatementSyntax f => f.Identifier.ValueText,
        LocalFunctionStatementSyntax l => l.Identifier.ValueText,
        LabeledStatementSyntax l => l.Identifier.ValueText,
        CatchDeclarationSyntax c => c.Identifier.ValueText,
        SimpleNameSyntax s => s.Identifier.ValueText,
        _ => string.Empty
    };
}
