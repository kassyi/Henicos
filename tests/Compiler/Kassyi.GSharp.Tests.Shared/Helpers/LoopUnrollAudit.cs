#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Derives the expected <c>#pragma unroll</c> output and diagnostics for a method straight from its
/// <c>Gpu.Hint.Unroll(...)</c> call sites, then audits the transpiler output against that oracle.
/// The expectation is computed from the source AST, never hardcoded per test case.
/// </summary>
public static class LoopUnrollAudit
{
    private readonly record struct ExpectedHint(string? Pragma, string? DiagnosticId, string? LoopKeyword);

    public static void Audit(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        string cudaSource,
        IReadOnlyCollection<Diagnostic> diagnostics)
    {
        var expected = CollectExpectedHints(method, semanticModel);

        var expectedIds = expected
            .Where(h => h.DiagnosticId is not null)
            .Select(h => h.DiagnosticId!) // Filtered by the preceding Where
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actualIds = diagnostics.Select(d => d.Id).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedIds, actualIds);

        var expectedPragmas = expected.Where(h => h.Pragma is not null).ToArray();
        var lines = cudaSource.Replace("\r", "").Split('\n').Select(l => l.Trim()).ToArray();
        var pragmaIndexes = Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].StartsWith("#pragma unroll", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(expectedPragmas.Length, pragmaIndexes.Length);

        for (int n = 0; n < expectedPragmas.Length; n++)
        {
            int at = pragmaIndexes[n];
            Assert.Equal(expectedPragmas[n].Pragma, lines[at]);
            Assert.True(at + 1 < lines.Length, "A pragma must be followed by a loop.");
            var loopKeyword = expectedPragmas[n].LoopKeyword;
            Assert.NotNull(loopKeyword);
            Assert.StartsWith(loopKeyword, lines[at + 1], StringComparison.Ordinal);
        }
    }

    private static List<ExpectedHint> CollectExpectedHints(MethodDeclarationSyntax method, SemanticModel model)
    {
        var result = new List<ExpectedHint>();

        foreach (var statement in method.DescendantNodes().OfType<ExpressionStatementSyntax>())
        {
            if (statement.Expression is not InvocationExpressionSyntax invocation ||
                model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol
                {
                    Name: "Unroll", ContainingType.Name: "Hint"
                })
            {
                continue;
            }

            var next = NextSibling(statement);
            string? keyword = next switch
            {
                ForStatementSyntax or ForEachStatementSyntax => "for",
                WhileStatementSyntax => "while",
                DoStatementSyntax => "do",
                _ => null
            };

            if (keyword is null)
            {
                result.Add(new ExpectedHint(null, GSharpDiagnostics.UnrollHintMustPrecedeLoop.Id, null));
                continue;
            }

            var args = invocation.ArgumentList.Arguments;
            if (args.Count == 0)
            {
                result.Add(new ExpectedHint("#pragma unroll", null, keyword));
                continue;
            }

            if (model.GetConstantValue(args[0].Expression).Value is int factor && factor >= 0)
            {
                // Factor 0 means "unspecified" and degrades to a bare pragma.
                result.Add(new ExpectedHint(factor == 0 ? "#pragma unroll" : $"#pragma unroll {factor}", null, keyword));
            }
            else
            {
                result.Add(new ExpectedHint(null, GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant.Id, null));
            }
        }

        return result;
    }

    private static StatementSyntax? NextSibling(StatementSyntax statement)
    {
        if (statement.Parent is not BlockSyntax block)
        {
            return null;
        }

        int index = block.Statements.IndexOf(statement);
        return index + 1 < block.Statements.Count ? block.Statements[index + 1] : null;
    }
}
