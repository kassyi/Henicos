namespace Kassyi.GSharp.Compiler.NativeApi;

internal sealed class ParameterDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public int TypeLine { get; set; }

    public string? Description { get; set; }

    public string? DefaultValue { get; set; }
}
