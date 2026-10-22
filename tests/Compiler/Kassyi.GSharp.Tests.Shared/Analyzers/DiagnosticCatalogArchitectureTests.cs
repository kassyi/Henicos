using System.Text.RegularExpressions;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.NativeApi;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>Guards that every diagnostic is defined in <c>diagnostics.json</c> and nowhere else.</summary>
[Category(TestCategory.Compiler)]
public partial class DiagnosticCatalogArchitectureTests
{
    // The build-time generator executable cannot link the generated GSharpDiagnostics, so this file mirrors
    // the NativeApi YAML IDs (see NativeApiReadError.DiagnosticId); NativeApiReadErrorIds_MatchCatalog pins them.
    private const string MirroredIdFile = "NativeApiDefinition.cs";

    // The catalog generator itself cites an example ID in its schema-error message.
    private const string CatalogGeneratorFile = "DiagnosticGenerator.cs";

    [GeneratedRegex(@"\b(GSH|SLQ)\d{4}\b")]
    private static partial Regex DiagnosticIdPattern();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Sources_ContainNoDiagnosticIdLiterals()
    {
        var violations = new List<string>();

        foreach (var path in EnumerateSourceFiles())
        {
            var fileName = Path.GetFileName(path);
            if (fileName is MirroredIdFile or CatalogGeneratorFile)
            {
                continue;
            }

            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
            foreach (var token in root.DescendantTokens())
            {
                if (token.Kind() is not (SyntaxKind.StringLiteralToken or SyntaxKind.InterpolatedStringTextToken) ||
                    !DiagnosticIdPattern().IsMatch(token.ValueText))
                {
                    continue;
                }

                var line = token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                violations.Add($"{fileName}:{line}: {token.ValueText}");
            }
        }

        Assert.True(violations.Count == 0,
            "Diagnostic IDs must come from diagnostics.json (GSharpDiagnostics), not string literals:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Sources_ContainNoHandWrittenDiagnosticDescriptors()
    {
        var violations = new List<string>();

        foreach (var path in EnumerateSourceFiles())
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
            foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
            {
                var isDescriptor = creation switch
                {
                    ObjectCreationExpressionSyntax explicitNew => explicitNew.Type.ToString().EndsWith("DiagnosticDescriptor", StringComparison.Ordinal),
                    // Target-typed `new(...)` bound to a DiagnosticDescriptor field or local.
                    ImplicitObjectCreationExpressionSyntax implicitNew => IsDescriptorDeclaration(implicitNew),
                    _ => false,
                };

                if (isDescriptor)
                {
                    var line = creation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    violations.Add($"{Path.GetFileName(path)}:{line}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "DiagnosticDescriptor instances must be generated from diagnostics.json:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void NativeApiReadErrorIds_MatchCatalog()
    {
        Assert.Equal(
            GSharpDiagnostics.NativeApiYamlSyntaxError.Id,
            new NativeApiReadError(NativeApiReadErrorKind.Syntax, 1, "x").DiagnosticId);
        Assert.Equal(
            GSharpDiagnostics.NativeApiSchemaViolation.Id,
            new NativeApiReadError(NativeApiReadErrorKind.Schema, 1, "x").DiagnosticId);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void StackLinqChainAnalyzer_DiagnosticId_ComesFromCatalog() =>
        Assert.Equal(GSharpDiagnostics.StackLinqChainTooDeep.Id, StackLinqChainAnalyzer.DiagnosticId);

    private static bool IsDescriptorDeclaration(ImplicitObjectCreationExpressionSyntax creation) =>
        creation.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } } =>
                declaration.Type.ToString().EndsWith("DiagnosticDescriptor", StringComparison.Ordinal),
            ArrowExpressionClauseSyntax { Parent: PropertyDeclarationSyntax property } =>
                property.Type.ToString().EndsWith("DiagnosticDescriptor", StringComparison.Ordinal),
            _ => false,
        };

    private static IEnumerable<string> EnumerateSourceFiles()
    {
        var separator = Path.DirectorySeparatorChar;
        foreach (var path in Directory.EnumerateFiles(RepositoryPaths.Combine("src"), "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{separator}bin{separator}") || path.Contains($"{separator}obj{separator}"))
            {
                continue;
            }

            yield return path;
        }
    }
}
