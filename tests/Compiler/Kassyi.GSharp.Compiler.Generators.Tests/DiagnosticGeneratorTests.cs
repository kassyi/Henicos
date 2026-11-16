using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Generators.Tests;

/// <summary>
/// The <c>diagnostics</c> mode: the strings of <c>diagnostics.yml</c> reach the generated descriptors exactly, and a
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
        "a colon: and a # mark, [brackets] and {braces}",
    ];

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(AwkwardStrings))]
    public void Strings_ReachTheDescriptorExactly(string text)
    {
        using var scratch = new ScratchDirectory();
        var quoted = Quote(text);
        var input = scratch.Write(
            "diagnostics.yml",
            "- name: Probe\n" +
            "  id: GSH9999\n" +
            $"  title: {quoted}\n" +
            $"  messageFormat: {quoted}\n" +
            $"  category: {quoted}\n" +
            $"  description: {quoted}\n" +
            "  defaultSeverity: Error\n" +
            "  isEnabledByDefault: true\n");

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
        var input = RepositoryPaths.Combine("src", "Compiler", "Kassyi.GSharp.Compiler", "Diagnostics", "diagnostics.yml");

        var result = GeneratorRun.Run("diagnostics", input, scratch.Combine("out"));

        Assert.Equal(0, result.ExitCode);
        var source = File.ReadAllText(scratch.Combine("out", "GSharpDiagnostics.g.cs"));
        GeneratorRun.ParseWithoutErrors(source);
        Assert.DoesNotContain("#error", source, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("name: Probe\n", 1, "The file must hold a YAML list of diagnostics.")]
    [InlineData("- Probe\n", 1, "Each diagnostic must be a mapping.")]
    [InlineData("- name: Probe\n  id: GSH9999\n  name: Again\n", 3, "Duplicate key 'name'.")]
    [InlineData("- name: Probe\n  severity: Error\n", 2, "Unknown key 'severity'.")]
    [InlineData("- name: Probe\n  isObsolete: yes\n", 2, "The value of 'isObsolete' must be true or false, not 'yes'.")]
    public void FileThatIsNotAList_FailsWithFileAndLine(string yaml, int line, string message)
    {
        using var scratch = new ScratchDirectory();
        var input = scratch.Write("diagnostics.yml", yaml);
        var stale = scratch.Write("out/GSharpDiagnostics.g.cs", "// stale");

        var result = GeneratorRun.Run("diagnostics", input, scratch.Combine("out"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"{input}({line}): error : {message}", result.StandardError, StringComparison.Ordinal);
        Assert.Equal("// stale", File.ReadAllText(stale));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void InvalidSeverity_IsAnErrorDirective()
    {
        var source = DiagnosticGenerator.GenerateSource([Definition("Probe", "GSH9999", "Fatal")]);

        Assert.Contains(
            "#error Invalid defaultSeverity 'Fatal' in diagnostics.yml (ID: GSH9999).",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ObsoleteDiagnostic_IsDeclaredButLeftOutOfReported()
    {
        DiagnosticDefinition[] definitions =
        [
            Definition("Current", "GSH9998", "Error"),
            Definition("Retired", "GSH9999", "Warning", isObsolete: true),
        ];

        var root = GeneratorRun.ParseWithoutErrors(DiagnosticGenerator.GenerateSource(definitions));

        var fields = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().ToList();
        var retired = fields.Single(static f => f.Identifier.Text == "Retired");
        var retiredAttributes = retired.Ancestors().OfType<FieldDeclarationSyntax>().First().AttributeLists.ToString();
        Assert.Contains("Obsolete", retiredAttributes, StringComparison.Ordinal);
        var reported = fields.Single(static f => f.Identifier.Text == "Reported");
        var listed = reported.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Select(static n => n.Identifier.Text)
            .ToList();
        Assert.Contains("Current", listed);
        Assert.DoesNotContain("Retired", listed);
    }

    // A YAML double-quoted scalar with the escapes the compiler's YAML parser reads
    private static string Quote(string text)
    {
        var quoted = new StringBuilder("\"");
        foreach (var c in text)
        {
            quoted.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\0' => "\\0",
                _ => c.ToString(),
            });
        }

        return quoted.Append('"').ToString();
    }

    private static DiagnosticDefinition Definition(string name, string id, string severity, bool isObsolete = false) =>
        new()
        {
            Name = name,
            Id = id,
            Title = "t",
            MessageFormat = "m",
            Category = "c",
            DefaultSeverity = severity,
            IsObsolete = isObsolete,
        };
}
