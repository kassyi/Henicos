using System.Collections.Generic;
using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Extensions;
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
    public void Test_ImmutableArray_WhereSelectCount()
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
    [Scenario(TestScenario.Boundary)]
    public void Test_DefaultImmutableArray_IsEmpty()
    {
        bool any = default(ImmutableArray<int>)
            .AsStackEnumerable()
            .Any();

        Assert.False(any);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ReadOnlyList_AnyAll()
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
    public void Test_SyntaxList_SkipsEmptyStatements()
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
    public void Test_SeparatedSyntaxList_ElementAtAndLast()
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
}
