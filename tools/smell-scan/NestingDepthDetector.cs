using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Measures how deeply <c>if</c>, <c>switch</c>, <c>for</c>, <c>foreach</c>, <c>while</c>, <c>do</c> and <c>try</c>
/// statements are nested. An <c>else if</c> continues its chain and does not add a level. A local function, a lambda
/// and an anonymous method start again at zero and are measured as units of their own. The top-level statements of a file are one unit. The place reported is the first
/// statement, in source order, that reaches the deepest level of the unit.
/// </summary>
internal sealed class NestingDepthDetector : ThresholdSmellDetector
{
    /// <summary>The parameter that holds the largest accepted depth.</summary>
    public const string MaxDepth = "max_depth";

    /// <inheritdoc />
    public override string Name => "NestingDepth";

    /// <inheritdoc />
    public override string ThresholdParameter => MaxDepth;

    /// <inheritdoc />
    public override ImmutableArray<DetectorParameter> Parameters { get; } =
        [new DetectorParameter(MaxDepth, 3, 1)];

    /// <inheritdoc />
    public override void Measure(DetectorContext context, MeasurementSink sink)
    {
        MeasureTopLevel(context, sink);
        foreach (var node in context.Root.DescendantNodes())
        {
            if (!IsUnit(node))
            {
                continue;
            }

            var deepest = new Deepest();
            Walk(node, 0, deepest);
            if (deepest.Node is null)
            {
                continue;
            }

            sink.Add(new Measurement(
                Name,
                "depth",
                context.Path,
                SourceRange.From(context.Text, deepest.Node),
                SyntaxSymbolNamer.Name(node),
                deepest.Depth,
                0,
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

        var deepest = new Deepest();
        foreach (var statement in statements)
        {
            Walk(statement, 0, deepest);
        }

        if (deepest.Node is not null)
        {
            sink.Add(new Measurement(
                Name,
                "depth",
                context.Path,
                SourceRange.From(context.Text, deepest.Node),
                TopLevelStatements.Symbol,
                deepest.Depth,
                0,
                statements[0].SpanStart));
        }
    }

    private static void Walk(SyntaxNode parent, int depth, Deepest deepest)
    {
        foreach (var child in parent.ChildNodes())
        {
            if (IsUnit(child))
            {
                continue;
            }

            var childDepth = depth;
            if (IsNesting(child))
            {
                childDepth++;
                if (childDepth > deepest.Depth)
                {
                    deepest.Depth = childDepth;
                    deepest.Node = child;
                }
            }

            Walk(child, childDepth, deepest);
        }
    }

    private static bool IsUnit(SyntaxNode node) => node switch
    {
        BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax
            or AnonymousFunctionExpressionSyntax => true,
        ArrowExpressionClauseSyntax { Parent: PropertyDeclarationSyntax or IndexerDeclarationSyntax } => true,
        _ => false,
    };

    private static bool IsNesting(SyntaxNode node) => node switch
    {
        IfStatementSyntax ifStatement => ifStatement.Parent is not ElseClauseSyntax,
        SwitchStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax
            or WhileStatementSyntax or DoStatementSyntax or TryStatementSyntax => true,
        _ => false,
    };

    private sealed class Deepest
    {
        public int Depth;
        public SyntaxNode? Node;
    }
}
