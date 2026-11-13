namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>A scalar value. <see cref="IsNull"/> is set for an empty value such as <c>key:</c>.</summary>
internal sealed class YamlScalar(int line, string value, bool isNull = false) : YamlNode(line)
{
    public string Value { get; } = value;

    public bool IsNull { get; } = isNull;
}
