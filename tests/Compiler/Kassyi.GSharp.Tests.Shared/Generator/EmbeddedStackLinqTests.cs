using System.Collections.Generic;
using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Helpers;
using Kassyi.ZeroAlloc.Linq.Embedded;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Verifies the netstandard2.0 copy of StackLinq that the compiler builds in, over the collections the compiler uses.
/// </summary>
[Category(TestCategory.Compiler)]
public class EmbeddedStackLinqTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ImmutableArray_WhereSelectCount_ReturnsEvenCountDoubledValuesAndFirstAboveThree()
    {
        var source = ImmutableArray.Create(1, 2, 3, 4, 5);

        int evenCount = source
            .AsStackEnumerable()
            .Where(static x => x % 2 == 0)
            .Count();
        int[] doubled = source
            .AsStackEnumerable()
            .Select(static x => x * 2)
            .Take(3)
            .ToArray();
        int firstAboveThree = source
            .AsStackEnumerable()
            .First(static x => x > 3);

        Assert.Equal(2, evenCount);
        Assert.Equal([2, 4, 6], doubled);
        Assert.Equal(4, firstAboveThree);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EquatableArray_CountAndDefault_HandlesDefaultAndCountsMatching()
    {
        var includes = ImmutableArray.Create("a.h", "b.h", "a.h").AsEquatableArray();

        int count = includes
            .AsStackEnumerable()
            .Count(static s => s == "a.h");
        int defaultCount = default(EquatableArray<string>)
            .AsStackEnumerable()
            .Count();

        Assert.Equal(2, count);
        Assert.Equal(0, defaultCount);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void HashSetAndDictionary_Query_ReturnsExpectedCountsAndMatch()
    {
        var names = new HashSet<string> { "a", "bb", "ccc" };
        var lengths = new Dictionary<string, int> { ["a"] = 1, ["bb"] = 2 };

        int longNames = names
            .AsStackEnumerable()
            .Count(static n => n.Length > 1);
        bool hasTwo = lengths
            .AsStackEnumerable()
            .Any(static kvp => kvp.Value == 2);
        int emptyCount = new HashSet<int>()
            .AsStackEnumerable()
            .Count();

        Assert.Equal(2, longNames);
        Assert.True(hasTwo);
        Assert.Equal(0, emptyCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ImmutableArray_DefaultValue_AnyReturnsFalse()
    {
        bool any = default(ImmutableArray<int>)
            .AsStackEnumerable()
            .Any();

        Assert.False(any);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ReadOnlyList_AnyAndAll_ReturnsTrueForMatchAndFalseOtherwise()
    {
        IReadOnlyList<string> source = new List<string> { "a", "bb", "ccc" };

        bool anyOfLengthTwo = source
            .AsStackEnumerable()
            .Any(static s => s.Length == 2);
        bool allShorterThanThree = source
            .AsStackEnumerable()
            .All(static s => s.Length < 3);

        Assert.True(anyOfLengthTwo);
        Assert.False(allShorterThanThree);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SyntaxList_EmptyStatements_SkipsEmptyStatementsCorrectly()
    {
        var statements = SyntaxFactory.List<StatementSyntax>(
            [SyntaxFactory.EmptyStatement(), SyntaxFactory.ReturnStatement(), SyntaxFactory.EmptyStatement()]
        );

        bool hasExecutable = statements
            .AsStackEnumerable()
            .Any(static s => s is not EmptyStatementSyntax);
        int emptyCount = statements
            .AsStackEnumerable()
            .Count(static s => s.IsKind(SyntaxKind.EmptyStatement));

        Assert.True(hasExecutable);
        Assert.Equal(2, emptyCount);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DescendantNodes_BlockSyntax_QueriesLazilyAndFindsIdentifier()
    {
        var block = SyntaxFactory.Block(
            SyntaxFactory.EmptyStatement(),
            SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("x")),
            SyntaxFactory.EmptyStatement()
        );

        bool hasReturn = block.DescendantNodes()
            .AsStackEnumerable()
            .Any(static n => n is ReturnStatementSyntax);
        int emptyCount = block.DescendantNodes()
            .AsStackEnumerable()
            .Count(static n => n is EmptyStatementSyntax);
        var identifier = block.DescendantNodes()
            .AsStackEnumerable()
            .FirstOrDefault(static n => n is IdentifierNameSyntax);

        Assert.True(hasReturn);
        Assert.Equal(2, emptyCount);
        Assert.IsType<IdentifierNameSyntax>(identifier);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void NodeSequence_FullyIterated_DisposesWhenExhausted()
    {
        var nodes = new DisposeTrackingNodes(SyntaxFactory.EmptyStatement(), SyntaxFactory.EmptyStatement());

        int count = nodes
            .AsStackEnumerable()
            .Count();

        Assert.Equal(2, count);
        Assert.Equal(1, nodes.DisposeCount);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void NodeSequence_EarlyExit_DoesNotDisposeUnderlyingResource()
    {
        var nodes = new DisposeTrackingNodes(SyntaxFactory.EmptyStatement(), SyntaxFactory.EmptyStatement());

        bool any = nodes
            .AsStackEnumerable()
            .Any();

        // The documented limit: an operator that stops early leaves the enumerator undisposed
        Assert.True(any);
        Assert.Equal(0, nodes.DisposeCount);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SyntaxTokenList_TokenSearch_FindsModifierToken()
    {
        var modifiers = SyntaxFactory.TokenList(
            SyntaxFactory.Token(SyntaxKind.PublicKeyword),
            SyntaxFactory.Token(SyntaxKind.RefKeyword)
        );

        bool hasRef = modifiers
            .AsStackEnumerable()
            .Any(static m => m.IsKind(SyntaxKind.RefKeyword));
        int count = default(SyntaxTokenList)
            .AsStackEnumerable()
            .Count();

        Assert.True(hasRef);
        Assert.Equal(0, count);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SeparatedSyntaxList_IndexAccess_ReturnsElementAtAndLast()
    {
        var arguments = SyntaxFactory.SeparatedList(
            [SyntaxFactory.Argument(SyntaxFactory.IdentifierName("a")), SyntaxFactory.Argument(SyntaxFactory.IdentifierName("b"))]
        );

        var second = arguments
            .AsStackEnumerable()
            .ElementAt(1);
        var last = arguments
            .AsStackEnumerable()
            .Last();

        Assert.Same(arguments[1], second);
        Assert.Same(arguments[1], last);
    }

    /// <summary>Yields the given nodes and counts how often its enumerator is disposed.</summary>
    private sealed class DisposeTrackingNodes(params SyntaxNode[] nodes) : IEnumerable<SyntaxNode>
    {
        public int DisposeCount { get; private set; }

        public IEnumerator<SyntaxNode> GetEnumerator()
        {
            try
            {
                foreach (var node in nodes)
                {
                    yield return node;
                }
            }
            finally
            {
                DisposeCount++;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
