#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Kassyi.GSharp.Compiler.NativeApi;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// The registry of <see cref="KnownDivergence"/> entries that <see cref="DifferentialRunner.Run"/> applies to every case,
/// read once from <c>KnownDivergences.yaml</c> (an embedded resource next to this file).
/// A matrix test that finds a new divergence files an issue (M1 or M2) and adds an entry there; the PR that fixes it removes the entry, since the entry then fails.
/// </summary>
public static class KnownDivergences
{
    /// <summary>The manifest resource name of <c>KnownDivergences.yaml</c>.</summary>
    internal const string ResourceName = "Kassyi.GSharp.Tests.Shared.Differential.KnownDivergences.yaml";

    private static readonly KnownDivergenceList s_list = Load();

    /// <summary>Gets every registered entry, one per case, column and issue.</summary>
    public static IReadOnlyList<KnownDivergence> All => s_list.Entries;

    /// <summary>Gets the issues the entries name, with the titles the file records.</summary>
    internal static IReadOnlyList<KnownDivergenceIssue> Issues => s_list.Issues;

    private static KnownDivergenceList Load()
    {
        using var stream = typeof(KnownDivergences).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses the text of <c>KnownDivergences.yaml</c>.</summary>
    /// <param name="yaml">The file text.</param>
    /// <exception cref="InvalidDataException">The text does not follow the format the file header describes.</exception>
    internal static KnownDivergenceList Parse(string yaml)
    {
        YamlNode root;
        try
        {
            root = YamlSubsetParser.Parse(yaml);
        }
        catch (NativeApiReadException ex)
        {
            var error = ex.ToError();
            throw new InvalidDataException($"KnownDivergences.yaml line {error.Line}: {error.Message}", ex);
        }

        var issues = new List<KnownDivergenceIssue>();
        var entries = new List<KnownDivergence>();
        if (root is YamlScalar { IsNull: true })
        {
            return new KnownDivergenceList(issues, entries);
        }

        foreach (var issueNode in ExpectSequence(root, "the document"))
        {
            var issueMapping = ExpectMapping(issueNode, "an issue block");
            int issue = RequireInt32(issueMapping, "issue");
            issues.Add(new KnownDivergenceIssue(issue, RequireScalar(issueMapping, "title"), issueMapping.Line));
            var entryNodes = issueMapping.TryGetValue("entries", out var entriesNode)
                ? ExpectSequence(entriesNode, "entries")
                : throw Error(issueMapping, "The issue block has no 'entries'.");
            foreach (var entryNode in entryNodes)
            {
                var entry = ExpectMapping(entryNode, "an entry");
                string caseName = RequireScalar(entry, "case");
                var paths = ParsePaths(entry);
                var hosts = ParseHosts(entry);
                foreach (string column in RequireScalarList(entry, "columns"))
                {
                    entries.Add(new KnownDivergence(caseName, column, paths, issue, hosts));
                }
            }
        }

        return new KnownDivergenceList(issues, entries);
    }

    private static DiffPath ParsePaths(YamlMapping entry)
    {
        var paths = DiffPath.None;
        foreach (string name in RequireScalarList(entry, "paths"))
        {
            paths |= name switch
            {
                "Managed" => DiffPath.Managed,
                "Native" => DiffPath.Native,
                "Cuda" => DiffPath.Cuda,
                _ => throw Error(entry, $"Unknown path '{name}'; expected Managed, Native or Cuda."),
            };
        }

        return paths;
    }

    private static DiffHost ParseHosts(YamlMapping entry)
    {
        if (!entry.TryGetValue("hosts", out _))
        {
            return DiffHost.Any;
        }

        DiffHost hosts = 0;
        foreach (string name in RequireScalarList(entry, "hosts"))
        {
            hosts |= name switch
            {
                "Windows" => DiffHost.Windows,
                "Unix" => DiffHost.Unix,
                _ => throw Error(entry, $"Unknown host '{name}'; expected Windows or Unix."),
            };
        }

        return hosts;
    }

    private static IEnumerable<YamlNode> ExpectSequence(YamlNode node, string what) =>
        node is YamlSequence sequence
            ? sequence.Items
            : throw Error(node, $"Expected a sequence for {what}.");

    private static YamlMapping ExpectMapping(YamlNode node, string what) =>
        node as YamlMapping ?? throw Error(node, $"Expected a mapping for {what}.");

    private static string RequireScalar(YamlMapping mapping, string key) =>
        mapping.TryGetScalar(key, out string? value) && value.Length > 0
            ? value
            : throw Error(mapping, $"Missing or empty '{key}'.");

    private static int RequireInt32(YamlMapping mapping, string key) =>
        mapping.TryGetInt32(key, out int value) && value > 0
            ? value
            : throw Error(mapping, $"'{key}' must be a positive issue number.");

    private static List<string> RequireScalarList(YamlMapping mapping, string key)
    {
        if (!mapping.TryGetValue(key, out var node) || node is not YamlSequence { Items.Count: > 0 } sequence)
        {
            throw Error(mapping, $"'{key}' must be a non-empty list.");
        }

        var values = new List<string>(sequence.Items.Count);
        foreach (var item in sequence.Items)
        {
            values.Add(item is YamlScalar { IsNull: false, Value.Length: > 0 } scalar
                ? scalar.Value
                : throw Error(item, $"Every item of '{key}' must be a non-empty scalar."));
        }

        return values;
    }

    private static InvalidDataException Error(YamlNode node, string message) =>
        new($"KnownDivergences.yaml line {node.Line}: {message}");
}
