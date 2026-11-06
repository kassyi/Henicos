using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Finds <c>catch</c> clauses that drop the exception: a handler with no statement (<c>emptyCatch</c>), and a handler
/// whose statements only write a log line (<c>logOnlyCatch</c>), because the program goes on as if nothing had gone wrong.
/// A handler that rethrows, returns, branches, assigns, declares or calls anything other than a logging call does
/// something else with the failure and is not reported. The tests carry the same idea for a broad catch as the analyzer
/// HNT0007; this detector reads the syntax alone and reports every exception type, filtered or not.
/// A handler is judged by its statements alone (this is narrower than the HNT0007 analyzer of the tests, which also reports a
/// handler that only returns or only calls something other than a logger, and skips every type but Exception): a handler
/// that returns, branches, assigns, declares or calls anything but a logger does something with the failure, which keeps the
/// Try pattern (catch { return false; }) of the sources out of the report. A nested block counts for what it holds, and
/// <c>receiver?.LogError(...)</c> is a logging call like the plain one.
/// A logging call is a call on <c>Console</c>, <c>Debug</c>, <c>Trace</c>, <c>Log</c> or a name that contains
/// <c>logger</c>, and a call to a method whose name starts with <c>Log</c>.
/// </summary>
internal sealed class SwallowedExceptionDetector : ThresholdSmellDetector
{
    /// <summary>The metric of a handler without statements.</summary>
    public const string EmptyMetric = "emptyCatch";

    /// <summary>The metric of a handler that only logs.</summary>
    public const string LogOnlyMetric = "logOnlyCatch";

    /// <inheritdoc />
    public override string Name => "SwallowedException";

    /// <inheritdoc />
    public override string Description => "A catch clause that drops the exception without handling it.";

    /// <inheritdoc />
    public override ImmutableArray<DetectorParameter> Parameters { get; } = [];

    /// <inheritdoc />
    public override int ThresholdOf(Measurement measurement, RuleParameters parameters) => 0;

    /// <inheritdoc />
    public override void Measure(DetectorContext context, MeasurementSink sink)
    {
        foreach (var node in context.Root.DescendantNodes())
        {
            if (node is not CatchClauseSyntax clause)
            {
                continue;
            }

            var metric = Classify(clause.Block);
            if (metric is null)
            {
                continue;
            }

            sink.Add(new Measurement(
                Name,
                metric,
                context.Path,
                SourceRange.From(context.Text, clause),
                SyntaxSymbolNamer.Name(clause),
                1,
                CodeLineCounter.PhysicalLines(context.Text, clause),
                clause.CatchKeyword.SpanStart,
                NodeKey: SyntaxText.Hash(clause)));
        }
    }

    private static string? Classify(BlockSyntax block)
    {
        var leaves = new List<StatementSyntax>();
        CollectLeaves(block, leaves);
        if (leaves.Count == 0)
        {
            return EmptyMetric;
        }

        foreach (var statement in leaves)
        {
            if (!IsLogStatement(statement))
            {
                return null;
            }
        }

        return LogOnlyMetric;
    }

    // Empty statements and nested blocks carry no code of their own: { { } } is as empty as { }.
    private static void CollectLeaves(BlockSyntax block, List<StatementSyntax> leaves)
    {
        foreach (var statement in block.Statements)
        {
            switch (statement)
            {
                case EmptyStatementSyntax:
                    break;
                case BlockSyntax inner:
                    CollectLeaves(inner, leaves);
                    break;
                default:
                    leaves.Add(statement);
                    break;
            }
        }
    }

    private static bool IsLogStatement(StatementSyntax statement) => statement is ExpressionStatementSyntax expression
        && expression.Expression switch
        {
            InvocationExpressionSyntax invocation => IsLogCall(invocation.Expression),
            ConditionalAccessExpressionSyntax { WhenNotNull: InvocationExpressionSyntax invocation } access =>
                IsConditionalLogCall(access.Expression, invocation.Expression),
            _ => false,
        };

    // receiver?.Method(...): the callee after the ?. is a member binding, so the receiver sits in the access.
    private static bool IsConditionalLogCall(ExpressionSyntax receiver, ExpressionSyntax callee) =>
        callee is MemberBindingExpressionSyntax binding
        && (StartsWithLog(binding.Name.Identifier.Text) || IsLoggerReceiver(receiver));

    private static bool IsLogCall(ExpressionSyntax callee) => callee switch
    {
        IdentifierNameSyntax bare => StartsWithLog(bare.Identifier.Text),
        MemberAccessExpressionSyntax access =>
            StartsWithLog(access.Name.Identifier.Text) || IsLoggerReceiver(access.Expression),
        _ => false,
    };

    private static bool IsLoggerReceiver(ExpressionSyntax receiver)
    {
        while (true)
        {
            switch (receiver)
            {
                case MemberAccessExpressionSyntax inner:
                    if (IsLoggerName(inner.Name.Identifier.Text))
                    {
                        return true;
                    }

                    receiver = inner.Expression;
                    break;
                case IdentifierNameSyntax name:
                    return IsLoggerName(name.Identifier.Text);
                default:
                    return false;
            }
        }
    }

    private static bool StartsWithLog(string name) =>
        name.StartsWith("Log", StringComparison.Ordinal) && (name.Length == 3 || char.IsUpper(name[3]));

    private static bool IsLoggerName(string name) =>
        name is "Console" or "Debug" or "Trace" or "Log"
        || name.Contains("logger", StringComparison.OrdinalIgnoreCase);
}
