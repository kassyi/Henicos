
namespace Kassyi.GSharp.Compiler.Generators;

/// <summary>Represents a YAML dialect definition describing intrinsic and type mapping rules for target backends.</summary>
public class DialectDefinition
{
    /// <summary>Gets or sets the dialect name.</summary>
    [YamlMember(Alias = "Dialect")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional base dialect name this dialect inherits from.</summary>
    [YamlMember(Alias = "Inherits")]
    public string? Inherits
    {
        get; set;
    }

    /// <summary>Gets or sets the output the dialect emits: <c>Cuda</c> or <c>Cpp</c>. Inherited when omitted.</summary>
    [YamlMember(Alias = "Backend")]
    public string? Backend
    {
        get; set;
    }

    /// <summary>Gets or sets the dictionary of mapped types.</summary>
    [YamlMember(Alias = "Types")]
    public Dictionary<string, string> Types { get; set; } = new();

    /// <summary>Gets or sets the dictionary of mapped intrinsic expressions.</summary>
    [YamlMember(Alias = "Intrinsics")]
    public Dictionary<string, string> Intrinsics { get; set; } = new();

    /// <summary>Gets or sets the capabilities of this dialect.</summary>
    [YamlMember(Alias = "Capabilities")]
    public string? Capabilities
    {
        get; set;
    }
}
