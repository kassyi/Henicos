#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Asserts that a syntax node has the shape of the expected C#, compared as trees with
/// <see cref="SyntaxFactory.AreEquivalent(SyntaxNode, SyntaxNode, bool)"/> (trivia ignored).
/// </summary>
/// <example>
/// <code>
/// SyntaxAssert.Equivalent("Next(ref a)", temps[1].Value);
/// SyntaxAssert.Equivalent(["x += 1;", "x += 2;"], statements);
/// </code>
/// </example>
public static class SyntaxAssert
{
    /// <summary>
    /// Asserts that <paramref name="actual"/> is equivalent to <paramref name="expected"/>, parsed as an expression
    /// when <paramref name="actual"/> is an expression and as a statement when it is a statement.
    /// </summary>
    /// <param name="expected">The expected C#; a statement keeps its <c>;</c>. A parse error fails the test.</param>
    /// <param name="actual">
    /// The node to check: an <see cref="ExpressionSyntax"/> or a <see cref="StatementSyntax"/>.
    /// </param>
    public static void Equivalent(string expected, SyntaxNode actual)
    {
        if (!AreEquivalent(Parse(expected, actual), actual))
        {
            Assert.Fail($"Expected `{expected}`, but found {Describe(actual)}.");
        }
    }

    /// <summary>Asserts that <paramref name="actual"/> is equivalent to <paramref name="expected"/>.</summary>
    public static void Equivalent(SyntaxNode expected, SyntaxNode actual)
    {
        if (!AreEquivalent(expected, actual))
        {
            Assert.Fail($"Expected a node equivalent to {Describe(expected)}, but found {Describe(actual)}.");
        }
    }

    /// <summary>
    /// Asserts that <paramref name="actual"/> has as many nodes as <paramref name="expected"/> and that each node is
    /// equivalent to the C# at the same position (see <see cref="Equivalent(string, SyntaxNode)"/>).
    /// </summary>
    public static void Equivalent(IReadOnlyList<string> expected, IEnumerable<SyntaxNode> actual)
    {
        var nodes = actual.ToArray();
        if (nodes.Length != expected.Count)
        {
            Assert.Fail($"Expected {expected.Count} nodes, but found {nodes.Length}: " +
                        string.Join(", ", nodes.Select(Describe)) + ".");
        }

        for (var i = 0; i < nodes.Length; i++)
        {
            if (!AreEquivalent(Parse(expected[i], nodes[i]), nodes[i]))
            {
                Assert.Fail($"Node {i}: expected `{expected[i]}`, but found {Describe(nodes[i])}.");
            }
        }
    }

    // A rewritten node can store a list in another shape than a parsed one with the same nodes and tokens, and
    // AreEquivalent tells the two apart; NormalizeWhitespace rebuilds both alike
    private static bool AreEquivalent(SyntaxNode expected, SyntaxNode actual) =>
        SyntaxFactory.AreEquivalent(expected.NormalizeWhitespace(), actual.NormalizeWhitespace(), topLevel: false);

    private static SyntaxNode Parse(string expected, SyntaxNode actual)
    {
        SyntaxNode parsed = actual switch
        {
            ExpressionSyntax => SyntaxFactory.ParseExpression(expected),
            StatementSyntax => SyntaxFactory.ParseStatement(expected),
            _ => throw new ArgumentException(
                $"SyntaxAssert compares expressions and statements, not {actual.Kind()}.", nameof(actual))
        };
        if (parsed.ContainsDiagnostics)
        {
            var form = actual is ExpressionSyntax ? "an expression" : "a statement";
            Assert.Fail($"The expected C# `{expected}` does not parse as {form}.");
        }

        return parsed;
    }

    // <kind> at <path or "line">(<line>,<column>), or <kind> outside a source file when the node is not part of a
    // compilation unit
    private static string Describe(SyntaxNode node)
    {
        var root = node;
        while (root.Parent is { } parent)
        {
            root = parent;
        }

        if (root is not CompilationUnitSyntax)
        {
            return $"{node.Kind()} outside a source file (its root is {root.Kind()})";
        }

        var span = node.GetLocation().GetLineSpan();
        var path = span.Path.Length == 0 ? "line" : span.Path;
        return $"{node.Kind()} at {path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1})";
    }
}
