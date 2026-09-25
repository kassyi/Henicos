using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public partial class DiagnosticParityTests
{
    private class DiagnosticMetadata
    {
        public string Id { get; set; } = "";
        public bool IsObsolete { get; set; }
    }

    [Fact]
    public void AllDiagnosticsMustHaveDocumentation()
    {
        string rootPath = GetRepositoryRoot();
        string jsonPath = Path.Combine(rootPath, "src", "Compiler", "Kassyi.GSharp.Compiler", "Diagnostics",
            "diagnostics.json");
        string docsDir = Path.Combine(rootPath, "docs", "spec", "en", "03_bcl_api", "diagnostics");

        Assert.True(File.Exists(jsonPath), $"diagnostics.json not found at {jsonPath}");
        Assert.True(Directory.Exists(docsDir), $"docs directory not found at {docsDir}");

        string json = File.ReadAllText(jsonPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var diagnostics = JsonSerializer.Deserialize<List<DiagnosticMetadata>>(json, options);

        Assert.NotNull(diagnostics);
        Assert.NotEmpty(diagnostics);

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

    private class FullDiagnosticMetadata
    {
        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
    }

    [Fact]
    public void AllDiagnosticsMustHaveValidIdentifiersAndIds()
    {
        string rootPath = GetRepositoryRoot();
        string jsonPath = Path.Combine(rootPath, "src", "Compiler", "Kassyi.GSharp.Compiler", "Diagnostics",
            "diagnostics.json");

        string json = File.ReadAllText(jsonPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var diagnostics = JsonSerializer.Deserialize<List<FullDiagnosticMetadata>>(json, options);

        Assert.NotNull(diagnostics);
        Assert.NotEmpty(diagnostics);

        var seenIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

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
        }
    }

    [GeneratedRegex(@"^[A-Z]{3}\d{4}$")]
    private static partial Regex DiagnosticIdRegex();

    private static string GetRepositoryRoot([CallerFilePath] string callerPath = "")
    {
        // callerPath is ...\tests\Kassyi.GSharp.Tests.Shared\Analyzers\DiagnosticParityTests.cs
        // Go up 4 levels to reach the repository root
        var dir = new DirectoryInfo(Path.GetDirectoryName(callerPath)!);
        return dir.Parent!.Parent!.Parent!.FullName;
    }
}
