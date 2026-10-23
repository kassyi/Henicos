using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Generator;

[Category(TestCategory.Compiler)]
public class GeneratorFileIoArchitectureTests
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_CompilerSources_ContainNoAbsolutePathLiterals()
    {
        var compilerRoot = RepositoryPaths.CompilerProject;
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(compilerRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var fileName = Path.GetFileName(path);
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();

            foreach (var token in root.DescendantTokens())
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
    public void Test_GeneratorSources_CatchBlocksDoNotWriteFiles(string relativePath)
    {
        var compilerRoot = RepositoryPaths.CompilerProject;
        var generatorPath = Path.Combine(compilerRoot, relativePath);
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(generatorPath)).GetRoot();
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

        if (expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "File" })
        {
            return true;
        }

        var text = expression.ToString();
        return text == "File" || text == "System.IO.File";
    }
}
