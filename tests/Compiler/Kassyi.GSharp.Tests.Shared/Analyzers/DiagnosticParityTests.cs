using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
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

    private static string ReadDiagnosticsJson(out string rootPath)
    {
        rootPath = RepositoryPaths.Root;
        string jsonPath = Path.Combine(RepositoryPaths.CompilerProject, "Diagnostics", "diagnostics.json");
        Assert.True(File.Exists(jsonPath), $"diagnostics.json not found at {jsonPath}");

        return File.ReadAllText(jsonPath);
    }

    private static List<DiagnosticMetadata> LoadDiagnosticsMetadata(out string rootPath)
    {
        string json = ReadDiagnosticsJson(out rootPath);
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
        string fullPath = RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "TestCases", relativeTestCasePath);
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

    // A merge-conflict resolution once folded two entries into one object (#420). System.Text.Json keeps the last
    // value of a repeated key, so the first diagnostic vanished without any of the checks above noticing.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void DiagnosticsJson_HasNoDuplicateKeysInAnyEntry()
    {
        var violations = FindDuplicateKeys(ReadDiagnosticsJson(out _));

        Assert.True(violations.Count == 0,
            "diagnostics.json has entries with repeated keys:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    // Gaps are allowed: IDs are reserved for planned issues before they are used.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void DiagnosticsJson_ListsIdsInAscendingOrderPerPrefix()
    {
        var violations = FindOutOfOrderIds(ReadDiagnosticsJson(out _));

        Assert.True(violations.Count == 0,
            "diagnostics.json lists IDs out of order:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void FindDuplicateKeys_ReportsEntryMergedFromTwoDiagnostics()
    {
        // The shape the #420 conflict resolution produced.
        const string Json = """
            [
              { "name": "A", "id": "GSH0001", "title": "First" },
              { "name": "KernelParameterSpaceExceeded", "id": "GSH0066", "title": "B",
                "name": "UserDefinedOperatorNotSupported", "id": "GSH0062", "title": "C" }
            ]
            """;

        var violations = FindDuplicateKeys(Json);

        Assert.Equal(["GSH0066+GSH0062: name", "GSH0066+GSH0062: id", "GSH0066+GSH0062: title"], violations);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindOutOfOrderIds_AllowsGapsAndReportsDescendingPair()
    {
        const string Json = """
            [
              { "id": "GSH0001" },
              { "id": "GSH0003" },
              { "id": "GSH0066" },
              { "id": "GSH0062" },
              { "id": "SLQ0001" }
            ]
            """;

        var violations = FindOutOfOrderIds(Json);

        Assert.Equal(["GSH0066 is followed by GSH0062"], violations);
    }

    private static List<string> FindDuplicateKeys(string json)
    {
        using var document = JsonDocument.Parse(json);
        var violations = new List<string>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ids = new List<string>();
            var repeated = new List<string>();
            foreach (var property in entry.EnumerateObject())
            {
                if (property.NameEquals("id"))
                {
                    ids.Add(property.Value.GetString() ?? "");
                }

                if (!seen.Add(property.Name))
                {
                    repeated.Add(property.Name);
                }
            }

            foreach (var key in repeated)
            {
                violations.Add($"{string.Join("+", ids)}: {key}");
            }
        }

        return violations;
    }

    private static List<string> FindOutOfOrderIds(string json)
    {
        using var document = JsonDocument.Parse(json);
        var previousByPrefix = new Dictionary<string, (int Number, string Id)>(StringComparer.Ordinal);
        var violations = new List<string>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var id = entry.GetProperty("id").GetString() ?? "";
            var prefix = id[..^4];
            var number = int.Parse(id[^4..], NumberStyles.None, CultureInfo.InvariantCulture);
            if (previousByPrefix.TryGetValue(prefix, out var previous) && number <= previous.Number)
            {
                violations.Add($"{previous.Id} is followed by {id}");
            }

            previousByPrefix[prefix] = (number, id);
        }

        return violations;
    }

    [GeneratedRegex(@"^[A-Z]{3}\d{4}$")]
    private static partial Regex DiagnosticIdRegex();
}

