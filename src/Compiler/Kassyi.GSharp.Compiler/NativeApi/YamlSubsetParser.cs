namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// A node produced by <see cref="YamlSubsetParser"/>.
/// </summary>
internal abstract class YamlNode(int line)
{
    /// <summary>Gets the 1-based source line on which the node starts.</summary>
    public int Line { get; } = line;
}

/// <summary>A scalar value. <see cref="IsNull"/> is set for an empty value such as <c>key:</c>.</summary>
internal sealed class YamlScalar(int line, string value, bool isNull = false) : YamlNode(line)
{
    public string Value { get; } = value;

    public bool IsNull { get; } = isNull;
}

internal sealed class YamlSequence(int line) : YamlNode(line)
{
    public ImmutableArray<YamlNode>.Builder Items { get; } = ImmutableArray.CreateBuilder<YamlNode>();
}

internal sealed class YamlMapping(int line) : YamlNode(line)
{
    public ImmutableArray<KeyValuePair<string, YamlNode>>.Builder Entries { get; } =
        ImmutableArray.CreateBuilder<KeyValuePair<string, YamlNode>>();

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

/// <summary>
/// Reflection-free parser for the YAML subset used by G# native API definitions.
/// </summary>
/// <remarks>
/// Supported: block mappings and sequences (including indentless sequences), single-line flow
/// collections (<c>[a, b]</c>, <c>{ k: v }</c>), plain / single-quoted / double-quoted scalars,
/// literal (<c>|</c>) and folded (<c>&gt;</c>) block scalars, comments and a leading <c>---</c> marker.
/// Anchors, aliases, tags, multi-line flow collections and multiple documents are rejected with a
/// <see cref="NativeApiReadException"/> carrying the offending line.
/// </remarks>
internal sealed class YamlSubsetParser
{
    private readonly string[] _lines;
    private readonly int[] _indent;
    private int _index;

    private YamlSubsetParser(string text)
    {
        _lines = SplitLines(text);
        _indent = new int[_lines.Length];
        for (var i = 0; i < _indent.Length; i++)
        {
            _indent[i] = -1;
        }
    }

    /// <summary>
    /// Parses <paramref name="text"/> into a node tree. Returns a null scalar for an empty document.
    /// </summary>
    /// <exception cref="NativeApiReadException">The text is not valid within the supported subset.</exception>
    public static YamlNode Parse(string text)
    {
        var parser = new YamlSubsetParser(text);
        return parser.ParseDocument();
    }

    private YamlNode ParseDocument()
    {
        if (!MoveToSignificant())
        {
            return new YamlScalar(1, string.Empty, isNull: true);
        }

        if (IsDocumentMarker(_index))
        {
            _index++;
            if (!MoveToSignificant())
            {
                return new YamlScalar(1, string.Empty, isNull: true);
            }
        }

        var root = ParseBlock(Indent(_index));
        if (MoveToSignificant())
        {
            throw Error(_index, IsDocumentMarker(_index)
                ? "Multiple YAML documents are not supported."
                : "Unexpected indentation.");
        }

        return root;
    }

    private bool IsDocumentMarker(int index) =>
        Indent(index) == 0 && _lines[index].AsSpan().TrimEnd().SequenceEqual("---".AsSpan());

    private YamlNode ParseBlock(int indent)
    {
        var content = Content(_index);
        return IsSequenceItem(content) ? ParseSequence(indent) : ParseMapping(indent);
    }

    private YamlMapping ParseMapping(int indent)
    {
        var mapping = new YamlMapping(_index + 1);
        while (MoveToSignificant())
        {
            var lineIndent = Indent(_index);
            if (lineIndent < indent)
            {
                break;
            }

            if (lineIndent > indent)
            {
                throw Error(_index, "Unexpected indentation.");
            }

            var content = Content(_index);
            if (IsSequenceItem(content))
            {
                if (mapping.Entries.Count == 0)
                {
                    throw Error(_index, "A sequence item cannot appear where a mapping key is expected.");
                }

                break;
            }

            var lineNumber = _index + 1;
            if (!TrySplitKey(content, lineNumber, out var key, out var rest))
            {
                throw Error(_index, "Expected a 'key: value' pair.");
            }

            if (mapping.TryGetValue(key, out _))
            {
                throw Error(_index, $"Duplicate key '{key}'.");
            }

            var value = ParseValue(rest, indent, lineNumber, allowIndentlessSequence: true);
            mapping.Entries.Add(new KeyValuePair<string, YamlNode>(key, value));
        }

        return mapping;
    }

    private YamlSequence ParseSequence(int indent)
    {
        var sequence = new YamlSequence(_index + 1);
        while (MoveToSignificant())
        {
            var lineIndent = Indent(_index);
            if (lineIndent < indent)
            {
                break;
            }

            if (lineIndent > indent)
            {
                throw Error(_index, "Unexpected indentation.");
            }

            var line = _lines[_index];
            if (!IsSequenceItem(Content(_index)))
            {
                break;
            }

            var lineNumber = _index + 1;
            var itemColumn = lineIndent + 1;
            while (itemColumn < line.Length && line[itemColumn] == ' ')
            {
                itemColumn++;
            }

            var itemContent = line[itemColumn..];
            if (itemContent.Length > 0 && itemContent[0] != '#' &&
                (IsSequenceItem(itemContent) || TrySplitKey(itemContent, lineNumber, out _, out _)))
            {
                // "- key: value" or "- - item": re-anchor this line at the item column and parse it as a nested block.
                _indent[_index] = itemColumn;
                sequence.Items.Add(ParseBlock(itemColumn));
                continue;
            }

            sequence.Items.Add(ParseValue(itemContent, indent, lineNumber, allowIndentlessSequence: false));
        }

        return sequence;
    }

    /// <summary>
    /// Parses the value that follows <c>key:</c> or <c>- </c> on the current line, advancing past any
    /// nested block it owns.
    /// </summary>
    private YamlNode ParseValue(string rest, int ownerIndent, int lineNumber, bool allowIndentlessSequence)
    {
        _index++;
        var cursor = SkipSpaces(rest, 0);
        if (cursor >= rest.Length || rest[cursor] == '#')
        {
            if (!MoveToSignificant())
            {
                return new YamlScalar(lineNumber, string.Empty, isNull: true);
            }

            var nextIndent = Indent(_index);
            if (nextIndent > ownerIndent)
            {
                return ParseBlock(nextIndent);
            }

            if (allowIndentlessSequence && nextIndent == ownerIndent && IsSequenceItem(Content(_index)))
            {
                return ParseSequence(nextIndent);
            }

            return new YamlScalar(lineNumber, string.Empty, isNull: true);
        }

        var c = rest[cursor];
        if (c is '|' or '>')
        {
            return ParseBlockScalar(rest, cursor, ownerIndent, lineNumber);
        }

        YamlNode node;
        int end;
        if (c is '[' or '{')
        {
            end = cursor;
            node = ParseFlow(rest, ref end, lineNumber);
        }
        else
        {
            end = cursor;
            node = ParseInlineScalar(rest, ref end, lineNumber, flowContext: false);
        }

        EnsureLineEnd(rest, end, lineNumber);
        return node;
    }

    private YamlScalar ParseBlockScalar(string rest, int cursor, int ownerIndent, int lineNumber)
    {
        var folded = rest[cursor] == '>';
        var chomping = ' ';
        var position = cursor + 1;
        if (position < rest.Length && rest[position] is '-' or '+')
        {
            chomping = rest[position];
            position++;
        }

        EnsureLineEnd(rest, position, lineNumber);

        var collected = ImmutableArray.CreateBuilder<string>();
        var blockIndent = -1;
        while (_index < _lines.Length)
        {
            var line = _lines[_index];
            var lineIndent = CountLeadingSpaces(line);
            var isBlank = lineIndent == line.Length;
            if (!isBlank)
            {
                if (lineIndent <= ownerIndent)
                {
                    break;
                }

                if (blockIndent < 0)
                {
                    blockIndent = lineIndent;
                }
                else if (lineIndent < blockIndent)
                {
                    throw Error(_index, "Block scalar line is less indented than the first line.");
                }
            }

            collected.Add(isBlank ? string.Empty : line[blockIndent..]);
            _index++;
        }

        var trailingBlanks = 0;
        for (var i = collected.Count - 1; i >= 0 && collected[i].Length == 0; i--)
        {
            trailingBlanks++;
        }

        var builder = new StringBuilder();
        var contentCount = collected.Count - trailingBlanks;
        for (var i = 0; i < contentCount; i++)
        {
            if (i > 0)
            {
                var separateWithSpace = folded && collected[i].Length > 0 && collected[i - 1].Length > 0 &&
                                        collected[i][0] != ' ' && collected[i - 1][0] != ' ';
                builder.Append(separateWithSpace ? ' ' : '\n');
            }

            builder.Append(collected[i]);
        }

        if (contentCount > 0 && chomping != '-')
        {
            builder.Append('\n');
            if (chomping == '+')
            {
                builder.Append('\n', trailingBlanks);
            }
        }

        return new YamlScalar(lineNumber, builder.ToString());
    }

    private YamlNode ParseFlow(string text, ref int position, int lineNumber)
    {
        var open = text[position];
        var close = open == '[' ? ']' : '}';
        position++;

        YamlSequence? sequence = open == '[' ? new YamlSequence(lineNumber) : null;
        YamlMapping? mapping = open == '{' ? new YamlMapping(lineNumber) : null;

        position = SkipSpaces(text, position);
        if (position < text.Length && text[position] == close)
        {
            position++;
            return (YamlNode?)sequence ?? mapping ?? throw Error(lineNumber - 1, "Invalid flow collection.");
        }

        while (true)
        {
            position = SkipSpaces(text, position);
            if (position >= text.Length)
            {
                throw Error(lineNumber - 1, $"Unterminated flow collection; expected '{close}'.");
            }

            if (mapping is not null)
            {
                var key = ParseFlowKey(text, ref position, lineNumber);
                if (mapping.TryGetValue(key, out _))
                {
                    throw Error(lineNumber - 1, $"Duplicate key '{key}'.");
                }

                position = SkipSpaces(text, position);
                YamlNode value;
                if (position < text.Length && text[position] is ',' or '}')
                {
                    value = new YamlScalar(lineNumber, string.Empty, isNull: true);
                }
                else
                {
                    value = ParseFlowItem(text, ref position, lineNumber);
                }

                mapping.Entries.Add(new KeyValuePair<string, YamlNode>(key, value));
            }
            else
            {
                sequence?.Items.Add(ParseFlowItem(text, ref position, lineNumber));
            }

            position = SkipSpaces(text, position);
            if (position >= text.Length)
            {
                throw Error(lineNumber - 1, $"Unterminated flow collection; expected '{close}'.");
            }

            if (text[position] == ',')
            {
                position++;
                continue;
            }

            if (text[position] == close)
            {
                position++;
                return (YamlNode?)sequence ?? mapping ?? throw Error(lineNumber - 1, "Invalid flow collection.");
            }

            throw Error(lineNumber - 1, $"Expected ',' or '{close}' in flow collection.");
        }
    }

    private YamlNode ParseFlowItem(string text, ref int position, int lineNumber)
    {
        position = SkipSpaces(text, position);
        if (position < text.Length && text[position] is '[' or '{')
        {
            return ParseFlow(text, ref position, lineNumber);
        }

        return ParseInlineScalar(text, ref position, lineNumber, flowContext: true);
    }

    private string ParseFlowKey(string text, ref int position, int lineNumber)
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
            throw Error(lineNumber - 1, "Expected ':' after flow mapping key.");
        }

        position++;
        if (key.Length == 0)
        {
            throw Error(lineNumber - 1, "Empty key in flow mapping.");
        }

        return key;
    }

    private YamlScalar ParseInlineScalar(string text, ref int position, int lineNumber, bool flowContext)
    {
        var c = text[position];
        if (c is '"' or '\'')
        {
            return new YamlScalar(lineNumber, ParseQuoted(text, ref position, lineNumber));
        }

        if (c is '&' or '*' or '!' or '%' or '@' or '`')
        {
            throw Error(lineNumber - 1, $"Unsupported YAML construct starting with '{c}' (anchors, aliases, tags and directives are not supported).");
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
                throw Error(lineNumber - 1, "Mapping values are not allowed here; quote the scalar.");
            }

            position++;
        }

        var value = text.AsSpan(start, position - start).TrimEnd().ToString();
        return new YamlScalar(lineNumber, value);
    }

    private string ParseQuoted(string text, ref int position, int lineNumber)
    {
        var quote = text[position];
        position++;
        var builder = new StringBuilder();
        while (position < text.Length)
        {
            var c = text[position];
            if (quote == '\'' && c == '\'')
            {
                if (position + 1 < text.Length && text[position + 1] == '\'')
                {
                    builder.Append('\'');
                    position += 2;
                    continue;
                }

                position++;
                return builder.ToString();
            }

            if (quote == '"' && c == '"')
            {
                position++;
                return builder.ToString();
            }

            if (quote == '"' && c == '\\')
            {
                if (position + 1 >= text.Length)
                {
                    break;
                }

                builder.Append(text[position + 1] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '0' => '\0',
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    ' ' => ' ',
                    var other => throw Error(lineNumber - 1, $"Unsupported escape sequence '\\{other}'.")
                });
                position += 2;
                continue;
            }

            builder.Append(c);
            position++;
        }

        throw Error(lineNumber - 1, "Unterminated quoted scalar (multi-line quoted scalars are not supported).");
    }

    private void EnsureLineEnd(string text, int position, int lineNumber)
    {
        position = SkipSpaces(text, position);
        if (position < text.Length && text[position] != '#')
        {
            throw Error(lineNumber - 1, $"Unexpected content '{text[position..]}'.");
        }
    }

    private bool TrySplitKey(string content, int lineNumber, out string key, out string rest)
    {
        key = string.Empty;
        rest = string.Empty;
        if (content.Length == 0)
        {
            return false;
        }

        int colon;
        if (content[0] is '"' or '\'')
        {
            var position = 0;
            var quotedKey = ParseQuoted(content, ref position, lineNumber);
            position = SkipSpaces(content, position);
            if (position >= content.Length || content[position] != ':' ||
                (position + 1 < content.Length && content[position + 1] != ' '))
            {
                return false;
            }

            key = quotedKey;
            colon = position;
        }
        else
        {
            if (content[0] is '[' or '{' or '#' or '|' or '>')
            {
                return false;
            }

            colon = -1;
            for (var i = 0; i < content.Length; i++)
            {
                if (content[i] == '#' && i > 0 && content[i - 1] == ' ')
                {
                    return false;
                }

                if (content[i] == ':' && (i + 1 == content.Length || content[i + 1] == ' '))
                {
                    colon = i;
                    break;
                }
            }

            if (colon <= 0)
            {
                return false;
            }

            key = content.AsSpan(0, colon).TrimEnd().ToString();
        }

        rest = content[(colon + 1)..];
        return true;
    }

    private static bool IsSequenceItem(string content) =>
        content.Length > 0 && content[0] == '-' && (content.Length == 1 || content[1] == ' ');

    private bool MoveToSignificant()
    {
        while (_index < _lines.Length)
        {
            var line = _lines[_index];
            var position = 0;
            while (position < line.Length && line[position] is ' ' or '\t')
            {
                position++;
            }

            if (position < line.Length && line[position] != '#')
            {
                for (var i = 0; i < position; i++)
                {
                    if (line[i] == '\t')
                    {
                        throw Error(_index, "Tabs are not allowed for indentation.");
                    }
                }

                return true;
            }

            _index++;
        }

        return false;
    }

    private int Indent(int index)
    {
        if (_indent[index] < 0)
        {
            _indent[index] = CountLeadingSpaces(_lines[index]);
        }

        return _indent[index];
    }

    private string Content(int index)
    {
        var line = _lines[index];
        var indent = Indent(index);
        return indent >= line.Length ? string.Empty : line.AsSpan(indent).TrimEnd().ToString();
    }

    private static string[] SplitLines(string text)
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != '\n')
            {
                continue;
            }

            var end = i > start && text[i - 1] == '\r' ? i - 1 : i;
            lines.Add(text[start..end]);
            start = i + 1;
        }

        return lines.ToArray();
    }

    private static int CountLeadingSpaces(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }

    private static int SkipSpaces(string text, int position)
    {
        while (position < text.Length && text[position] == ' ')
        {
            position++;
        }

        return position;
    }

    private static NativeApiReadException Error(int zeroBasedLine, string message) =>
        new(NativeApiReadErrorKind.Syntax, zeroBasedLine + 1, message);
}
