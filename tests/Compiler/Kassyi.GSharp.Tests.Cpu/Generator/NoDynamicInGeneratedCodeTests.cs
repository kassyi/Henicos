using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Generator;

[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class NoDynamicInGeneratedCodeTests
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void VerifiedSnapshots_AllGeneratedCode_ContainsNoDynamicIdentifier()
    {
        var testsDirectory = RepositoryPaths.Combine("tests");
        var verifiedFiles = Directory.EnumerateFiles(testsDirectory, "*.verified.cs", SearchOption.AllDirectories);

        var violations = new List<string>();

        foreach (var file in verifiedFiles)
        {
            // The snapshots add up to tens of MB; only a file with a dynamic identifier token (not one in a comment or a
            // string) is parsed into a tree, and no tree outlives its file.
            var code = File.ReadAllText(file);
            if (!HasDynamicIdentifier(code))
            {
                continue;
            }

#pragma warning disable RS0030 // Category 8: parses the verified snapshot files read from disk
            var tree = CSharpSyntaxTree.ParseText(code);
#pragma warning restore RS0030
            var root = tree.GetRoot();

            foreach (var node in root.DescendantNodes())
            {
                switch (node)
                {
                    case FieldDeclarationSyntax fieldDecl:
                        if (UsesDynamic(fieldDecl.Declaration.Type))
                        {
                            foreach (var variable in fieldDecl.Declaration.Variables)
                            {
                                var lineSpan = variable.GetLocation().GetLineSpan();
                                violations.Add($"{file}:{lineSpan.StartLinePosition.Line + 1}: field '{variable.Identifier.ValueText}' uses dynamic");
                            }
                        }
                        break;
                    case LocalDeclarationStatementSyntax localDecl:
                        if (UsesDynamic(localDecl.Declaration.Type))
                        {
                            foreach (var variable in localDecl.Declaration.Variables)
                            {
                                var lineSpan = variable.GetLocation().GetLineSpan();
                                violations.Add($"{file}:{lineSpan.StartLinePosition.Line + 1}: local '{variable.Identifier.ValueText}' uses dynamic");
                            }
                        }
                        break;
                    case ParameterSyntax parameter:
                        if (parameter.Type != null && UsesDynamic(parameter.Type))
                        {
                            var lineSpan = parameter.GetLocation().GetLineSpan();
                            violations.Add($"{file}:{lineSpan.StartLinePosition.Line + 1}: parameter '{parameter.Identifier.ValueText}' uses dynamic");
                        }
                        break;
                }
            }
        }

        Assert.Empty(violations);
    }

    // The prefilter keeps a file with dynamic in each position the scan reports, and skips comments, strings and longer names
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("class C { dynamic f; }", true)]
    [InlineData("class C { void M() { List<dynamic> l = null; } }", true)]
    [InlineData("class C { void M(dynamic p) { } }", true)]
    [InlineData("class C { void M(int @dynamic) { } }", true)]
    [InlineData("// dynamic shared memory\nclass C { string s = \"dynamic\"; /* dynamic */ }", false)]
    [InlineData("class C { int dynamicSize; }", false)]
    public void HasDynamicIdentifier_FindsOnlyIdentifierTokens(string code, bool expected) =>
        Assert.Equal(expected, HasDynamicIdentifier(code));

    private static bool HasDynamicIdentifier(string code) =>
        SyntaxFactory.ParseTokens(code)
            .Any(static t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == "dynamic");

    private static bool UsesDynamic(TypeSyntax typeSyntax)
    {
        return typeSyntax is IdentifierNameSyntax id && id.Identifier.ValueText == "dynamic"
            || typeSyntax.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(id => id.Identifier.ValueText == "dynamic");
    }
}
