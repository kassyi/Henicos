using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Measures the sections of <c>switch</c> statements and the arms of <c>switch</c> expressions. A switch whose sections
/// or arms are all table entries is a lookup table and is not measured; a switch over an enum is measured like any
/// other. The test is on the syntax, not on how the source is laid out, so reformatting never changes it.
/// A section of a switch statement is a table entry when, with its blocks opened and the jump that ends it
/// (<c>break</c>, <c>continue</c>, <c>goto</c>, <c>return</c>, <c>throw</c>) left out, it holds at most one statement; the
/// statement counts through the bodies of <c>if</c>, loops, <c>try</c>, <c>lock</c> and <c>using</c>. So
/// <c>case 1: { return 1; }</c> and <c>case 1: x = 1; return true;</c> are table entries (one piece of work and the return)
/// and <c>case 1: x = 1; y = 2; return true;</c> is not. Labels that fall through to the section are not statements. An arm of a switch expression is a table entry when its expression holds no
/// lambda with a block body and no nested switch expression.
/// </summary>
internal sealed class LargeSwitchDetector : ThresholdSmellDetector
{
    /// <summary>The parameter that holds the largest accepted number of sections or arms.</summary>
    public const string MaxCases = "max_cases";

    /// <inheritdoc />
    public override string Name => "LargeSwitch";

    /// <inheritdoc />
    public override string Description => "A switch with many sections or arms that is more than a table of one-statement cases.";

    /// <inheritdoc />
    public override ImmutableArray<DetectorParameter> Parameters { get; } =
        [new DetectorParameter(MaxCases, 6, 1)];

    /// <inheritdoc />
    public override int ThresholdOf(Measurement measurement, RuleParameters parameters) => parameters.Get(MaxCases);

    /// <inheritdoc />
    public override void Measure(DetectorContext context, MeasurementSink sink)
    {
        foreach (var node in context.Root.DescendantNodes())
        {
            switch (node)
            {
                case SwitchStatementSyntax statement when !IsTable(statement):
                    Add(context, sink, node, statement.Sections.Count);
                    break;
                case SwitchExpressionSyntax expression when !IsTable(expression):
                    Add(context, sink, node, expression.Arms.Count);
                    break;
            }
        }
    }

    private void Add(DetectorContext context, MeasurementSink sink, SyntaxNode node, int cases) =>
        sink.Add(new Measurement(
            Name,
            "cases",
            context.Path,
            SourceRange.From(context.Text, node),
            SyntaxSymbolNamer.Name(node),
            cases,
            CodeLineCounter.PhysicalLines(context.Text, node),
            UnitAnchor.Of(node)));

    private static bool IsTable(SwitchStatementSyntax statement)
    {
        foreach (var section in statement.Sections)
        {
            if (CountSection(section) > 1)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTable(SwitchExpressionSyntax expression)
    {
        foreach (var arm in expression.Arms)
        {
            foreach (var node in arm.Expression.DescendantNodesAndSelf())
            {
                if (node is SwitchExpressionSyntax or AnonymousFunctionExpressionSyntax { Block: not null }
                    or ParenthesizedLambdaExpressionSyntax { Block: not null } or SimpleLambdaExpressionSyntax { Block: not null })
                {
                    return false;
                }
            }
        }

        return true;
    }

    // The statements of a section with its blocks opened. The jump that ends the section (break, continue, goto, return,
    // throw) is free; everything before it counts, so "one piece of work and a return" is still a table entry.
    private static int CountSection(SwitchSectionSyntax section)
    {
        var flat = new List<StatementSyntax>();
        Flatten(section.Statements, flat);
        if (flat.Count > 0 && flat[^1] is BreakStatementSyntax or ContinueStatementSyntax or GotoStatementSyntax
            or ReturnStatementSyntax or ThrowStatementSyntax)
        {
            flat.RemoveAt(flat.Count - 1);
        }

        var count = 0;
        foreach (var statement in flat)
        {
            count += CountStatements(statement);
        }

        return count;
    }

    private static void Flatten(IEnumerable<StatementSyntax> statements, List<StatementSyntax> flat)
    {
        foreach (var statement in statements)
        {
            if (statement is BlockSyntax block)
            {
                Flatten(block.Statements, flat);
            }
            else
            {
                flat.Add(statement);
            }
        }
    }

    // The statements of one flattened section that do work: the bodies of if, loops, try, lock and using
    // are looked through, so the count does not depend on braces.
    private static int CountStatements(StatementSyntax statement)
    {
        switch (statement)
        {
            case EmptyStatementSyntax:
                return 0;
            case BlockSyntax block:
                var total = 0;
                foreach (var inner in block.Statements)
                {
                    total += CountStatements(inner);
                }

                return total;
            case IfStatementSyntax ifStatement:
                return CountStatements(ifStatement.Statement) + (ifStatement.Else is null ? 0 : CountStatements(ifStatement.Else.Statement));
            case ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax or LockStatementSyntax
                or UsingStatementSyntax or CheckedStatementSyntax or UnsafeStatementSyntax or FixedStatementSyntax:
                return CountStatements(InnerOf(statement));
            case TryStatementSyntax tryStatement:
                var inTry = CountStatements(tryStatement.Block);
                foreach (var clause in tryStatement.Catches)
                {
                    inTry += CountStatements(clause.Block);
                }

                return inTry + (tryStatement.Finally is null ? 0 : CountStatements(tryStatement.Finally.Block));
            case LabeledStatementSyntax labeled:
                return CountStatements(labeled.Statement);
            default:
                return 1;
        }
    }

    private static StatementSyntax InnerOf(StatementSyntax statement) => statement switch
    {
        ForStatementSyntax f => f.Statement,
        CommonForEachStatementSyntax f => f.Statement,
        WhileStatementSyntax w => w.Statement,
        DoStatementSyntax d => d.Statement,
        LockStatementSyntax l => l.Statement,
        UsingStatementSyntax u => u.Statement,
        CheckedStatementSyntax c => c.Block,
        UnsafeStatementSyntax u => u.Block,
        FixedStatementSyntax f => f.Statement,
        _ => statement,
    };
}
