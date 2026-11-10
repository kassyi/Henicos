namespace Kassyi.GSharp.Compiler.Generators;

/// <summary>
/// Generates the nested constant classes of <c>GSharpInternalNames</c> from <c>Resources/InternalNames.yml</c>, and
/// checks the vocabulary while doing so: values are unique, start with <c>__gsharp_</c>, end in <c>_</c> exactly when
/// the name ends in <c>Prefix</c>, and names marked with a Prelude file or a Managed (Core) file are declared in that
/// file.
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
        string? ManagedFile,
        int Line);

    private sealed record NameGroup(string Name, string Summary, List<InternalName> Names);

    /// <summary>
    /// Reads <paramref name="yamlPath"/>, checks it against the Prelude files in <paramref name="preludeDir"/> and the
    /// Core sources in <paramref name="coreDir"/>, and writes <c>GSharpInternalNames.g.cs</c> to
    /// <paramref name="outputDir"/>.
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

        YamlNode rootNode;
        try
        {
            rootNode = YamlSubsetParser.Parse(File.ReadAllText(yamlPath, Encoding.UTF8));
        }
        catch (NativeApiReadException ex)
        {
            throw new GeneratorInputException(yamlPath, ex.ToError().Line, ex.Message);
        }

        if (rootNode is not YamlMapping root)
        {
            throw new GeneratorInputException(
                yamlPath, rootNode.Line, "The internal names file must be a YAML mapping.");
        }

        var groups = new List<NameGroup>();
        foreach (var (groupKey, groupNode) in root.Entries)
        {
            groups.Add(ReadGroup(yamlPath, groupKey, groupNode));
        }

        return groups;
    }

    private static NameGroup ReadGroup(string yamlPath, string groupKey, YamlNode groupNode)
    {
        if (!SyntaxFacts.IsValidIdentifier(groupKey))
        {
            throw new GeneratorInputException(
                yamlPath, groupNode.Line, $"'{groupKey}' is not a valid C# identifier.");
        }

        if (groupNode is not YamlMapping group)
        {
            throw new GeneratorInputException(
                yamlPath, groupNode.Line, $"Group '{groupKey}' must be a mapping.");
        }

        if (!group.TryGetScalar("Summary", out var summary))
        {
            throw new GeneratorInputException(
                yamlPath, group.Line, $"Group '{groupKey}' has no Summary.");
        }

        if (!group.TryGetValue("Names", out var namesNode) || namesNode is not YamlMapping names)
        {
            throw new GeneratorInputException(
                yamlPath, namesNode?.Line ?? group.Line, $"Group '{groupKey}' has no Names.");
        }

        var entries = new List<InternalName>();
        foreach (var (nameKey, valueNode) in names.Entries)
        {
            entries.Add(ReadEntry(yamlPath, groupKey, nameKey, valueNode));
        }

        return new NameGroup(groupKey, summary, entries);
    }

    private static InternalName ReadEntry(
        string yamlPath, string groupKey, string nameKey, YamlNode valueNode)
    {
        if (!SyntaxFacts.IsValidIdentifier(nameKey))
        {
            throw new GeneratorInputException(
                yamlPath, valueNode.Line, $"'{nameKey}' is not a valid C# identifier.");
        }

        return valueNode switch
        {
            YamlScalar scalar => new InternalName(
                groupKey, nameKey, scalar.Value, null, null, scalar.Line),
            YamlMapping mapping => new InternalName(
                groupKey,
                nameKey,
                mapping.TryGetScalar("Value", out var val)
                    ? val
                    : throw new GeneratorInputException(
                        yamlPath, mapping.Line, $"{groupKey}.{nameKey} has no Value."),
                mapping.TryGetScalar("Prelude", out var prelude) ? prelude : null,
                mapping.TryGetScalar("Managed", out var managed) ? managed : null,
                mapping.Line),
            _ => throw new GeneratorInputException(
                yamlPath,
                valueNode.Line,
                $"{groupKey}.{nameKey} must be a value or a mapping.")
        };
    }

    private static void Check(string yamlPath, List<NameGroup> groups, string preludeDir, string coreDir)
    {
        var owners = new Dictionary<string, (string Path, int Line)>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var entry in group.Names)
            {
                var path = $"{entry.Group}.{entry.Name}";
                if (!entry.Value.StartsWith(RequiredPrefix, StringComparison.Ordinal))
                {
                    throw new GeneratorInputException(
                        yamlPath, entry.Line, $"{path} = '{entry.Value}' must start with {RequiredPrefix}.");
                }

                if (entry.Name.EndsWith(PrefixSuffix, StringComparison.Ordinal) != entry.Value.EndsWith('_'))
                {
                    throw new GeneratorInputException(
                        yamlPath,
                        entry.Line,
                        $"{path} = '{entry.Value}': a name ends in {PrefixSuffix} exactly when its value ends in '_'.");
                }

                if (!owners.TryAdd(entry.Value, (path, entry.Line)))
                {
                    throw new GeneratorInputException(
                        yamlPath, entry.Line, $"{path} and {owners[entry.Value].Path} both spell '{entry.Value}'.");
                }

                if (entry.PreludeFile is { } preludeFile)
                {
                    CheckDeclared(yamlPath, path, entry.Value, preludeDir, preludeFile, "Prelude", entry.Line);
                }

                if (entry.ManagedFile is { } managedFile)
                {
                    CheckDeclared(yamlPath, path, entry.Value, coreDir, managedFile, "Kassyi.GSharp.Core", entry.Line);
                }
            }
        }
    }

    private static void CheckDeclared(
        string yamlPath, string path, string value, string directory, string file, string label, int line)
    {
        var filePath = Path.Combine(directory, file);
        if (!File.Exists(filePath) || !ContainsIdentifier(File.ReadAllText(filePath), value))
        {
            throw new GeneratorInputException(
                yamlPath, line, $"{path} = '{value}' is not declared in {label}/{file}.");
        }
    }

    /// <summary>
    /// Whether <paramref name="identifier"/> occurs in <paramref name="text"/> as a whole identifier.
    /// </summary>
    private static bool ContainsIdentifier(string text, string identifier)
    {
        for (var index = text.IndexOf(identifier, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(identifier, index + 1, StringComparison.Ordinal))
        {
            var end = index + identifier.Length;
            if ((index == 0 || !IsIdentifierChar(text[index - 1]))
                && (end == text.Length || !IsIdentifierChar(text[end])))
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
                "/// <summary>Every name in the vocabulary with its group, and the Prelude file that declares it, " +
                "if any.</summary>");
            writer.Line(
                "internal static readonly ImmutableArray<(string Path, string Value, string? PreludeFile)> All = " +
                "ImmutableArray.Create<(string, string, string?)>(");
            for (var i = 0; i < all.Count; i++)
            {
                var entry = all[i];
                var prelude = entry.PreludeFile is null ? "null" : CSharpStringLiteral.Quote(entry.PreludeFile);
                var separator = i == all.Count - 1 ? ");" : ",";
                writer.Line(
                    $"({CSharpStringLiteral.Quote(entry.Group + "." + entry.Name)}, " +
                    $"{entry.Group}.{entry.Name}, {prelude}){separator}");
            }
        }

        return writer.ToString();
    }
}
