using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>A call, object creation, property/indexer access, conversion, operator, or collection builder that reaches a block barrier.</summary>
/// <param name="Target">The called method, constructor, accessor, conversion operator, or builder method.</param>
/// <param name="Arguments">The arguments for invocations, object creations, and indexers.</param>
/// <param name="Operation">The operation representing the site.</param>
/// <param name="SiteSyntax">The syntax node of the site when it differs from <see cref="IOperation.Syntax"/>.</param>
internal readonly record struct BarrierCallSite(
    IMethodSymbol Target,
    ImmutableArray<IArgumentOperation> Arguments,
    IOperation Operation,
    SyntaxNode? SiteSyntax = null)
{
    public SyntaxNode Site => SiteSyntax ?? Operation.Syntax;
}
