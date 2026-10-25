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

    /// <summary>Parses a <see cref="DialectDefinition"/> from YAML text using <see cref="YamlSubsetParser"/>.</summary>
    public static DialectDefinition? TryParse(string text)
    {
        YamlNode root;
        try
        {
            root = YamlSubsetParser.Parse(text);
        }
        catch
        {
            return null;
        }

        if (root is not YamlMapping mapping || !mapping.TryGetScalar("Dialect", out var dialectName) ||
            string.IsNullOrEmpty(dialectName))
        {
            return null;
        }

        var def = new DialectDefinition
        {
            Name = dialectName,
            Inherits = mapping.TryGetScalar("Inherits", out var inherits) ? inherits : null,
            Backend = mapping.TryGetScalar("Backend", out var backend) ? backend : null,
            Capabilities = mapping.TryGetScalar("Capabilities", out var capabilities) ? capabilities : null,
        };

        if (mapping.TryGetValue("Types", out var typesNode) && typesNode is YamlMapping typesMapping)
        {
            foreach (var entry in typesMapping.Entries)
            {
                if (entry.Value is YamlScalar { IsNull: false } scalar)
                {
                    def.Types[entry.Key] = new DialectTypeMapping(scalar.Value);
                }
                else if (entry.Value is YamlMapping flow && flow.TryGetScalar("cpp", out var cppType))
                {
                    int? size = flow.TryGetInt32("size", out var s) ? s : null;
                    int? align = flow.TryGetInt32("align", out var a) ? a : null;
                    def.Types[entry.Key] = new DialectTypeMapping(cppType, size, align);
                }
            }
        }

        if (mapping.TryGetValue("Layout", out var layoutNode) && layoutNode is YamlMapping layoutMapping)
        {
            foreach (var entry in layoutMapping.Entries)
            {
                if (entry.Value is YamlMapping flow &&
                    flow.TryGetInt32("size", out var size) &&
                    flow.TryGetInt32("align", out var align))
                {
                    def.Layout[entry.Key] = new DialectLayoutEntry(size, align);
                }
            }
        }

        if (mapping.TryGetValue("Intrinsics", out var intrinsicsNode) && intrinsicsNode is YamlMapping intrinsicsMapping)
        {
            foreach (var entry in intrinsicsMapping.Entries)
            {
                if (entry.Value is YamlScalar { IsNull: false } scalar)
                {
                    def.Intrinsics[entry.Key] = scalar.Value;
                }
            }
        }

        return def;
    }
}
