namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Reads and validates the values of a YAML native API definition (scalars, lists, sequences, identifiers) for
/// <see cref="NativeApiYamlReader"/> and <see cref="NativeApiMethodReader"/>; a schema error is a
/// <see cref="NativeApiReadException"/> with the line of the value.
/// </summary>
internal static class NativeApiYamlValues
{
    // Reads each item of the sequence under 'key' with readItem, which gets state as its second argument
    internal static ImmutableArray<T> ReadSequence<T, TState>(
        YamlNode node, string key, TState state, Func<YamlNode, TState, T> readItem)
    {
        if (node is not YamlSequence sequence)
        {
            throw Schema(node.Line, $"'{key}' must be a sequence.");
        }

        var builder = ImmutableArray.CreateBuilder<T>(sequence.Items.Count);
        foreach (var item in sequence.Items)
        {
            builder.Add(readItem(item, state));
        }

        return builder.MoveToImmutable();
    }

    internal static ImmutableArray<string> ReadStringList(YamlMapping mapping, string key)
    {
        if (!mapping.TryGetValue(key, out var node) || IsNull(node))
        {
            return ImmutableArray<string>.Empty;
        }

        if (node is YamlScalar single)
        {
            return ImmutableArray.Create(Normalize(single.Value));
        }

        if (node is not YamlSequence sequence)
        {
            throw Schema(node.Line, $"'{key}' must be a scalar or a sequence of scalars.");
        }

        var builder = ImmutableArray.CreateBuilder<string>(sequence.Items.Count);
        foreach (var item in sequence.Items)
        {
            if (item is not YamlScalar scalar || scalar.IsNull)
            {
                throw Schema(item.Line, $"Each entry of '{key}' must be a non-empty scalar.");
            }

            builder.Add(Normalize(scalar.Value));
        }

        return builder.MoveToImmutable();
    }

    internal static string RequireScalar(YamlMapping mapping, string key) =>
        OptionalScalar(mapping, key) is { Length: > 0 } value
            ? value
            : throw Schema(mapping.Line, $"'{key}' is required.");

    internal static string? OptionalScalar(YamlMapping mapping, string key)
    {
        if (!mapping.TryGetValue(key, out var node) || IsNull(node))
        {
            return null;
        }

        return node is YamlScalar scalar
            ? Normalize(scalar.Value)
            : throw Schema(node.Line, $"'{key}' must be a scalar value.");
    }

    internal static bool OptionalBoolean(YamlMapping mapping, string key) =>
        OptionalScalar(mapping, key) switch
        {
            null => false,
            var s when string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) => true,
            var s when string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) => false,
            _ => throw Schema(LineOf(mapping, key), $"'{key}' must be 'true' or 'false'.")
        };

    /// <summary>Whether every dot-separated segment of <paramref name="value"/> is a C# identifier.</summary>
    internal static bool IsValidDottedName(string value)
    {
        var start = 0;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i < value.Length && value[i] != '.')
            {
                continue;
            }

            if (!SyntaxFacts.IsValidIdentifier(value[start..i]))
            {
                return false;
            }

            start = i + 1;
        }

        return true;
    }

    internal static void ValidateIdentifier(YamlMapping mapping, string key, string value)
    {
        if (!SyntaxFacts.IsValidIdentifier(value))
        {
            throw Schema(LineOf(mapping, key), $"'{key}' value '{value}' is not a valid C# identifier.");
        }
    }

    internal static int LineOf(YamlMapping mapping, string key) =>
        mapping.TryGetValue(key, out var node) ? node.Line : mapping.Line;

    internal static bool IsNull(YamlNode node) => node is YamlScalar { IsNull: true };

    internal static string Normalize(string value) => value.TrimEnd('\n', '\r');

    internal static NativeApiReadException Schema(int line, string message) =>
        new(NativeApiReadErrorKind.Schema, line, message);
}
