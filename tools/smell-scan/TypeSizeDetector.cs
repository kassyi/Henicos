using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Measures the code lines and the members of classes, structs, interfaces and records; enums and delegates are not
/// measured. The code lines are those of the whole declaration, nested types included. The members are the methods,
/// constructors, properties, indexers, events, operators and fields declared directly in the type (one for each variable
/// of a field declaration); nested types are measured on their own and do not count. The members of a C# 14 extension
/// block belong to the type that holds the block. The parts of a <c>partial</c> type carry a group name, so that the
/// parts in one project are added up before they are judged.
/// </summary>
internal sealed class TypeSizeDetector : ThresholdSmellDetector
{
    /// <summary>The parameter that holds the largest accepted number of code lines.</summary>
    public const string MaxCodeLines = "max_code_lines";

    /// <summary>The parameter that holds the largest accepted number of members; 0 turns the check off.</summary>
    public const string MaxMembers = "max_members";

    /// <summary>The metric of the code lines of a type.</summary>
    public const string CodeLinesMetric = "codeLines";

    /// <summary>The metric of the members of a type.</summary>
    public const string MembersMetric = "members";

    /// <inheritdoc />
    public override string Name => "TypeSize";

    /// <inheritdoc />
    public override string Description => "A type that is large in code lines or in members.";

    /// <inheritdoc />
    public override ImmutableArray<DetectorParameter> Parameters { get; } =
        [new DetectorParameter(MaxCodeLines, 400, 1), new DetectorParameter(MaxMembers, 30, 0)];

    /// <inheritdoc />
    public override int ThresholdOf(Measurement measurement, RuleParameters parameters) =>
        parameters.Get(measurement.Metric == MembersMetric ? MaxMembers : MaxCodeLines);

    /// <inheritdoc />
    public override bool Exceeds(Measurement measurement, RuleParameters parameters)
    {
        var threshold = ThresholdOf(measurement, parameters);
        if (measurement.Metric == MembersMetric && threshold == 0)
        {
            return false;
        }

        return measurement.Value > threshold;
    }

    /// <inheritdoc />
    public override void Measure(DetectorContext context, MeasurementSink sink)
    {
        foreach (var node in context.Root.DescendantNodes())
        {
            if (node is not TypeDeclarationSyntax type || IsExtensionBlock(type))
            {
                continue;
            }

            var symbol = SyntaxSymbolNamer.Name(type);
            var range = SourceRange.From(context.Text, type);
            var group = type.Modifiers.Any(SyntaxKind.PartialKeyword) ? SyntaxSymbolNamer.PartialTypeKey(type) : null;
            var position = UnitAnchor.Of(type);
            sink.Add(new Measurement(
                Name,
                CodeLinesMetric,
                context.Path,
                range,
                symbol,
                CodeLineCounter.Count(context.Text, type),
                CodeLineCounter.PhysicalLines(context.Text, type),
                position,
                group));
            sink.Add(new Measurement(
                Name,
                MembersMetric,
                context.Path,
                range,
                symbol,
                CountMembers(type.Members),
                0,
                position,
                group));
        }
    }

    private static bool IsExtensionBlock(TypeDeclarationSyntax type) => type.Identifier.Text.Length == 0;

    private static int CountMembers(SyntaxList<MemberDeclarationSyntax> members)
    {
        var count = 0;
        foreach (var member in members)
        {
            count += member switch
            {
                FieldDeclarationSyntax field => field.Declaration.Variables.Count,
                EventFieldDeclarationSyntax eventField => eventField.Declaration.Variables.Count,
                TypeDeclarationSyntax block when IsExtensionBlock(block) => CountMembers(block.Members),
                BaseTypeDeclarationSyntax or DelegateDeclarationSyntax => 0,
                _ => 1,
            };
        }

        return count;
    }
}
