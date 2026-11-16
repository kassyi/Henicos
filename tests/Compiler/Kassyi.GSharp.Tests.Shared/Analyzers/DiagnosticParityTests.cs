using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;
using Kassyi.GSharp.Compiler.NativeApi;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public partial class DiagnosticParityTests : TranspilerTestBase
{
    private static List<DiagnosticCatalogEntry> LoadDiagnosticsMetadata(out string rootPath)
    {
        rootPath = RepositoryPaths.Root;
        var diagnostics = DiagnosticCatalogFile.Read();
        Assert.NotEmpty(diagnostics);
        return diagnostics;
    }

    [Fact]
    public void Diagnostics_DocumentationFiles_AllHaveCorrespondingDocFiles()
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
    public void Diagnostics_IdentifiersAndSymbols_MatchBidirectionalParity()
    {
        var diagnostics = LoadDiagnosticsMetadata(out _);

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<string, DiagnosticCatalogEntry>(StringComparer.OrdinalIgnoreCase);

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

        // Bidirectional check between GSharpDiagnostics descriptor fields (via Roslyn symbols) and diagnostics.yml
        var gsharpDiagnosticsSymbol = RoslynTestUtility.BaseCompilation
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

        // Verify all registered validation rule descriptors match diagnostics.yml metadata
        foreach (var descriptor in DiagnosticRuleRegistry.AllSupportedDiagnostics)
        {
            Assert.True(byId.TryGetValue(descriptor.Id, out var meta),
                $"Descriptor '{descriptor.Id}' in DiagnosticRuleRegistry is missing from diagnostics.yml.");
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

        var analyzerDiagnostics = (await GeneratorTestRunner.GetAnalyzerDiagnosticsAsync(source, new GpuKernelAnalyzer()))
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

            // GetAnalyzerDiagnosticsAsync gives the analyzer the CUDA dialect
            var validationContext = new ValidationContext(model, methodSymbol, walkerDiagnostics.Add, new GSharpCudaDialect());
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

    // A merge-conflict resolution once folded two entries into one (#420); the JSON reader kept the last value of a
    // repeated key, so the first diagnostic vanished without any of the checks above noticing. The YAML parser of the
    // compiler rejects a repeated key, and the build reads the catalog with it.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void DiagnosticCatalog_EntryMergedFromTwoDiagnostics_IsRejected()
    {
        const string Yaml = """
            - name: A
              id: GSH0001
              title: First
            - name: KernelParameterSpaceExceeded
              id: GSH0064
              title: B
              name: UserDefinedOperatorNotSupported
            """;

        var error = Assert.Throws<NativeApiReadException>(() => DiagnosticCatalogFile.Parse(Yaml));

        Assert.Contains("Duplicate key 'name'", error.Message, StringComparison.Ordinal);
        Assert.Equal(7, error.ToError().Line);
    }

    // Gaps are allowed: IDs are reserved for planned issues before they are used.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void DiagnosticCatalog_ListsIdsInAscendingOrderPerPrefix()
    {
        var violations = FindOutOfOrderIds(DiagnosticCatalogFile.Read().Select(static d => d.Id));

        Assert.True(violations.Count == 0,
            "diagnostics.yml lists IDs out of order:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void FindOutOfOrderIds_AllowsGapsAndReportsDescendingPair()
    {
        var violations = FindOutOfOrderIds(["GSH0001", "GSH0003", "GSH0064", "GSH0060", "SLQ0001"]);

        Assert.Equal(["GSH0064 is followed by GSH0060"], violations);
    }

    private static List<string> FindOutOfOrderIds(IEnumerable<string> ids)
    {
        var previousByPrefix = new Dictionary<string, (int Number, string Id)>(StringComparer.Ordinal);
        var violations = new List<string>();
        foreach (var id in ids)
        {
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

