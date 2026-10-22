using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Kassyi.GSharp.Compiler.Generators;

/// <summary>
/// Generates the nested constant classes of <c>GSharpInternalNames</c> from <c>Resources/InternalNames.yml</c>, and checks
/// the vocabulary while doing so: values are unique, start with <c>__gsharp_</c>, end in <c>_</c> exactly when the name ends
/// in <c>Prefix</c>, and names marked with a Prelude file or a Managed (Core) file are declared in that file.
/// </summary>
public static class InternalNamesGenerator
{
    // DO NOT add any more magic strings here. Add them to InternalNames.yml and update this class.
    private const string RequiredPrefix = "__gsharp_";
    private const string PrefixSuffix = "Prefix";

    private sealed record InternalName(
        string Group,
        string Name,
        string Value,
        string? PreludeFile,
        string? ManagedFile);

    private sealed record NameGroup(string Name, string Summary, List<InternalName> Names);

    /// <summary>
    /// Reads <paramref name="yamlPath"/>, checks it against the Prelude files in <paramref name="preludeDir"/> and the Core
    /// sources in <paramref name="coreDir"/>, and writes <c>GSharpInternalNames.g.cs</c> to <paramref name="outputDir"/>.
    /// </summary>
    public static void Run(string yamlPath, string preludeDir, string coreDir, string outputDir)
    {
        var groups = Read(yamlPath);
        Check(groups, preludeDir, coreDir);

        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "GSharpInternalNames.g.cs"), Generate(groups), Encoding.UTF8);
    }

    private static List<NameGroup> Read(string yamlPath)
    {
        var stream = new YamlStream();
        using (var reader = new StreamReader(yamlPath, Encoding.UTF8))
        {
            stream.Load(reader);
        }

        var groups = new List<NameGroup>();
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        foreach (var (groupKey, groupNode) in root.Children)
        {
            var groupName = ((YamlScalarNode)groupKey).Value ?? string.Empty;
            var group = (YamlMappingNode)groupNode;
            var summary = Scalar(group, "Summary") ?? throw Fail($"Group '{groupName}' has no Summary.");
            if (!group.Children.TryGetValue(new YamlScalarNode("Names"), out var namesNode) ||
                namesNode is not YamlMappingNode names)
            {
                throw Fail($"Group '{groupName}' has no Names.");
            }

            var entries = new List<InternalName>();
            foreach (var (nameKey, valueNode) in names.Children)
            {
                var name = ((YamlScalarNode)nameKey).Value ?? string.Empty;
                entries.Add(valueNode switch
                {
                    YamlScalarNode scalar => new InternalName(groupName, name, scalar.Value ?? string.Empty, null,
                        null),
                    YamlMappingNode mapping => new InternalName(groupName, name,
                        Scalar(mapping, "Value") ?? throw Fail($"{groupName}.{name} has no Value."),
                        Scalar(mapping, "Prelude"), Scalar(mapping, "Managed")),
                    _ => throw Fail($"{groupName}.{name} must be a value or a mapping.")
                });
            }

            groups.Add(new NameGroup(groupName, summary, entries));
        }

        return groups;
    }

    private static string? Scalar(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) ? (node as YamlScalarNode)?.Value : null;

    private static void Check(List<NameGroup> groups, string preludeDir, string coreDir)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var entry in group.Names)
            {
                var path = $"{entry.Group}.{entry.Name}";
                if (!entry.Value.StartsWith(RequiredPrefix, StringComparison.Ordinal))
                {
                    throw Fail($"{path} = '{entry.Value}' must start with {RequiredPrefix}.");
                }

                if (entry.Name.EndsWith(PrefixSuffix, StringComparison.Ordinal) != entry.Value.EndsWith('_'))
                {
                    throw Fail(
                        $"{path} = '{entry.Value}': a name ends in {PrefixSuffix} exactly when its value ends in '_'.");
                }

                if (!owners.TryAdd(entry.Value, path))
                {
                    throw Fail($"{path} and {owners[entry.Value]} both spell '{entry.Value}'.");
                }

                if (entry.PreludeFile is { } preludeFile)
                {
                    CheckDeclared(path, entry.Value, preludeDir, preludeFile, "Prelude");
                }

                if (entry.ManagedFile is { } managedFile)
                {
                    CheckDeclared(path, entry.Value, coreDir, managedFile, "Kassyi.GSharp.Core");
                }
            }
        }
    }

    private static void CheckDeclared(string path, string value, string directory, string file, string label)
    {
        var filePath = Path.Combine(directory, file);
        if (!File.Exists(filePath) ||
            !Regex.IsMatch(File.ReadAllText(filePath), $@"(?<![A-Za-z0-9_]){Regex.Escape(value)}(?![A-Za-z0-9_])"))
        {
            throw Fail($"{path} = '{value}' is not declared in {label}/{file}.");
        }
    }

    private static string Generate(List<NameGroup> groups)
    {
        using var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader();
        writer.Line("namespace Kassyi.GSharp.Compiler;");
        writer.Line();
        writer.Line("internal static partial class GSharpInternalNames");
        using (writer.Scope())
        {
            foreach (var group in groups)
            {
                writer.Line($"/// <summary>{EscapeXml(group.Summary)}</summary>");
                writer.Line($"internal static partial class {group.Name}");
                using (writer.Scope())
                {
                    foreach (var entry in group.Names)
                    {
                        writer.Line($"public const string {entry.Name} = \"{entry.Value}\";");
                    }
                }
            }

            var all = new List<InternalName>();
            foreach (var group in groups)
            {
                all.AddRange(group.Names);
            }

            writer.Line(
                "/// <summary>Every name in the vocabulary with its group, and the Prelude file that declares it, if any.</summary>");
            writer.Line(
                "internal static readonly ImmutableArray<(string Path, string Value, string? PreludeFile)> All = ImmutableArray.Create<(string, string, string?)>(");
            for (var i = 0; i < all.Count; i++)
            {
                var entry = all[i];
                var prelude = entry.PreludeFile is null ? "null" : $"\"{entry.PreludeFile}\"";
                var separator = i == all.Count - 1 ? ");" : ",";
                writer.Line($"(\"{entry.Group}.{entry.Name}\", {entry.Group}.{entry.Name}, {prelude}){separator}");
            }
        }

        return writer.ToString();
    }

    private static string EscapeXml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static InvalidOperationException Fail(string message) => new($"InternalNames.yml: {message}");
}
