using System.Globalization;
using System.Text;

// Reads the JSON lines of a symbol file by hand, also in the demangle tool and the build tasks, so StringBuilder is the intended tool here.
#pragma warning disable RS0030

namespace Kassyi.GSharp.Naming;

/// <summary>
/// Reads the sidecar symbol file (<c>*.gsym.json</c>) that <see cref="GSharpNameEncoding"/> writes: one JSON object per
/// line, one of them (the writer puts it first; the readers take the first they find) a <c>gsharp_symbols</c> header
/// that carries the hash of the source the file belongs to.
/// </summary>
/// <remarks>
/// When the caller passes the hash its source expects, the header is required and must match; a file without a header
/// is rejected by both readers.
/// </remarks>
internal static class SymbolFileReader
{
    private const string HeaderKey = "gsharp_symbols";

    private delegate void EntryHandler<TState>(TState state, string content, int lineStart, int lineEnd);

    /// <summary>Reads the error site entries and returns siteId -&gt; record mappings.</summary>
    public static IReadOnlyDictionary<uint, GSharpNameEncoding.SymbolErrorSiteRecord> ReadErrorSites(
        string content,
        string? expectedHash)
    {
        var result = new Dictionary<uint, GSharpNameEncoding.SymbolErrorSiteRecord>();
        ReadEntries(content, expectedHash, result, AddErrorSite);
        return result;
    }

    /// <summary>Reads the symbol entries and returns cpp -&gt; cs mappings.</summary>
    public static IReadOnlyDictionary<string, string> ReadSymbolMap(string content, string? expectedHash)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        ReadEntries(content, expectedHash, result, AddSymbol);
        return result;
    }

    private static void AddErrorSite(
        Dictionary<uint, GSharpNameEncoding.SymbolErrorSiteRecord> sites,
        string content,
        int lineStart,
        int lineEnd)
    {
        if (TryExtractJsonKeyValue(content, lineStart, lineEnd, "error_site", out var siteIdStr) &&
            uint.TryParse(siteIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var siteId) &&
            TryExtractJsonKeyValue(content, lineStart, lineEnd, "file", out var file) &&
            TryExtractJsonKeyValue(content, lineStart, lineEnd, "line", out var lineStr) &&
            int.TryParse(lineStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var line) &&
            TryExtractJsonKeyValue(content, lineStart, lineEnd, "column", out var colStr) &&
            int.TryParse(colStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var column) &&
            TryExtractJsonKeyValue(content, lineStart, lineEnd, "member", out var member) &&
            TryExtractJsonKeyValue(content, lineStart, lineEnd, "message", out var message))
        {
            sites[siteId] = new GSharpNameEncoding.SymbolErrorSiteRecord(siteId, file, line, column, member, message);
        }
    }

    private static void AddSymbol(Dictionary<string, string> symbols, string content, int lineStart, int lineEnd)
    {
        if (TryExtractJsonKeyValue(content, lineStart, lineEnd, "cpp", out var cpp) &&
            TryExtractJsonKeyValue(content, lineStart, lineEnd, "cs", out var cs))
        {
            symbols[cpp] = cs;
        }
    }

    /// <summary>
    /// Walks the lines of the file: checks the header against <paramref name="expectedHash"/> and hands every other
    /// line to <paramref name="onEntry"/> with the trimmed bounds of the line.
    /// </summary>
    private static void ReadEntries<TState>(
        string content,
        string? expectedHash,
        TState state,
        EntryHandler<TState> onEntry)
    {
        if (string.IsNullOrEmpty(content))
        {
            if (expectedHash is not null)
            {
                throw new InvalidOperationException(
                    $"Symbol file is empty while companion source expects hash '{expectedHash}'.");
            }

            return;
        }

        var sawHeader = false;
        var pos = 0;
        while (pos < content.Length)
        {
            var eol = content.IndexOf('\n', pos);
            var lineEnd = eol >= 0 ? eol : content.Length;
            var lineStart = pos;
            pos = eol >= 0 ? eol + 1 : content.Length;

            while (lineStart < lineEnd && char.IsWhiteSpace(content[lineStart]))
            {
                lineStart++;
            }

            while (lineEnd > lineStart && char.IsWhiteSpace(content[lineEnd - 1]))
            {
                lineEnd--;
            }

            if (lineEnd <= lineStart)
            {
                continue;
            }

            if (content[lineStart] != '{' || content[lineEnd - 1] != '}')
            {
                throw new InvalidOperationException("Symbol file contains a malformed JSON line.");
            }

            if (!sawHeader && TryExtractJsonKeyValue(content, lineStart, lineEnd, HeaderKey, out var headerHash))
            {
                sawHeader = true;
                if (expectedHash is not null && !string.Equals(headerHash, expectedHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Symbol file hash '{headerHash}' does not match source hash '{expectedHash}'.");
                }

                continue;
            }

            onEntry(state, content, lineStart, lineEnd);
        }

        if (expectedHash is not null && !sawHeader)
        {
            throw new InvalidOperationException(
                $"Symbol file is missing a matching '{HeaderKey}' hash header (expected '{expectedHash}').");
        }
    }

    private static bool TryExtractJsonKeyValue(string jsonText, int lineStart, int lineEnd, string key, out string value)
    {
        value = string.Empty;
        var keyPattern = "\"" + key + "\":\"";
        var keyIndex = jsonText.IndexOf(keyPattern, lineStart, lineEnd - lineStart, StringComparison.Ordinal);
        if (keyIndex < 0)
        {
            return false;
        }

        var valStart = keyIndex + keyPattern.Length;
        var sb = new StringBuilder();
        var escaping = false;

        for (var i = valStart; i < lineEnd; i++)
        {
            var c = jsonText[i];
            if (escaping)
            {
                if (c == 'n')
                {
                    sb.Append('\n');
                }
                else if (c == 'r')
                {
                    sb.Append('\r');
                }
                else if (c == 't')
                {
                    sb.Append('\t');
                }
                else
                {
                    sb.Append(c);
                }

                escaping = false;
            }
            else if (c == '\\')
            {
                escaping = true;
            }
            else if (c == '"')
            {
                value = sb.ToString();
                return true;
            }
            else
            {
                sb.Append(c);
            }
        }

        return false;
    }
}
