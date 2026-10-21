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
public class NoDynamicInGeneratedCodeTests
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void NoDynamicInVerifiedSnapshots()
    {
        var testsDirectory = RepositoryPaths.Combine("tests");
        var verifiedFiles = Directory.GetFiles(testsDirectory, "*.verified.cs", SearchOption.AllDirectories);

        var violations = new List<string>();

        foreach (var file in verifiedFiles)
        {
            var code = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(code);
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

    private static bool UsesDynamic(TypeSyntax typeSyntax)
    {
        return typeSyntax is IdentifierNameSyntax id && id.Identifier.ValueText == "dynamic"
            || typeSyntax.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(id => id.Identifier.ValueText == "dynamic");
    }
}
