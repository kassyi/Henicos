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

        Assert.Equal(2, source.AsStackEnumerable().Where(static x => x % 2 == 0).Count());
        Assert.Equal([2, 4, 6], source.AsStackEnumerable().Select(static x => x * 2).Take(3).ToArray());
        Assert.Equal(4, source.AsStackEnumerable().First(static x => x > 3));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_DefaultImmutableArray_IsEmpty()
    {
        Assert.False(default(ImmutableArray<int>).AsStackEnumerable().Any());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ReadOnlyList_AnyAll()
    {
        IReadOnlyList<string> source = new List<string> { "a", "bb", "ccc" };

        Assert.True(source.AsStackEnumerable().Any(static s => s.Length == 2));
        Assert.False(source.AsStackEnumerable().All(static s => s.Length < 3));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SyntaxList_SkipsEmptyStatements()
    {
        var statements = SyntaxFactory.List<StatementSyntax>(
            [SyntaxFactory.EmptyStatement(), SyntaxFactory.ReturnStatement(), SyntaxFactory.EmptyStatement()]
        );

        Assert.True(statements.AsStackEnumerable().Any(static s => s is not EmptyStatementSyntax));
        Assert.Equal(2, statements.AsStackEnumerable().Count(static s => s.IsKind(SyntaxKind.EmptyStatement)));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SeparatedSyntaxList_ElementAtAndLast()
    {
        var arguments = SyntaxFactory.SeparatedList(
            [SyntaxFactory.Argument(SyntaxFactory.IdentifierName("a")), SyntaxFactory.Argument(SyntaxFactory.IdentifierName("b"))]
        );

        Assert.Same(arguments[1], arguments.AsStackEnumerable().ElementAt(1));
        Assert.Same(arguments[1], arguments.AsStackEnumerable().Last());
    }
}
