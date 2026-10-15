using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public partial class DiagnosticParityTests : TranspilerTestBase
{
    private sealed class DiagnosticMetadata
    {
        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string DefaultSeverity { get; set; } = "";
        public bool IsObsolete { get; set; }
    }

    private static List<DiagnosticMetadata> LoadDiagnosticsMetadata(out string rootPath)
    {
        rootPath = GetRepositoryRoot();
        string jsonPath = Path.Combine(rootPath, "src", "Compiler", "Kassyi.GSharp.Compiler", "Diagnostics",
            "diagnostics.json");
        Assert.True(File.Exists(jsonPath), $"diagnostics.json not found at {jsonPath}");

        string json = File.ReadAllText(jsonPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var diagnostics = JsonSerializer.Deserialize<List<DiagnosticMetadata>>(json, options);

        Assert.NotNull(diagnostics);
        Assert.NotEmpty(diagnostics);
        return diagnostics;
    }

    [Fact]
    public void AllDiagnosticsMustHaveDocumentation()
    {
        var diagnostics = LoadDiagnosticsMetadata(out string rootPath);
        string docsDir = Path.Combine(rootPath, "docs", "spec", "en", "03_bcl_api", "diagnostics");
        Assert.True(Directory.Exists(docsDir), $"docs directory not found at {docsDir}");

        foreach (var diag in diagnostics)
        {
            if (diag.IsObsolete)
            {
                continue; // Obsolete items do not strictly require documentation
            }

            string docPath = Path.Combine(docsDir, $"{diag.Id}.md");
            Assert.True(File.Exists(docPath),
                $"Documentation for diagnostic {diag.Id} is missing. Expected file at: {docPath}");
        }
    }

    [Fact]
    public void AllDiagnosticsMustHaveValidIdentifiersIdsAndBidirectionalSymbolParity()
    {
        var diagnostics = LoadDiagnosticsMetadata(out _);

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<string, DiagnosticMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var diag in diagnostics)
        {
            // ID validation
            Assert.False(string.IsNullOrWhiteSpace(diag.Id), "Diagnostic ID must not be empty or whitespace.");
            Assert.Matches(DiagnosticIdRegex(), diag.Id);
            Assert.True(seenIds.Add(diag.Id), $"Duplicate diagnostic ID found: '{diag.Id}'");

            // Name validation
            Assert.False(string.IsNullOrWhiteSpace(diag.Name), $"Diagnostic Name must not be empty (ID: {diag.Id}).");
            Assert.True(Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(diag.Name),
                $"Diagnostic Name '{diag.Name}' is not a valid C# identifier (ID: {diag.Id}).");
            Assert.True(seenNames.Add(diag.Name), $"Duplicate diagnostic Name found: '{diag.Name}'");

            // Title validation
            Assert.False(string.IsNullOrWhiteSpace(diag.Title), $"Diagnostic Title must not be empty (ID: {diag.Id}).");
            byId[diag.Id] = diag;
        }

        // Bidirectional check between GSharpDiagnostics descriptor fields (via Roslyn symbols) and diagnostics.json
        var gsharpDiagnosticsSymbol = MegaCompilationCache.BaseCompilation
            .GetTypeByMetadataName("Kassyi.GSharp.Compiler.Diagnostics.GSharpDiagnostics");
        Assert.NotNull(gsharpDiagnosticsSymbol);

        var descriptorFieldNames = gsharpDiagnosticsSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.IsStatic && f.Type.Name == nameof(DiagnosticDescriptor))
            .Select(f => f.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(descriptorFieldNames);
        foreach (var jsonName in seenNames)
        {
            Assert.Contains(jsonName, descriptorFieldNames);
        }

        foreach (var fieldName in descriptorFieldNames)
        {
            Assert.Contains(fieldName, seenNames);
        }

        // Verify all registered validation rule descriptors match diagnostics.json metadata
        foreach (var descriptor in DiagnosticRuleRegistry.AllSupportedDiagnostics)
        {
            Assert.True(byId.TryGetValue(descriptor.Id, out var meta),
                $"Descriptor '{descriptor.Id}' in DiagnosticRuleRegistry is missing from diagnostics.json.");
            Assert.Equal(meta.Title, descriptor.Title.ToString());
            Assert.Equal(Enum.Parse<DiagnosticSeverity>(meta.DefaultSeverity), descriptor.DefaultSeverity);
        }
    }

    [Theory]
    [InlineData("Analyzers/GpuKernel/Foreach_InGpuKernel.cu.cs")]
    [InlineData("Analyzers/GpuKernel/ImplicitVar_WithoutSuffix_InGpuKernel.cu.cs")]
    [InlineData("Analyzers/MacroArgumentSadPath.cs")]
    [InlineData("Analyzers/UnrollHintSadPath.cs")]
    public async Task AnalyzerAndTranspiler_ReportMatchingDiagnosticIdsSeveritiesAndSpans(string relativeTestCasePath)
    {
        string rootPath = GetRepositoryRoot();
        string fullPath = Path.Combine(rootPath, "tests", "Kassyi.GSharp.Tests.Shared", "TestCases", relativeTestCasePath);
        string source = await File.ReadAllTextAsync(fullPath);

        var analyzerDiagnostics = (await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, new GpuKernelAnalyzer()))
            .Where(d => d.Id.StartsWith("GSH", StringComparison.Ordinal) && d.Id != GSharpDiagnostics.KernelClassMustBePartial.Id)
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ThenBy(d => d.Id)
            .ToList();

        var compilation = RoslynTestUtility.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Last();
        var model = compilation.GetSemanticModel(tree);
        var walkerDiagnostics = new List<Diagnostic>();

        foreach (var methodDecl in tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>())
        {
            var methodSymbol = model.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
            if (methodSymbol == null)
            {
                continue;
            }

            bool isGpuMethod = methodSymbol.GetAttributes().Any(a =>
                a.AttributeClass?.Name is "GpuKernelAttribute" or "GpuDeviceFunctionAttribute");
            if (!isGpuMethod)
            {
                continue;
            }

            var validationContext = new ValidationContext(model, methodSymbol, walkerDiagnostics.Add);
            using var walker = GpuKernelValidationWalker.Rent(validationContext);
            walker.Visit(methodDecl);
        }

        var transpilerValidationDiagnostics = walkerDiagnostics
            .Where(d => d.Id.StartsWith("GSH", StringComparison.Ordinal))
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ThenBy(d => d.Id)
            .ToList();

        Assert.NotEmpty(analyzerDiagnostics);
        Assert.Equal(analyzerDiagnostics.Count, transpilerValidationDiagnostics.Count);

        for (int i = 0; i < analyzerDiagnostics.Count; i++)
        {
            Assert.Equal(analyzerDiagnostics[i].Id, transpilerValidationDiagnostics[i].Id);
            Assert.Equal(analyzerDiagnostics[i].Severity, transpilerValidationDiagnostics[i].Severity);
            Assert.Equal(
                analyzerDiagnostics[i].Location.GetLineSpan().Span,
                transpilerValidationDiagnostics[i].Location.GetLineSpan().Span);
        }
    }

    [GeneratedRegex(@"^[A-Z]{3}\d{4}$")]
    private static partial Regex DiagnosticIdRegex();

    private static string GetRepositoryRoot([CallerFilePath] string callerPath = "")
    {
        var dirPath = Path.GetDirectoryName(callerPath) ?? throw new InvalidOperationException($"Invalid caller path: {callerPath}");
        var dir = new DirectoryInfo(dirPath);
        return dir.Parent?.Parent?.Parent?.FullName ?? throw new InvalidOperationException("Failed to resolve repository root directory.");
    }
}

