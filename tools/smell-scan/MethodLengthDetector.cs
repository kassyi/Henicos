using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Measures the code lines of methods, constructors, operators, local functions, properties, indexers, events and the
/// top-level statements of a file (as one unit). The count includes attributes and the signature. A member includes the
/// local functions and lambdas inside it; a local function is also measured on its own.
/// </summary>
internal sealed class MethodLengthDetector : ThresholdSmellDetector
{
    /// <summary>The parameter that holds the largest accepted number of code lines.</summary>
    public const string MaxCodeLines = "max_code_lines";

    /// <inheritdoc />
    public override string Name => "MethodLength";

    /// <inheritdoc />
    public override string ThresholdParameter => MaxCodeLines;

    /// <inheritdoc />
    public override ImmutableArray<DetectorParameter> Parameters { get; } =
        [new DetectorParameter(MaxCodeLines, 60, 1)];

    /// <inheritdoc />
    public override void Measure(DetectorContext context, MeasurementSink sink)
    {
        MeasureTopLevel(context, sink);
        foreach (var node in context.Root.DescendantNodes())
        {
            if (!IsMeasured(node))
            {
                continue;
            }

            sink.Add(new Measurement(
                Name,
                "codeLines",
                context.Path,
                SourceRange.From(context.Text, node),
                SyntaxSymbolNamer.Name(node),
                CodeLineCounter.Count(context.Text, node),
                CodeLineCounter.PhysicalLines(context.Text, node),
                UnitAnchor.Of(node)));
        }
    }

    private void MeasureTopLevel(DetectorContext context, MeasurementSink sink)
    {
        var statements = TopLevelStatements.Find(context.Root);
        if (statements.Count == 0)
        {
            return;
        }

        var span = TopLevelStatements.Span(statements);
        var tokens = new List<SyntaxToken>();
        foreach (var statement in statements)
        {
            tokens.AddRange(statement.DescendantTokens());
        }

        sink.Add(new Measurement(
            Name,
            "codeLines",
            context.Path,
            SourceRange.From(context.Text, span),
            TopLevelStatements.Symbol,
            CodeLineCounter.Count(context.Text, span, tokens),
            CodeLineCounter.PhysicalLines(context.Text, span),
            statements[0].SpanStart));
    }

    private static bool IsMeasured(SyntaxNode node) => node switch
    {
        BaseMethodDeclarationSyntax method => method.Body is not null || method.ExpressionBody is not null,
        LocalFunctionStatementSyntax local => local.Body is not null || local.ExpressionBody is not null,
        PropertyDeclarationSyntax or IndexerDeclarationSyntax or EventDeclarationSyntax => true,
        _ => false,
    };
}
