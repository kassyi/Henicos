#nullable enable
using System.Text;
using Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

namespace Kassyi.GSharp.Tests.Cpu.Transpiler;

/// <summary>
/// Enforces the #2314 compiler layering gate: <c>Ir/</c> (excluding <c>Ir/Linear/IrStateMachine*</c>),
/// <c>Semantics/</c>, <c>Dialects/</c>, and <c>Backends/</c> must not introduce new direct or indirect references
/// to types or extension methods declared in <c>Transpiler/</c> or <c>Lowering/</c>.
/// </summary>
[Collection(TestCollections.CorpusScan)]
public class CompilerLayerArchitectureTests(ITestOutputHelper output)
{
    private const string DirectExpectedFile = "CompilerLayerDirect.expected.yml";
    private const string IndirectExpectedFile = "CompilerLayerIndirect.expected.yml";

    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly Lazy<CompilerLayerGraph> s_layerGraph =
        new(CompilerLayerGraph.Build, LazyThreadSafetyMode.ExecutionAndPublication);

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void CompilerLayers_DirectReferences_MatchExpected()
    {
        var graph = s_layerGraph.Value;
        Assert.Empty(graph.CompilationErrors);

        AssertEdgesMatchExpected(
            name: "CompilerLayerDirect",
            expectedFile: DirectExpectedFile,
            headerComment:
                "# Direct references from Ir/ (excluding Ir/Linear/IrStateMachine*), Semantics/, Dialects/, " +
                "and Backends/\r\n" +
                "# to types or extension methods declared in Transpiler/ or Lowering/.",
            actualEdges: graph.DirectEdges);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void CompilerLayers_IndirectReferences_MatchExpected()
    {
        var graph = s_layerGraph.Value;
        Assert.Empty(graph.CompilationErrors);

        AssertEdgesMatchExpected(
            name: "CompilerLayerIndirect",
            expectedFile: IndirectExpectedFile,
            headerComment:
                "# Indirect references: types reachable from Tier 1 (excluding Transpiler/, Lowering/, " +
                "and Ir/Linear/IrStateMachine*)\r\n" +
                "# that reference types or extension methods declared in Transpiler/ or Lowering/.",
            actualEdges: graph.IndirectEdges);
    }

    private void AssertEdgesMatchExpected(
        string name,
        string expectedFile,
        string headerComment,
        ImmutableArray<string> actualEdges)
    {
        var expectedPath = RepositoryPaths.Combine(
            "tests",
            "Compiler",
            "Kassyi.GSharp.Tests.Cpu",
            "Transpiler",
            expectedFile);

        var expectedExists = File.Exists(expectedPath);
        var expectedEdges = ReadExpectedEdges(expectedPath);

        var added = actualEdges
            .Where(edge => !expectedEdges.Contains(edge))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var actualSet = actualEdges.ToHashSet(StringComparer.Ordinal);
        var removed = expectedEdges
            .Where(edge => !actualSet.Contains(edge))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (expectedExists && added.Length == 0 && removed.Length == 0)
        {
            return;
        }

        var receivedPath = IrExpectedFiles.ReceivedPath(name, ".yml");
        var yaml = FormatYamlDocument(headerComment, actualEdges);
        File.WriteAllText(receivedPath, yaml, s_utf8NoBom);
        output.WriteLine("Received: " + receivedPath);

        var message = new StringBuilder();
        if (!expectedExists)
        {
            message.Append("Missing expected file: ").Append(expectedPath).AppendLine();
        }

        if (added.Length > 0)
        {
            message.Append("Added edges (").Append(added.Length).AppendLine("):");
            foreach (var edge in added)
            {
                message.Append("  + ").AppendLine(edge);
            }
        }

        if (removed.Length > 0)
        {
            message.Append("Removed edges (").Append(removed.Length).AppendLine("):");
            foreach (var edge in removed)
            {
                message.Append("  - ").AppendLine(edge);
            }
        }

        message.Append("Received: ").Append(receivedPath);
        Assert.Fail(message.ToString());
    }

    private static string FormatYamlDocument(string headerComment, ImmutableArray<string> edges)
    {
        var builder = new StringBuilder();
        builder.Append(headerComment).Append("\r\n");
        foreach (var edge in edges)
        {
            builder.Append("- \"");
            foreach (var ch in edge)
            {
                if (ch is '\\' or '"')
                {
                    builder.Append('\\');
                }

                builder.Append(ch);
            }

            builder.Append("\"\r\n");
        }

        return builder.ToString();
    }

    private static HashSet<string> ReadExpectedEdges(string expectedPath)
    {
        var edges = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(expectedPath))
        {
            return edges;
        }

        foreach (var rawLine in File.ReadAllLines(expectedPath))
        {
            var span = rawLine.AsSpan().Trim();
            if (span.IsEmpty || span[0] == '#')
            {
                continue;
            }

            if (!span.StartsWith("- ", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unexpected YAML line in {expectedPath}: {rawLine}");
            }

            var value = span[2..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            edges.Add(UnescapeYamlQuoted(value));
        }

        return edges;
    }

    private static string UnescapeYamlQuoted(ReadOnlySpan<char> value)
    {
        if (!value.Contains('\\'))
        {
            return value.ToString();
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }
}
