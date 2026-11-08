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
        Check(yamlPath, groups, preludeDir, coreDir);

        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "GSharpInternalNames.g.cs"), Generate(groups), Encoding.UTF8);
    }

    private static List<NameGroup> Read(string yamlPath)
    {
        if (!File.Exists(yamlPath))
        {
            throw new GeneratorInputException(yamlPath, 0, "The internal names file does not exist.");
        }

        var stream = new YamlStream();
        using (var reader = new StreamReader(yamlPath, Encoding.UTF8))
        {
            try
            {
                stream.Load(reader);
            }
            catch (YamlDotNet.Core.YamlException ex)
            {
                throw new GeneratorInputException(yamlPath, (int)ex.Start.Line, ex.Message);
            }
        }

        var groups = new List<NameGroup>();
        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        foreach (var (groupKey, groupNode) in root.Children)
        {
            var groupName = Identifier(yamlPath, groupKey);
            var group = (YamlMappingNode)groupNode;
            var summary = Scalar(group, "Summary") ?? throw Fail(yamlPath, $"Group '{groupName}' has no Summary.");
            if (!group.Children.TryGetValue(new YamlScalarNode("Names"), out var namesNode) ||
                namesNode is not YamlMappingNode names)
            {
                throw Fail(yamlPath, $"Group '{groupName}' has no Names.");
            }

            var entries = new List<InternalName>();
            foreach (var (nameKey, valueNode) in names.Children)
            {
                var name = Identifier(yamlPath, nameKey);
                entries.Add(valueNode switch
                {
                    YamlScalarNode scalar => new InternalName(groupName, name, scalar.Value ?? string.Empty, null,
                        null),
                    YamlMappingNode mapping => new InternalName(groupName, name,
                        Scalar(mapping, "Value") ?? throw Fail(yamlPath, $"{groupName}.{name} has no Value."),
                        Scalar(mapping, "Prelude"), Scalar(mapping, "Managed")),
                    _ => throw Fail(yamlPath, $"{groupName}.{name} must be a value or a mapping.")
                });
            }

            groups.Add(new NameGroup(groupName, summary, entries));
        }

        return groups;
    }

    // Group and entry names become C# class and constant names.
    private static string Identifier(string yamlPath, YamlDotNet.RepresentationModel.YamlNode key)
    {
        var value = (key as YamlScalarNode)?.Value ?? string.Empty;
        return SyntaxFacts.IsValidIdentifier(value)
            ? value
            : throw new GeneratorInputException(yamlPath, (int)key.Start.Line, $"'{value}' is not a valid C# identifier.");
    }

    private static string? Scalar(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) ? (node as YamlScalarNode)?.Value : null;

    private static void Check(string yamlPath, List<NameGroup> groups, string preludeDir, string coreDir)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var entry in group.Names)
            {
                var path = $"{entry.Group}.{entry.Name}";
                if (!entry.Value.StartsWith(RequiredPrefix, StringComparison.Ordinal))
                {
                    throw Fail(yamlPath, $"{path} = '{entry.Value}' must start with {RequiredPrefix}.");
                }

                if (entry.Name.EndsWith(PrefixSuffix, StringComparison.Ordinal) != entry.Value.EndsWith('_'))
                {
                    throw Fail(
                        yamlPath,
                        $"{path} = '{entry.Value}': a name ends in {PrefixSuffix} exactly when its value ends in '_'.");
                }

                if (!owners.TryAdd(entry.Value, path))
                {
                    throw Fail(yamlPath, $"{path} and {owners[entry.Value]} both spell '{entry.Value}'.");
                }

                if (entry.PreludeFile is { } preludeFile)
                {
                    CheckDeclared(yamlPath, path, entry.Value, preludeDir, preludeFile, "Prelude");
                }

                if (entry.ManagedFile is { } managedFile)
                {
                    CheckDeclared(yamlPath, path, entry.Value, coreDir, managedFile, "Kassyi.GSharp.Core");
                }
            }
        }
    }

    private static void CheckDeclared(string yamlPath, string path, string value, string directory, string file, string label)
    {
        var filePath = Path.Combine(directory, file);
        if (!File.Exists(filePath) || !ContainsIdentifier(File.ReadAllText(filePath), value))
        {
            throw Fail(yamlPath, $"{path} = '{value}' is not declared in {label}/{file}.");
        }
    }

    /// <summary>Whether <paramref name="identifier"/> occurs in <paramref name="text"/> as a whole identifier.</summary>
    private static bool ContainsIdentifier(string text, string identifier)
    {
        for (var index = text.IndexOf(identifier, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(identifier, index + 1, StringComparison.Ordinal))
        {
            var end = index + identifier.Length;
            if ((index == 0 || !IsIdentifierChar(text[index - 1])) && (end == text.Length || !IsIdentifierChar(text[end])))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIdentifierChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

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
                writer.Line($"/// <summary>{XmlDocEscaping.Escape(group.Summary)}</summary>");
                writer.Line($"internal static partial class {group.Name}");
                using (writer.Scope())
                {
                    foreach (var entry in group.Names)
                    {
                        writer.Line($"public const string {entry.Name} = {CSharpStringLiteral.Quote(entry.Value)};");
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
                var prelude = entry.PreludeFile is null ? "null" : CSharpStringLiteral.Quote(entry.PreludeFile);
                var separator = i == all.Count - 1 ? ");" : ",";
                writer.Line($"({CSharpStringLiteral.Quote(entry.Group + "." + entry.Name)}, {entry.Group}.{entry.Name}, {prelude}){separator}");
            }
        }

        return writer.ToString();
    }

    private static GeneratorInputException Fail(string yamlPath, string message) => new(yamlPath, 0, message);
}
