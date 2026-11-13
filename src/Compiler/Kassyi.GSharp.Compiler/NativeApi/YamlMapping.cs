namespace Kassyi.GSharp.Compiler.NativeApi;

internal sealed class YamlMapping(int line) : YamlNode(line)
{
    public ImmutableArray<KeyValuePair<string, YamlNode>>.Builder Entries { get; } =
        ImmutableArray.CreateBuilder<KeyValuePair<string, YamlNode>>();

    public bool TryGetScalar(string key, [NotNullWhen(true)] out string? value)
    {
        if (TryGetValue(key, out var node) && node is YamlScalar { IsNull: false } scalar)
        {
            value = scalar.Value;
            return true;
        }

        value = null;
        return false;
    }

    public bool TryGetInt32(string key, out int value)
    {
        if (TryGetScalar(key, out var text) &&
            int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    public bool TryGetValue(string key, [NotNullWhen(true)] out YamlNode? value)
    {
        foreach (var entry in Entries)
        {
            if (entry.Key == key)
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
