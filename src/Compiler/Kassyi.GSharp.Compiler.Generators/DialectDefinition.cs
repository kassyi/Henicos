using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace Kassyi.GSharp.Compiler.Generators;

public class DialectDefinition
{
    [YamlMember(Alias = "Dialect")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "Inherits")]
    public string? Inherits
    {
        get; set;
    }

    [YamlMember(Alias = "RequiredHeaders")]
    public List<string> RequiredHeaders { get; set; } = new();

    [YamlMember(Alias = "StructPrefix")]
    public string StructPrefix { get; set; } = string.Empty;

    [YamlMember(Alias = "MethodPrefix")]
    public string MethodPrefix { get; set; } = string.Empty;

    [YamlMember(Alias = "Types")]
    public Dictionary<string, string> Types { get; set; } = new();

    [YamlMember(Alias = "Intrinsics")]
    public Dictionary<string, string> Intrinsics { get; set; } = new();
}
