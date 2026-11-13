namespace Kassyi.GSharp.Compiler.NativeApi;

internal sealed class YamlSequence(int line) : YamlNode(line)
{
    public ImmutableArray<YamlNode>.Builder Items { get; } = ImmutableArray.CreateBuilder<YamlNode>();
}
