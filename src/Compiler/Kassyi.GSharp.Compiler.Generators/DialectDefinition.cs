namespace Kassyi.GSharp.Compiler.Generators;

/// <summary>Represents a mapped type entry in a YAML dialect definition, with optional byte size and alignment.</summary>
public readonly record struct DialectTypeMapping(string CppType, int? Size = null, int? Align = null)
{
    public bool HasLayout => Size.HasValue && Align.HasValue;
}

/// <summary>Represents a byte size and alignment pair in a YAML dialect definition.</summary>
public readonly record struct DialectLayoutEntry(int Size, int Align);

/// <summary>Represents a YAML dialect definition describing intrinsic and type mapping rules for target backends.</summary>
public class DialectDefinition
{
    /// <summary>Gets or sets the dialect name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional base dialect name this dialect inherits from.</summary>
    public string? Inherits
    {
        get; set;
    }

    /// <summary>Gets or sets the output the dialect emits: <c>Cuda</c> or <c>Cpp</c>. Inherited when omitted.</summary>
    public string? Backend
    {
        get; set;
    }

    /// <summary>Gets or sets the dictionary of mapped types.</summary>
    public Dictionary<string, DialectTypeMapping> Types { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the dictionary of layout entries (e.g., <c>ArrayView</c>, <c>Pointer</c>).</summary>
    public Dictionary<string, DialectLayoutEntry> Layout { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the dictionary of mapped intrinsic expressions.</summary>
    public Dictionary<string, string> Intrinsics { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the capabilities of this dialect.</summary>
    public string? Capabilities
    {
        get; set;
    }

    /// <summary>Gets or sets the 1-based line of the <c>Dialect</c> key,
    /// for errors about the dialect as a whole.</summary>
    public int Line
    {
        get; set;
    }

    /// <summary>Gets or sets the file the dialect was read from, for errors.</summary>
    public string Path { get; set; } = string.Empty;

    private static readonly string[] s_topLevelKeys =
        ["Dialect", "Inherits", "Backend", "Capabilities", "Types", "Layout", "Intrinsics"];

    /// <summary>
    /// Parses a <see cref="DialectDefinition"/> from YAML text using <see cref="YamlSubsetParser"/>.
    /// </summary>
    /// <param name="text">The YAML text.</param>
    /// <param name="path">The file the text was read from; errors name it.</param>
    /// <exception cref="GeneratorInputException">The text is not valid YAML or does not describe a dialect.</exception>
    public static DialectDefinition Parse(string text, string path)
    {
        var mapping = ParseRootMapping(text, path);
        ValidateTopLevelKeys(mapping, path);

        var def = new DialectDefinition
        {
            Path = path,
            Name = RequireIdentifier(mapping, "Dialect", path, required: true) ?? string.Empty,
            Line = mapping.TryGetValue("Dialect", out var dialectNode) ? dialectNode.Line : 0,
            Inherits = RequireIdentifier(mapping, "Inherits", path, required: false),
            Backend = OptionalScalar(mapping, "Backend", path),
            Capabilities = OptionalScalar(mapping, "Capabilities", path),
        };

        ParseTypes(mapping, path, def.Types);
        ParseLayout(mapping, path, def.Layout);
        ParseIntrinsics(mapping, path, def.Intrinsics);

        return def;
    }

    private static YamlMapping ParseRootMapping(string text, string path)
    {
        YamlNode root;
        try
        {
            root = YamlSubsetParser.Parse(text);
        }
        catch (NativeApiReadException ex)
        {
            throw new GeneratorInputException(path, ex.ToError().Line, ex.Message);
        }

        if (root is not YamlMapping mapping)
        {
            throw new GeneratorInputException(path, root.Line,
                "The document root must be a mapping with a 'Dialect' key.");
        }

        return mapping;
    }

    private static void ValidateTopLevelKeys(YamlMapping mapping, string path)
    {
        foreach (var entry in mapping.Entries)
        {
            if (Array.IndexOf(s_topLevelKeys, entry.Key) < 0)
            {
                throw new GeneratorInputException(path, entry.Value.Line,
                    $"Unknown key '{entry.Key}'. Expected one of: {string.Join(", ", s_topLevelKeys)}.");
            }
        }
    }

    private static void ParseTypes(YamlMapping mapping, string path, Dictionary<string, DialectTypeMapping> types)
    {
        foreach (var entry in Entries(mapping, "Types", path))
        {
            types[entry.Key] = ParseTypeMapping(entry, path);
        }
    }

    private static DialectTypeMapping ParseTypeMapping(KeyValuePair<string, YamlNode> entry, string path)
    {
        if (entry.Value is YamlScalar { IsNull: false } scalar)
        {
            return new DialectTypeMapping(scalar.Value);
        }

        if (entry.Value is YamlMapping flow && flow.TryGetScalar("cpp", out var cppType))
        {
            int? size = OptionalInt32(flow, "size", path);
            int? align = OptionalInt32(flow, "align", path);
            if (size.HasValue != align.HasValue)
            {
                throw new GeneratorInputException(path, entry.Value.Line,
                    $"The type '{entry.Key}' must give both 'size' and 'align', or neither.");
            }

            return new DialectTypeMapping(cppType, size, align);
        }

        throw new GeneratorInputException(path, entry.Value.Line,
            $"The type '{entry.Key}' must map to a C++ type name or to " +
            "{ cpp: <type>, size: <bytes>, align: <bytes> }.");
    }

    private static void ParseLayout(YamlMapping mapping, string path, Dictionary<string, DialectLayoutEntry> layout)
    {
        foreach (var entry in Entries(mapping, "Layout", path))
        {
            if (entry.Value is not YamlMapping flow ||
                OptionalInt32(flow, "size", path) is not { } size ||
                OptionalInt32(flow, "align", path) is not { } align)
            {
                throw new GeneratorInputException(path, entry.Value.Line,
                    $"The layout '{entry.Key}' must be {{ size: <bytes>, align: <bytes> }}.");
            }

            layout[entry.Key] = new DialectLayoutEntry(size, align);
        }
    }

    private static void ParseIntrinsics(YamlMapping mapping, string path, Dictionary<string, string> intrinsics)
    {
        foreach (var entry in Entries(mapping, "Intrinsics", path))
        {
            if (entry.Value is not YamlScalar { IsNull: false } scalar)
            {
                throw new GeneratorInputException(path, entry.Value.Line,
                    $"The intrinsic '{entry.Key}' must map to a C++ expression.");
            }

            intrinsics[entry.Key] = scalar.Value;
        }
    }

    private static string? OptionalScalar(YamlMapping mapping, string key, string path)
    {
        if (!mapping.TryGetValue(key, out var node))
        {
            return null;
        }

        return node is YamlScalar { IsNull: false } scalar
            ? scalar.Value
            : throw new GeneratorInputException(path, node.Line, $"'{key}' must be a single value.");
    }

    // The dialect name and its base name become C# class names (<name>Dialect).
    private static string? RequireIdentifier(YamlMapping mapping, string key, string path, bool required)
    {
        var value = OptionalScalar(mapping, key, path);
        if (value is null)
        {
            return required
                ? throw new GeneratorInputException(
                    path, mapping.Line, $"The '{key}' key is missing; it names the dialect.")
                : null;
        }

        if (!SyntaxFacts.IsValidIdentifier(value))
        {
            mapping.TryGetValue(key, out var node);
            throw new GeneratorInputException(path, node?.Line ?? mapping.Line,
                $"'{key}: {value}' is not a valid C# identifier; it is used in the class name {value}Dialect.");
        }

        return value;
    }

    private static int? OptionalInt32(YamlMapping mapping, string key, string path)
    {
        if (!mapping.TryGetValue(key, out var node))
        {
            return null;
        }

        return mapping.TryGetInt32(key, out var value) && value > 0
            ? value
            : throw new GeneratorInputException(path, node.Line, $"'{key}' must be a positive integer.");
    }

    private static ImmutableArray<KeyValuePair<string, YamlNode>> Entries(YamlMapping mapping, string key, string path)
    {
        if (!mapping.TryGetValue(key, out var node))
        {
            return [];
        }

        return node is YamlMapping section
            ? section.Entries.ToImmutable()
            : throw new GeneratorInputException(path, node.Line, $"'{key}' must be a mapping.");
    }
}
