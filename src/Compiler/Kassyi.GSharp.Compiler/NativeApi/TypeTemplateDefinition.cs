namespace Kassyi.GSharp.Compiler.NativeApi;

internal sealed class TypeTemplateDefinition
{
    public string Placeholder { get; set; } = "T";

    public ImmutableArray<string> Types { get; set; } = ImmutableArray<string>.Empty;
}
