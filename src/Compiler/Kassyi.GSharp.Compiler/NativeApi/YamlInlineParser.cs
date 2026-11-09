namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Parses the text of one line of the YAML subset of <see cref="YamlSubsetParser"/> after its indentation: flow
/// collections, plain and quoted scalars, and the key of a <c>key: value</c> pair. Line numbers are 1-based.
/// </summary>
internal static class YamlInlineParser
{
    public static YamlNode ParseFlow(string text, ref int position, int lineNumber)
    {
        var close = text[position] == '[' ? ']' : '}';
        position++;

        YamlNode collection = close == ']' ? new YamlSequence(lineNumber) : new YamlMapping(lineNumber);
        position = SkipSpaces(text, position);
        if (position < text.Length && text[position] == close)
        {
            position++;
            return collection;
        }

        while (true)
        {
            position = SkipSpaces(text, position);
            if (position >= text.Length)
            {
                throw YamlSubsetParser.Error(lineNumber - 1, $"Unterminated flow collection; expected '{close}'.");
            }

            ParseFlowEntry(collection, text, ref position, lineNumber);

            position = SkipSpaces(text, position);
            if (position >= text.Length)
            {
                throw YamlSubsetParser.Error(lineNumber - 1, $"Unterminated flow collection; expected '{close}'.");
            }

            if (text[position] == ',')
            {
                position++;
                continue;
            }

            if (text[position] == close)
            {
                position++;
                return collection;
            }

            throw YamlSubsetParser.Error(lineNumber - 1, $"Expected ',' or '{close}' in flow collection.");
        }
    }

    // One item of a flow sequence, or one 'key: value' entry of a flow mapping (a missing value is null)
    private static void ParseFlowEntry(YamlNode collection, string text, ref int position, int lineNumber)
    {
        if (collection is YamlSequence sequence)
        {
            sequence.Items.Add(ParseFlowItem(text, ref position, lineNumber));
            return;
        }

        var mapping = (YamlMapping)collection;
        var key = ParseFlowKey(text, ref position, lineNumber);
        if (mapping.TryGetValue(key, out _))
        {
            throw YamlSubsetParser.Error(lineNumber - 1, $"Duplicate key '{key}'.");
        }

        position = SkipSpaces(text, position);
        var value = position < text.Length && text[position] is ',' or '}'
            ? new YamlScalar(lineNumber, string.Empty, isNull: true)
            : ParseFlowItem(text, ref position, lineNumber);

        mapping.Entries.Add(new KeyValuePair<string, YamlNode>(key, value));
    }

    private static YamlNode ParseFlowItem(string text, ref int position, int lineNumber)
    {
        position = SkipSpaces(text, position);
        if (position < text.Length && text[position] is '[' or '{')
        {
            return ParseFlow(text, ref position, lineNumber);
        }

        return ParseInlineScalar(text, ref position, lineNumber, flowContext: true);
    }

    private static string ParseFlowKey(string text, ref int position, int lineNumber)
    {
        string key;
        if (text[position] is '"' or '\'')
        {
            key = ParseQuoted(text, ref position, lineNumber);
        }
        else
        {
            var start = position;
            while (position < text.Length && text[position] != ':' && text[position] is not (',' or '}'))
            {
                position++;
            }

            key = text.AsSpan(start, position - start).Trim().ToString();
        }

        position = SkipSpaces(text, position);
        if (position >= text.Length || text[position] != ':')
        {
            throw YamlSubsetParser.Error(lineNumber - 1, "Expected ':' after flow mapping key.");
        }

        position++;
        if (key.Length == 0)
        {
            throw YamlSubsetParser.Error(lineNumber - 1, "Empty key in flow mapping.");
        }

        return key;
    }

    public static YamlScalar ParseInlineScalar(string text, ref int position, int lineNumber, bool flowContext)
    {
        var c = text[position];
        if (c is '"' or '\'')
        {
            return new YamlScalar(lineNumber, ParseQuoted(text, ref position, lineNumber));
        }

        if (c is '&' or '*' or '!' or '%' or '@' or '`')
        {
            throw YamlSubsetParser.Error(lineNumber - 1,
                $"Unsupported YAML construct starting with '{c}' " +
                "(anchors, aliases, tags and directives are not supported).");
        }

        var start = position;
        while (position < text.Length)
        {
            var current = text[position];
            if (current == '#' && position > start && text[position - 1] == ' ')
            {
                break;
            }

            if (flowContext && current is ',' or ']' or '}')
            {
                break;
            }

            if (current == ':' && (position + 1 >= text.Length || text[position + 1] == ' ') && !flowContext)
            {
                throw YamlSubsetParser.Error(lineNumber - 1, "Mapping values are not allowed here; quote the scalar.");
            }

            position++;
        }

        var value = text.AsSpan(start, position - start).TrimEnd().ToString();
        return new YamlScalar(lineNumber, value);
    }

#pragma warning disable RS0030 // Input parsing: decodes a YAML scalar character by character
    private static string ParseQuoted(string text, ref int position, int lineNumber)
    {
        var quote = text[position];
        position++;
        var builder = new StringBuilder();
        while (position < text.Length)
        {
            var c = text[position];
            if (c == quote)
            {
                // '' inside a single-quoted scalar is one quote
                if (quote == '\'' && position + 1 < text.Length && text[position + 1] == '\'')
                {
                    builder.Append('\'');
                    position += 2;
                    continue;
                }

                position++;
                return builder.ToString();
            }

            if (quote == '"' && c == '\\')
            {
                if (position + 1 >= text.Length)
                {
                    break;
                }

                builder.Append(DecodeEscape(text[position + 1], lineNumber));
                position += 2;
                continue;
            }

            builder.Append(c);
            position++;
        }

        throw YamlSubsetParser.Error(lineNumber - 1,
            "Unterminated quoted scalar (multi-line quoted scalars are not supported).");
    }
#pragma warning restore RS0030

    private static char DecodeEscape(char escaped, int lineNumber) =>
        escaped switch
        {
            'n' => '\n',
            't' => '\t',
            'r' => '\r',
            '0' => '\0',
            '"' => '"',
            '\\' => '\\',
            '/' => '/',
            ' ' => ' ',
            var other => throw YamlSubsetParser.Error(lineNumber - 1, $"Unsupported escape sequence '\\{other}'.")
        };

    public static void EnsureLineEnd(string text, int position, int lineNumber)
    {
        position = SkipSpaces(text, position);
        if (position < text.Length && text[position] != '#')
        {
            throw YamlSubsetParser.Error(lineNumber - 1, $"Unexpected content '{text[position..]}'.");
        }
    }

    public static bool TrySplitKey(string content, int lineNumber, out string key, out string rest)
    {
        key = string.Empty;
        rest = string.Empty;
        if (content.Length == 0)
        {
            return false;
        }

        var colon = content[0] is '"' or '\''
            ? FindQuotedKeyColon(content, lineNumber, out var parsedKey)
            : FindPlainKeyColon(content, out parsedKey);
        if (colon < 0)
        {
            return false;
        }

        key = parsedKey;
        rest = content[(colon + 1)..];
        return true;
    }

    // The index of the ':' after a quoted key, or -1 when the line is not '"key": value'
    private static int FindQuotedKeyColon(string content, int lineNumber, out string key)
    {
        var position = 0;
        key = ParseQuoted(content, ref position, lineNumber);
        position = SkipSpaces(content, position);
        if (position >= content.Length || content[position] != ':' ||
            (position + 1 < content.Length && content[position + 1] != ' '))
        {
            return -1;
        }

        return position;
    }

    // The index of the first ': ' (or a final ':') after a plain key, or -1 when the line is not 'key: value'
    private static int FindPlainKeyColon(string content, out string key)
    {
        key = string.Empty;
        if (content[0] is '[' or '{' or '#' or '|' or '>')
        {
            return -1;
        }

        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '#' && i > 0 && content[i - 1] == ' ')
            {
                return -1;
            }

            if (content[i] == ':' && (i + 1 == content.Length || content[i + 1] == ' '))
            {
                // An empty key (a line that starts with ':') is not a key
                if (i == 0)
                {
                    return -1;
                }

                key = content.AsSpan(0, i).TrimEnd().ToString();
                return i;
            }
        }

        return -1;
    }

    public static int SkipSpaces(string text, int position)
    {
        while (position < text.Length && text[position] == ' ')
        {
            position++;
        }

        return position;
    }
}
