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

/// <summary>
/// Reflection-free, zero-dependency parser for the YAML subset used by G# native API definitions,
/// dialect configurations, and compiler-internal metadata.
/// </summary>
/// <remarks>
/// <para>
/// Why this parser exists instead of a NuGet dependency such as YamlDotNet:
/// 1. Reflection ban: Roslyn source generators and analyzers fundamentally prohibit reflection for compile-time speed.
/// 2. Zero analyzer dependencies: Loading third-party dependencies into the compiler and IDE host processes risks
///    assembly version conflicts and overhead.
/// 3. Precise line-numbered diagnostics: Syntax errors and schema violations report exact 1-based source lines
///    via <see cref="NativeApiReadException"/> and <see cref="YamlNode.Line"/>.
/// </para>
/// <para>
/// Supported: block mappings and sequences (including indentless sequences), single-line flow
/// collections (<c>[a, b]</c>, <c>{ k: v }</c>), plain / single-quoted / double-quoted scalars,
/// literal (<c>|</c>) and folded (<c>&gt;</c>) block scalars, comments and a leading <c>---</c> marker.
/// Anchors, aliases, tags, multi-line flow collections and multiple documents are rejected with a
/// <see cref="NativeApiReadException"/> carrying the offending line.
/// </para>
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
            if (!YamlInlineParser.TrySplitKey(content, lineNumber, out var key, out var rest))
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
                (IsSequenceItem(itemContent) || YamlInlineParser.TrySplitKey(itemContent, lineNumber, out _, out _)))
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
        var cursor = YamlInlineParser.SkipSpaces(rest, 0);
        if (cursor >= rest.Length || rest[cursor] == '#')
        {
            return ParseNestedValue(ownerIndent, lineNumber, allowIndentlessSequence);
        }

        var c = rest[cursor];
        if (c is '|' or '>')
        {
            return ParseBlockScalar(rest, cursor, ownerIndent, lineNumber);
        }

        var end = cursor;
        YamlNode node = c is '[' or '{'
            ? YamlInlineParser.ParseFlow(rest, ref end, lineNumber)
            : YamlInlineParser.ParseInlineScalar(rest, ref end, lineNumber, flowContext: false);

        YamlInlineParser.EnsureLineEnd(rest, end, lineNumber);
        return node;
    }

    // The value of a key or item with nothing after it on its line: the block on the following lines, or null
    private YamlNode ParseNestedValue(int ownerIndent, int lineNumber, bool allowIndentlessSequence)
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

#pragma warning disable RS0030 // Input parsing: decodes a YAML scalar character by character
    private YamlScalar ParseBlockScalar(string rest, int cursor, int ownerIndent, int lineNumber)
    {
        // '|' or '>', then an optional chomping indicator '-' (strip) or '+' (keep)
        var folded = rest[cursor] == '>';
        var chomping = ' ';
        var position = cursor + 1;
        if (position < rest.Length && rest[position] is '-' or '+')
        {
            chomping = rest[position];
            position++;
        }

        YamlInlineParser.EnsureLineEnd(rest, position, lineNumber);
        var lines = CollectBlockScalarLines(ownerIndent);
        return new YamlScalar(lineNumber, AssembleBlockScalar(lines, folded, chomping));
    }

    // The lines of a block scalar without their common indentation; a blank line is an empty string
    private ImmutableArray<string> CollectBlockScalarLines(int ownerIndent)
    {
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

        return collected.ToImmutable();
    }

    private static string AssembleBlockScalar(ImmutableArray<string> lines, bool folded, char chomping)
    {
        var trailingBlanks = 0;
        for (var i = lines.Length - 1; i >= 0 && lines[i].Length == 0; i--)
        {
            trailingBlanks++;
        }

        var builder = new StringBuilder();
        var contentCount = lines.Length - trailingBlanks;
        for (var i = 0; i < contentCount; i++)
        {
            if (i > 0)
            {
                var separateWithSpace = folded && lines[i].Length > 0 && lines[i - 1].Length > 0 &&
                                        lines[i][0] != ' ' && lines[i - 1][0] != ' ';
                builder.Append(separateWithSpace ? ' ' : '\n');
            }

            builder.Append(lines[i]);
        }

        if (contentCount > 0 && chomping != '-')
        {
            builder.Append('\n');
            if (chomping == '+')
            {
                builder.Append('\n', trailingBlanks);
            }
        }

        return builder.ToString();
    }
#pragma warning restore RS0030

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
                EnsureNoIndentationTabs(line, position, _index);
                return true;
            }

            _index++;
        }

        return false;
    }

    private static void EnsureNoIndentationTabs(string line, int position, int lineIndex)
    {
        for (var i = 0; i < position; i++)
        {
            if (line[i] == '\t')
            {
                throw Error(lineIndex, "Tabs are not allowed for indentation.");
            }
        }
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

    public static NativeApiReadException Error(int zeroBasedLine, string message) =>
        new(NativeApiReadErrorKind.Syntax, zeroBasedLine + 1, message);
}
