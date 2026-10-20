namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>Spells user identifiers for generated host C#.</summary>
internal static class CSharpIdentifierExtensions
{
    /// <summary>
    /// Returns <paramref name="name"/> as it must be written in generated C#: reserved keywords get the verbatim
    /// <c>@</c> prefix (<c>ISymbol.Name</c> of <c>@class</c> is <c>class</c>).
    /// </summary>
    /// <remarks>Only names emitted on their own need this; derived names such as <c>d_class</c> are never keywords.</remarks>
    public static string ToCSharpIdentifier(this string name) =>
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
}
