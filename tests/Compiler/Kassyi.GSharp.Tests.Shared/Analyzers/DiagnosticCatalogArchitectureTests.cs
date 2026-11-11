using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.NativeApi;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>Guards that every diagnostic is defined in <c>diagnostics.json</c> and nowhere else.</summary>
[Category(TestCategory.Compiler)]
public class DiagnosticCatalogArchitectureTests
{
    // The build-time generator executable cannot link the generated GSharpDiagnostics, so this file mirrors
    // the NativeApi YAML IDs (see NativeApiReadError.DiagnosticId); NativeApiReadErrorIds_MatchCatalog pins them.
    private const string MirroredIdFile = "NativeApiDefinition.cs";

    // The catalog generator itself cites an example ID in its schema-error message.
    private const string CatalogGeneratorFile = "DiagnosticGenerator.cs";

    // Every ID the catalog defines (diagnostics.json, generated into GSharpDiagnostics)
    private static readonly string[] s_catalogIds = [.. GSharpDiagnostics.All.Select(static d => d.Id)];

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Sources_DiagnosticIdReferences_ContainNoDiagnosticIdLiterals()
    {
        var violations = new List<string>();

        foreach (var path in EnumerateSourceFiles())
        {
            var fileName = Path.GetFileName(path);
            if (fileName is MirroredIdFile or CatalogGeneratorFile)
            {
                continue;
            }

            var root = RoslynTestUtility.CreateCompilation(File.ReadAllText(path)).SyntaxTrees.First().GetRoot();
            foreach (var token in root.DescendantTokens())
            {
                if (token.Kind() is not (SyntaxKind.StringLiteralToken or SyntaxKind.InterpolatedStringTextToken) ||
                    !s_catalogIds.Any(id => token.ValueText.Contains(id, StringComparison.Ordinal)))
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
    public void Sources_DiagnosticDescriptors_ContainNoHandWrittenDiagnosticDescriptors()
    {
        var violations = new List<string>();

        foreach (var path in EnumerateSourceFiles())
        {
            var root = RoslynTestUtility.CreateCompilation(File.ReadAllText(path)).SyntaxTrees.First().GetRoot();
            foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
            {
                var isDescriptor = creation switch
                {
                    ObjectCreationExpressionSyntax explicitNew => IsDiagnosticDescriptorType(explicitNew.Type),
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

    private static bool IsDiagnosticDescriptorType(TypeSyntax type) =>
        type switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText.EndsWith("DiagnosticDescriptor", StringComparison.Ordinal),
            QualifiedNameSyntax q => q.Right.Identifier.ValueText.EndsWith("DiagnosticDescriptor", StringComparison.Ordinal),
            _ => false,
        };

    private static bool IsDescriptorDeclaration(ImplicitObjectCreationExpressionSyntax creation) =>
        creation.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } } =>
                IsDiagnosticDescriptorType(declaration.Type),
            ArrowExpressionClauseSyntax { Parent: PropertyDeclarationSyntax property } =>
                IsDiagnosticDescriptorType(property.Type),
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
