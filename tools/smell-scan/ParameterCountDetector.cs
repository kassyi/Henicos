using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Measures the number of parameters of methods, constructors, operators and local functions that have a body.
/// An <c>override</c> and an explicit interface implementation take the parameters of the member they replace, so they
/// are not measured. The positional parameters of a record describe the shape of the data and are not measured; the primary
/// constructor of a class or struct takes its dependencies and is measured.
/// </summary>
internal sealed class ParameterCountDetector : ThresholdSmellDetector
{
    /// <summary>The parameter that holds the largest accepted number of parameters.</summary>
    public const string MaxParameters = "max_parameters";

    /// <inheritdoc />
    public override string Name => "ParameterCount";

    /// <inheritdoc />
    public override string ThresholdParameter => MaxParameters;

    /// <inheritdoc />
    public override ImmutableArray<DetectorParameter> Parameters { get; } =
        [new DetectorParameter(MaxParameters, 7, 1)];

    /// <inheritdoc />
    public override void Measure(DetectorContext context, MeasurementSink sink)
    {
        foreach (var node in context.Root.DescendantNodes())
        {
            var parameters = ParametersOf(node);
            if (parameters is null)
            {
                continue;
            }

            sink.Add(new Measurement(
                Name,
                "parameters",
                context.Path,
                SourceRange.From(context.Text, PrimaryConstructorSpan(node) ?? node.Span),
                node is TypeDeclarationSyntax type ? SyntaxSymbolNamer.PrimaryConstructorName(type) : SyntaxSymbolNamer.Name(node),
                parameters.Value,
                0,
                UnitAnchor.Of(node)));
        }
    }

    private static TextSpan? PrimaryConstructorSpan(SyntaxNode node) =>
        node is TypeDeclarationSyntax { ParameterList: { } list } type ? TextSpan.FromBounds(type.SpanStart, list.Span.End) : null;

    private static int? ParametersOf(SyntaxNode node)
    {
        switch (node)
        {
            case BaseMethodDeclarationSyntax method:
                var hasBody = method.Body is not null || method.ExpressionBody is not null;
                if (!hasBody || method.Modifiers.Any(SyntaxKind.OverrideKeyword))
                {
                    return null;
                }

                if (method is MethodDeclarationSyntax { ExplicitInterfaceSpecifier: not null })
                {
                    return null;
                }

                return method.ParameterList.Parameters.Count;
            case ClassDeclarationSyntax or StructDeclarationSyntax:
                return ((TypeDeclarationSyntax)node).ParameterList?.Parameters.Count;
            case LocalFunctionStatementSyntax local:
                return local.Body is null && local.ExpressionBody is null ? null : local.ParameterList.Parameters.Count;
            default:
                return null;
        }
    }
}
