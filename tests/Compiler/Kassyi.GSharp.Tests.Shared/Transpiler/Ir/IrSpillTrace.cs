#nullable enable
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Lowering;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// What a statement evaluates before its split points, as a list of tokens in the order the lowered code runs them:
/// <c>spill(&lt;source&gt;)</c> (an operand evaluated into a temporary), <c>split(&lt;source&gt;)</c> (a call that
/// splits) and <c>cond{&lt;branch&gt;|&lt;branch&gt;}</c> (a conditional expression written as an <c>if</c>). Read
/// from the output of the syntax-based passes and from the split plan of the IR, so that the two can be compared.
/// </summary>
internal static class IrSpillTrace
{
    /// <summary>
    /// One token, with the span of the source it stands for (for a conditional, of its first inner token).
    /// </summary>
    public readonly record struct Token(string Text, TextSpan Span);

    /// <summary>The tokens of the plan of one statement.</summary>
    public static List<Token> Of(ImmutableArray<IrSpillStep> steps, SourceText text)
    {
        var tokens = new List<Token>();
        foreach (var step in steps)
        {
            tokens.Add(step.Kind switch
            {
                IrSpillStepKind.Spill => Leaf("spill", step.Node.Source.Span, text),
                IrSpillStepKind.Split => Leaf("split", step.Node.Source.Span, text),
                _ => Conditional(step.Branches.Select(branch => Of(branch, text)).ToList(), step.Node.Source.Span)
            });
        }

        return tokens;
    }

    /// <summary>
    /// The tokens of the plan as the syntax-based passes decide it: without the spills they do not make (an earlier
    /// operand, a target part, a compound read, a spill before a split inside a conditional) and with every
    /// conditional written as an <c>if</c> before the rest of its statement or branch, except <c>??</c>, which they do
    /// not lower.
    /// </summary>
    public static List<Token> AsSyntaxPassesDo(ImmutableArray<IrSpillStep> steps, SourceText text)
    {
        var conditionals = new List<Token>();
        var rest = new List<Token>();
        foreach (var step in steps)
        {
            switch (step.Kind)
            {
                case IrSpillStepKind.Spill when IsMadeBySyntaxPasses(step):
                    rest.Add(Leaf("spill", step.Node.Source.Span, text));
                    break;
                case IrSpillStepKind.Split:
                    rest.Add(Leaf("split", step.Node.Source.Span, text));
                    break;
                case IrSpillStepKind.Conditional when step.Node is IrCoalesce:
                    // The syntax passes do not lower ??: its right operand runs unconditionally, in place
                    rest.AddRange(AsSyntaxPassesDo(step.Branches[0], text));
                    break;
                case IrSpillStepKind.Conditional:
                    conditionals.Add(Conditional(
                        step.Branches.Select(branch => AsSyntaxPassesDo(branch, text)).ToList(),
                        step.Node.Source.Span));
                    break;
            }
        }

        return [.. conditionals, .. rest];
    }

    /// <summary>Whether the syntax-based passes make the spill of this step.</summary>
    public static bool IsMadeBySyntaxPasses(IrSpillStep step) =>
        step.Role is IrSpillRole.SplitArgument or IrSpillRole.LeftOperand && !step.ForcedByConditional;

    /// <summary>
    /// The tokens of a list of statements put out by the syntax-based passes, in the order they run. The tokens of
    /// nested statements follow those of the statement that holds them; each token is attributed to its statement by
    /// its span afterwards.
    /// </summary>
    public static List<Token> Of(IEnumerable<StatementSyntax> statements, SourceText text)
    {
        var tokens = new List<Token>();
        var conditionPending = false;
        foreach (var statement in statements)
        {
            if (TempDeclaration(statement) is ({ } name, { } initializer))
            {
                if (name.StartsWith(GSharpInternalNames.Lowering.ArgumentTempPrefix, StringComparison.Ordinal))
                {
                    tokens.Add(Leaf("spill", OriginalSpan(WithoutParentheses(initializer)), text));
                }
                else
                {
                    tokens.AddRange(Splits(initializer, text));
                    conditionPending |= name.StartsWith(GSharpInternalNames.Lowering.ConditionTempPrefix,
                        StringComparison.Ordinal);
                }

                continue;
            }

            if (conditionPending && statement is IfStatementSyntax @if && !HasOriginalSpan(@if))
            {
                conditionPending = false;
                tokens.AddRange(Splits(@if.Condition, text));
                var branches = new List<List<Token>> { Of(StatementsOf(@if.Statement), text) };
                if (@if.Else is { } @else)
                {
                    branches.Add(Of(StatementsOf(@else.Statement), text));
                }

                tokens.Add(Conditional(branches, default));
                continue;
            }

            tokens.AddRange(Splits(statement, text));
            foreach (var nested in NestedStatements(statement))
            {
                tokens.AddRange(Of(StatementsOf(nested), text));
            }
        }

        return tokens;
    }

    /// <summary>The tokens as one line.</summary>
    public static string Print(IEnumerable<Token> tokens) => string.Join(" ", tokens.Select(static t => t.Text));

    private static Token Leaf(string kind, TextSpan span, SourceText text) =>
        new($"{kind}({Squeeze(text.ToString(span))})", span);

    // cond{a|b}; the span is that of the first inner token, which lies in the conditional
    private static Token Conditional(List<List<Token>> branches, TextSpan fallbackSpan)
    {
        var first = branches.SelectMany(static b => b).Select(static t => (TextSpan?)t.Span).FirstOrDefault();
        return new Token("cond{" + string.Join("|", branches.Select(Print)) + "}", first ?? fallbackSpan);
    }

    /// <summary><paramref name="source"/> on one line, each run of white space one space.</summary>
    public static string Squeeze(string source)
    {
        var squeezed = new StringBuilder(source.Length);
        foreach (var c in source)
        {
            if (!char.IsWhiteSpace(c))
            {
                squeezed.Append(c);
            }
            else if (squeezed.Length > 0 && squeezed[^1] != ' ')
            {
                squeezed.Append(' ');
            }
        }

        if (squeezed.Length > 0 && squeezed[^1] == ' ')
        {
            squeezed.Length--;
        }

        return squeezed.ToString();
    }

    // The calls that split in the statement's own expressions (not in nested statements or lambdas), outermost first
    private static IEnumerable<Token> Splits(SyntaxNode node, SourceText text) =>
        node.DescendantNodesAndSelf(child =>
                child == node || child is not (StatementSyntax or LambdaExpressionSyntax
                    or AnonymousMethodExpressionSyntax))
            .OfType<InvocationExpressionSyntax>()
            .Where(static invocation =>
                invocation.HasAnnotations(LoweringAnnotations.SuspendingCallAnnotationKind) ||
                invocation.HasAnnotations(LoweringAnnotations.SyncIntrinsicAnnotationKind))
            .Select(invocation => Leaf("split", OriginalSpanOfNearest(invocation), text));

    // The statements directly below a statement: the bodies of if, else, blocks, loops and switch sections
    private static IEnumerable<StatementSyntax> NestedStatements(StatementSyntax statement) =>
        statement.DescendantNodes(child => child == statement || child is not StatementSyntax)
            .OfType<StatementSyntax>();

    private static IEnumerable<StatementSyntax> StatementsOf(StatementSyntax statement) =>
        statement is BlockSyntax block ? block.Statements : [statement];

    // 'var <name> = <initializer>;' with a name of a temporary of the lowering
    private static (string? Name, ExpressionSyntax? Initializer) TempDeclaration(StatementSyntax statement) =>
        statement is LocalDeclarationStatementSyntax
        {
            Declaration.Variables: [{ Initializer.Value: var initializer } declarator]
        } && declarator.Identifier.ValueText.StartsWith("__gsharp_", StringComparison.Ordinal) &&
        (declarator.Identifier.ValueText.StartsWith(GSharpInternalNames.Lowering.ArgumentTempPrefix,
             StringComparison.Ordinal) ||
         declarator.Identifier.ValueText.StartsWith(GSharpInternalNames.Lowering.WarpTempPrefix,
             StringComparison.Ordinal) ||
         declarator.Identifier.ValueText.StartsWith(GSharpInternalNames.Lowering.CallTempPrefix,
             StringComparison.Ordinal) ||
         declarator.Identifier.ValueText.StartsWith(GSharpInternalNames.Lowering.ConditionTempPrefix,
             StringComparison.Ordinal))
            ? (declarator.Identifier.ValueText, initializer)
            : (null, null);

    // The IR has no node for parentheses, which change nothing in C#
    private static ExpressionSyntax WithoutParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool HasOriginalSpan(SyntaxNode node) =>
        node.HasAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind);

    // A call that a pre-lowering expansion wrote (the barriers of Gpu.SyncGrid(body)) stands for the source it was
    // written for
    private static TextSpan OriginalSpanOfNearest(SyntaxNode node) =>
        OriginalSpan(node.AncestorsAndSelf().First(HasOriginalSpan));

    private static TextSpan OriginalSpan(SyntaxNode node)
    {
        var annotation = node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind).FirstOrDefault();
        return LoweringAnnotations.TryParseOriginalSpanData(annotation?.Data, out var start, out var length, out _,
            out _)
            ? new TextSpan(start, length)
            : throw new InvalidOperationException($"'{node}' has no original span.");
    }
}
