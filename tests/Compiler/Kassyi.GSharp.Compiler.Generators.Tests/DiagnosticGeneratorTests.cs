using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Generators.Tests;

/// <summary>
/// The <c>diagnostics</c> mode: the strings of <c>diagnostics.json</c> reach the generated descriptors exactly, and a
/// file that is not a list of diagnostics fails the run instead of leaving the earlier output in place.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Unit)]
public class DiagnosticGeneratorTests
{
    public static TheoryData<string> AwkwardStrings =>
    [
        "Cannot open C:\\temp\\x",
        "first line\nsecond line",
        "carriage\r\nreturn",
        "a \"quoted\" word",
        "tab\tand nul\0",
        "trailing backslash\\",
        "line separator\u2028and next line\u0085",
        "{0} and {{literal}}",
        "non-ASCII: \u00e9\u4e2d",
    ];

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(AwkwardStrings))]
    public void Strings_ReachTheDescriptorExactly(string text)
    {
        using var scratch = new ScratchDirectory();
        var entry = new Dictionary<string, object>
        {
            ["name"] = "Probe",
            ["id"] = "GSH9999",
            ["title"] = text,
            ["messageFormat"] = text,
            ["category"] = text,
            ["description"] = text,
            ["defaultSeverity"] = "Error",
            ["isEnabledByDefault"] = true,
        };
        var input = scratch.Write("diagnostics.json", JsonSerializer.Serialize(new[] { entry }));

        var result = GeneratorRun.Run("diagnostics", input, scratch.Combine("out"));

        Assert.Equal(0, result.ExitCode);
        var root = GeneratorRun.ParseWithoutErrors(File.ReadAllText(scratch.Combine("out", "GSharpDiagnostics.g.cs")));
        var literals = GeneratorRun.StringLiteralValues(root);
        Assert.Equal(4, literals.Count(l => l == text));
        Assert.Contains("GSH9999", literals);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void RepositoryDiagnostics_GenerateParsableSource()
    {
        using var scratch = new ScratchDirectory();
        var input = RepositoryPaths.Combine("src", "Compiler", "Kassyi.GSharp.Compiler", "Diagnostics", "diagnostics.json");

        var result = GeneratorRun.Run("diagnostics", input, scratch.Combine("out"));

        Assert.Equal(0, result.ExitCode);
        var source = File.ReadAllText(scratch.Combine("out", "GSharpDiagnostics.g.cs"));
        GeneratorRun.ParseWithoutErrors(source);
        Assert.DoesNotContain("#error", source, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("null", 0, "The file must hold a JSON array of diagnostics")]
    [InlineData("[\n  { \"name\": \"Probe\",\n    \"id\": }\n]", 3, "")]
    [InlineData("{ \"name\": \"Probe\" }", 1, "")]
    public void FileThatIsNotAList_FailsWithFileAndLine(string json, int line, string message)
    {
        using var scratch = new ScratchDirectory();
        var input = scratch.Write("diagnostics.json", json);
        var stale = scratch.Write("out/GSharpDiagnostics.g.cs", "// stale");

        var result = GeneratorRun.Run("diagnostics", input, scratch.Combine("out"));

        Assert.Equal(1, result.ExitCode);
        var location = line > 0 ? $"{input}({line})" : input;
        Assert.Contains($"{location}: error : {message}", result.StandardError, StringComparison.Ordinal);
        Assert.Equal("// stale", File.ReadAllText(stale));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void InvalidSeverity_IsAnErrorDirective()
    {
        var definition = new DiagnosticDefinition
        {
            Name = "Probe",
            Id = "GSH9999",
            Title = "t",
            MessageFormat = "m",
            Category = "c",
            DefaultSeverity = "Fatal",
        };

        var source = DiagnosticGenerator.GenerateSource([definition]);

        Assert.Contains("#error Invalid defaultSeverity 'Fatal' in diagnostics.json (ID: GSH9999).", source, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ObsoleteDiagnostic_IsDeclaredButLeftOutOfAll()
    {
        DiagnosticDefinition[] definitions =
        [
            new()
            {
                Name = "Reported",
                Id = "GSH9998",
                Title = "t",
                MessageFormat = "m",
                Category = "c",
                DefaultSeverity = "Error",
            },
            new()
            {
                Name = "Retired",
                Id = "GSH9999",
                Title = "t",
                MessageFormat = "m",
                Category = "c",
                DefaultSeverity = "Warning",
                IsObsolete = true,
            },
        ];

        var root = GeneratorRun.ParseWithoutErrors(DiagnosticGenerator.GenerateSource(definitions));

        var fields = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().ToList();
        var retired = fields.Single(static f => f.Identifier.Text == "Retired");
        var retiredAttributes = retired.Ancestors().OfType<FieldDeclarationSyntax>().First().AttributeLists.ToString();
        Assert.Contains("Obsolete", retiredAttributes, StringComparison.Ordinal);
        var all = fields.Single(static f => f.Identifier.Text == "All");
        var listed = all.DescendantNodes().OfType<IdentifierNameSyntax>().Select(static n => n.Identifier.Text).ToList();
        Assert.Contains("Reported", listed);
        Assert.DoesNotContain("Retired", listed);
    }
}
