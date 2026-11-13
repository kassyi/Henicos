namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// A node produced by <see cref="YamlSubsetParser"/>.
/// </summary>
internal abstract class YamlNode(int line)
{
    /// <summary>Gets the 1-based source line on which the node starts.</summary>
    public int Line { get; } = line;
}
