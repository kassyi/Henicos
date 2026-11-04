using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// What the purity and effect checks of a metamorphic rewrite need to know about the kernel being rewritten.
/// </summary>
/// <param name="Model">The semantic model of the tree that holds the kernel.</param>
/// <param name="SafeInputArrays">
/// The one-dimensional array parameters (other than <c>checksum</c>) that the kernel never writes or passes by
/// reference; reading <c>a[gid]</c> from one of them is free of effects.
/// </param>
/// <param name="Gid">The <c>gid</c> local of the guarded prologue, or <see langword="null"/> when the kernel has none.</param>
internal sealed record EffectScope(SemanticModel Model, IReadOnlySet<ISymbol> SafeInputArrays, ISymbol? Gid)
{
    private static readonly IReadOnlySet<ISymbol> s_noArrays = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

    /// <summary>Creates the scope of <paramref name="kernel"/>.</summary>
    internal static EffectScope ForKernel(MethodDeclarationSyntax kernel, SemanticModel model) =>
        new(model, CollectSafeInputArrays(kernel, model), ResolveGuardedGid(kernel, model));

    /// <summary>Creates a scope with no safe input arrays and no <c>gid</c>, for code outside a kernel.</summary>
    internal static EffectScope WithoutInputs(SemanticModel model) => new(model, s_noArrays, null);

    /// <summary>
    /// Returns the <c>gid</c> local when <paramref name="kernel"/> starts with <c>int gid = ...;</c> followed by
    /// the bounds guard on <c>gid</c>.
    /// </summary>
    internal static ISymbol? ResolveGuardedGid(MethodDeclarationSyntax kernel, SemanticModel model)
    {
        if (kernel.Body is null)
        {
            return null;
        }

        var statements = kernel.Body.Statements;
        if (statements.Count < 2 ||
            !FuzzSyntaxHelpers.IsGidDeclaration(statements[0]) ||
            !FuzzSyntaxHelpers.IsGidBoundsGuard(statements[1]) ||
            statements[0] is not LocalDeclarationStatementSyntax gidDecl)
        {
            return null;
        }

        foreach (var v in gidDecl.Declaration.Variables)
        {
            if (string.Equals(v.Identifier.ValueText, "gid", StringComparison.Ordinal))
            {
                return model.GetDeclaredSymbol(v);
            }
        }

        return null;
    }

    private static HashSet<ISymbol> CollectSafeInputArrays(MethodDeclarationSyntax kernel, SemanticModel model)
    {
        var candidates = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var p in kernel.ParameterList.Parameters)
        {
            if (!string.Equals(p.Identifier.ValueText, "checksum", StringComparison.Ordinal) &&
                model.GetDeclaredSymbol(p) is IParameterSymbol
                {
                    RefKind: RefKind.None,
                    Type: IArrayTypeSymbol { Rank: 1 },
                } paramSymbol)
            {
                candidates.Add(paramSymbol);
            }
        }

        if (kernel.Body is null || candidates.Count == 0)
        {
            return candidates;
        }

        foreach (var node in kernel.Body.DescendantNodes())
        {
            if (node is ElementAccessExpressionSyntax elem && MetamorphicPurity.IsWrittenOrByRefElementAccess(elem))
            {
                var arrSymbol = model.GetSymbolInfo(elem.Expression).Symbol;
                if (arrSymbol is null)
                {
                    // An unresolved array target may be any of the parameters.
                    candidates.Clear();
                    break;
                }

                candidates.Remove(arrSymbol);
            }
        }

        return candidates;
    }
}
