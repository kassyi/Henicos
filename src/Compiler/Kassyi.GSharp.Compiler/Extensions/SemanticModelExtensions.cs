namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>Provides zero-allocation extension methods for Roslyn <see cref="SemanticModel"/> queries.</summary>
internal static class SemanticModelExtensions
{
    /// <summary>
    /// Gets the resolved symbol for the specified syntax node, or null if unresolvable.
    /// </summary>
    public static ISymbol? GetSymbol(this SemanticModel semanticModel, SyntaxNode node, CancellationToken cancellationToken = default) =>
        semanticModel.GetSymbolInfo(node, cancellationToken).Symbol;

    /// <summary>
    /// Gets the resolved type symbol for the specified syntax node, or null if unresolvable.
    /// </summary>
    public static ITypeSymbol? GetTypeSymbol(this SemanticModel semanticModel, SyntaxNode node, CancellationToken cancellationToken = default) =>
        semanticModel.GetTypeInfo(node, cancellationToken).Type;
}
