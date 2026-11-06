using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>The top-level statements of a file, measured together as one unit (the compiler's synthesized entry method).</summary>
internal static class TopLevelStatements
{
    /// <summary>The symbol name of the unit.</summary>
    public const string Symbol = "<top-level>";

    /// <summary>Lists the global statements of <paramref name="root"/> in source order.</summary>
    public static List<GlobalStatementSyntax> Find(SyntaxNode root)
    {
        var statements = new List<GlobalStatementSyntax>();
        if (root is CompilationUnitSyntax unit)
        {
            foreach (var member in unit.Members)
            {
                if (member is GlobalStatementSyntax statement)
                {
                    statements.Add(statement);
                }
            }
        }

        return statements;
    }

    /// <summary>Gets the span from the first to the last of <paramref name="statements"/>, which must not be empty.</summary>
    public static TextSpan Span(List<GlobalStatementSyntax> statements) =>
        TextSpan.FromBounds(statements[0].Span.Start, statements[^1].Span.End);
}
