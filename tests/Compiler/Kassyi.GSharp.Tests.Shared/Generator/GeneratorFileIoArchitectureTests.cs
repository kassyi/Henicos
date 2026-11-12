using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Generator;

[Category(TestCategory.Compiler)]
public class GeneratorFileIoArchitectureTests
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CompilerSources_PathLiterals_ContainNoAbsolutePathLiterals()
    {
        var violations = new List<string>();

        foreach (var tree in RoslynTestUtility.CompilerSourceCompilation.SyntaxTrees)
        {
            var fileName = Path.GetFileName(tree.FilePath);
            foreach (var token in tree.GetRoot().DescendantTokens())
            {
                if (token.Kind() is not (SyntaxKind.StringLiteralToken or SyntaxKind.InterpolatedStringTextToken))
                {
                    continue;
                }

                var text = token.ValueText;
                if (IsAbsolutePathLiteral(text))
                {
                    var line = token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    violations.Add($"{fileName}:{line}: {text}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Compiler sources must not contain absolute path literals:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    // The generator entry point and the one place that writes the AOT artifact
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("GpuKernelGenerator.cs")]
    [InlineData("Aot/AotArtifactWriter.cs")]
    public void GeneratorSources_CatchBlocks_DoNotWriteFiles(string relativePath)
    {
        var generatorPath = Path.GetFullPath(Path.Combine(RepositoryPaths.CompilerProject, relativePath));
        var root = RoslynTestUtility.CompilerSourceCompilation.SyntaxTrees
            .Single(tree =>
                string.Equals(Path.GetFullPath(tree.FilePath), generatorPath, StringComparison.OrdinalIgnoreCase))
            .GetRoot();
        var violations = new List<string>();

        foreach (var catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>())
        {
            foreach (var invocation in catchClause.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                    IsFileAccessExpression(memberAccess.Expression))
                {
                    var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    violations.Add($"{relativePath}:{line}: {invocation}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"{relativePath} catch blocks must not perform file I/O:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    private static bool IsAbsolutePathLiteral(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 3)
        {
            return false;
        }

        var first = text[0];
        var isLetter = (first >= 'A' && first <= 'Z') || (first >= 'a' && first <= 'z');
        return isLetter && text[1] == ':' && (text[2] == '\\' || text[2] == '/');
    }

    private static bool IsFileAccessExpression(ExpressionSyntax expression)
    {
        if (expression is IdentifierNameSyntax id && id.Identifier.ValueText == "File")
        {
            return true;
        }

        // System.IO.File
        return expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "File" };
    }
}
