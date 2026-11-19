namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The classes of the extension methods that a managed body calls in their reduced form inside a null-conditional
/// access (<c>q?[i].M()</c>, <c>s?.M()</c>), which the generated files of the class import with <c>using static</c>.
/// </summary>
/// <remarks>
/// [WHY] <see cref="ManagedNameQualifier"/> writes any other extension call as the static call with its receiver as
/// the first argument; inside <c>WhenNotNull</c> the receiver is a binding (<c>[i]</c>, <c>.Inner</c>), which cannot
/// stand alone, so the call keeps its reduced form. The generated file has none of the user's <c>using</c> directives,
/// and <c>using static</c> of the class brings its extension methods into scope for the reduced form (CS1061).
/// </remarks>
internal static class ManagedExtensionImports
{
    /// <summary>
    /// The <c>global::</c>-qualified names of those classes for the declarations of <paramref name="type"/>, sorted;
    /// empty when it has none.
    /// </summary>
    public static EquatableArray<string> Get(INamedTypeSymbol type, Compilation compilation)
    {
        var classes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            var tree = reference.SyntaxTree;
            if (!compilation.ContainsSyntaxTree(tree))
            {
                continue;
            }

            // The syntax first: only a file with a null-conditional access needs its semantic model
            var declaration = reference.GetSyntax();
            if (!declaration
                    .DescendantNodes()
                    .AsStackEnumerable()
                    .Any(static node => node is ConditionalAccessExpressionSyntax))
            {
                continue;
            }

            declaration
                .DescendantNodes()
                .AsStackEnumerable()
                .Select(compilation.GetCachedSemanticModel(tree), static (node, model) => GetImportedClass(node, model))
                .OfType<string>()
                .AddTo(classes);
        }

        return classes.Count == 0
            ? default
            : ImmutableArray.CreateRange(classes).AsEquatableArray();
    }

    // The class of a reduced extension call inside the WhenNotNull of a null-conditional access, or null
    private static string? GetImportedClass(SyntaxNode node, SemanticModel model) =>
        node is InvocationExpressionSyntax call &&
        IsInWhenNotNull(call) &&
        model.GetSymbol(call) is IMethodSymbol { ReducedFrom.ContainingType: { IsFileLocal: false } container }
            ? container.GetFullyQualifiedCsharpName()
            : null;

    private static bool IsInWhenNotNull(SyntaxNode node)
    {
        for (var current = node; current.Parent is { } parent; current = parent)
        {
            if (parent is ConditionalAccessExpressionSyntax access && access.WhenNotNull == current)
            {
                return true;
            }
        }

        return false;
    }
}
