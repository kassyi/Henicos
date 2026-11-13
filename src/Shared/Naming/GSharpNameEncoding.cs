using System.Globalization;
using System.Text;
using Kassyi.GSharp.Hashing;
#if GSHARP_COMPILER
using Kassyi.GSharp.Compiler;
#endif

// Encodes and decodes names as strings, also in the demangle tool that has no SourceWriter, so StringBuilder is the intended tool here.
#pragma warning disable RS0030

namespace Kassyi.GSharp.Naming;

/// <summary>
/// Spelling of renamed user identifiers in generated C++. Shared by the compiler, which emits the names, and the build
/// tasks, which translate nvcc / host compiler output back to C# names.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description><c>__gsharp_id_{ordinal}_{name}</c>: parameters, locals, labels and local functions. The ordinal is omitted when the declaration could not be located.</description></item>
///   <item><description><c>__gsharp_il_{region}_{ordinal}_{name}</c>: parameters, locals, labels and local functions of a declaration that a pre-lowering or inline expansion copied into the method; the first number is the copy (region).</description></item>
///   <item><description><c>__gsharp_mb_{name}</c>: instance fields and properties of structs that G# emits.</description></item>
///   <item><description><c>__gsharp_bf_{name}</c>: the backing field of a property of such a struct whose accessors use
/// the <c>field</c> keyword; it decodes to <c>{name}.field</c>.</description></item>
///   <item><description><c>__gsharp_ty_</c> / <c>__gsharp_fn_</c> / <c>__gsharp_k_</c> followed by the encoded C# path (<c>Encode</c>): types, device functions and kernel entry points, which share one global C++ scope.</description></item>
///   <item><description><c>__gsharp_tp_{name}</c>: template parameters.</description></item>
///   <item><description><c>__gsharp_gi_</c> followed by one encoded segment: the C# name of the closed copy of a
/// generic kernel or device function; it decodes to the name with its type arguments.</description></item>
///   <item><description><c>GSharpModuleContext_{module}</c>: the generated C# class that loads the fatbin of an
/// assembly, with the module name escaped as a segment is (an empty name included); it decodes to
/// <c>GSharpModuleContext(module)</c>. <c>__gsharp_module_{module}</c>, the field that holds the fatbin of a module
/// a launcher loads by name, escapes the module name the same way and decodes to
/// <c>__gsharp_module(module)</c>.</description></item>
/// </list>
/// GSH0055 rejects <c>__</c> in user identifiers, so a C# name never starts with a digit-and-underscore ordinal and
/// decoding is unambiguous.
/// </remarks>
internal static class GSharpNameEncoding
{
    public const string IdentifierPrefix = "__gsharp_id_";
    public const string InlinedIdentifierPrefix = "__gsharp_il_";
    public const string MemberPrefix = "__gsharp_mb_";
    public const string BackingFieldPrefix = "__gsharp_bf_";
    public const string TypePrefix = "__gsharp_ty_";
    public const string FunctionPrefix = "__gsharp_fn_";
    public const string KernelPrefix = "__gsharp_k_";
    public const string TypeParameterPrefix = "__gsharp_tp_";

    /// <summary>
    /// The prefix of the C# name of the closed copy of a generic kernel or device function, followed by one encoded
    /// segment: the name with every type argument and the overload ordinal.
    /// </summary>
    public const string InstancePrefix = "__gsharp_gi_";

    /// <summary>The prefix of the generated C# class that loads the fatbin module of one assembly.</summary>
    public const string ModuleContextPrefix = "GSharpModuleContext_";

    /// <summary>
    /// The prefix of the generated C# field that holds the fatbin of a module a launcher loads by name.
    /// </summary>
    public const string ModuleFieldPrefix = "__gsharp_module_";

    // Every renamed identifier starts with one of these (the module field prefix starts with the first)
    private const string InternalPrefix = "__gsharp_";

    public const string SymbolHeaderPrefix = "// gsharp-symbols: ";

    /// <summary>
    /// Computes the 16-character lowercase hex 64-bit FNV-1a hash of source after normalizing \r\n to \n
    /// (and stripping a leading // gsharp-symbols: &lt;hash&gt;\n line if present).
    /// </summary>
    public static string ComputeSourceHash(string source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var startIndex = 0;
        if (source.StartsWith(SymbolHeaderPrefix, StringComparison.Ordinal))
        {
            var eolIndex = source.IndexOf('\n');
            startIndex = eolIndex >= 0 ? eolIndex + 1 : source.Length;
        }

        var hash = new Fnv1A64();
        for (var i = startIndex; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '\r' && i + 1 < source.Length && source[i + 1] == '\n')
            {
                continue;
            }

            hash.Append(c);
        }

        return hash.Value.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Checks if the first line of sourceText starts with SymbolHeaderPrefix and extracts the trimmed hash.
    /// </summary>
    public static bool TryReadSymbolHeaderHash(string sourceText, out string hash)
    {
        hash = string.Empty;
        if (string.IsNullOrEmpty(sourceText) || !sourceText.StartsWith(SymbolHeaderPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var eolIndex = sourceText.IndexOf('\n');
        var end = eolIndex >= 0 ? eolIndex : sourceText.Length;
        var start = SymbolHeaderPrefix.Length;

        while (start < end && char.IsWhiteSpace(sourceText[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(sourceText[end - 1]))
        {
            end--;
        }

        if (end <= start)
        {
            return false;
        }

        hash = new StringBuilder(end - start).Append(sourceText, start, end - start).ToString();
        return true;
    }

    /// <summary>Formats the symbol header line.</summary>
    public static string FormatSymbolHeaderLine(string hash) => $"{SymbolHeaderPrefix}{hash}";

    /// <summary>
    /// Builds the symbol file content for sourceBodyWithoutHeader.
    /// </summary>
    /// <summary>Represents an error site entry stored in a <c>.gsym.json</c> symbol file.</summary>
    public readonly record struct SymbolErrorSiteRecord(
        uint SiteId,
        string File,
        int Line,
        int Column,
        string Member,
        string Message);

    public static string BuildSymbolFileContent(
        string sourceBodyWithoutHeader,
        string dialect,
        Func<string, string?>? resolveShortened = null,
        IReadOnlyList<SymbolErrorSiteRecord>? errorSites = null)
    {
        var hash = ComputeSourceHash(sourceBodyWithoutHeader);
        var builder = new StringBuilder();
        builder.Append("{\"gsharp_symbols\":\"").Append(hash).Append("\",\"dialect\":\"").Append(dialect).Append("\"}\n");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;

        while (index < sourceBodyWithoutHeader.Length)
        {
            var start = sourceBodyWithoutHeader.IndexOf("__gsharp_", index, StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }

            var end = start;
            while (end < sourceBodyWithoutHeader.Length && IsIdentifierChar(sourceBodyWithoutHeader[end]))
            {
                end++;
            }

            index = end;
            var startsIdentifier = start == 0 || !IsIdentifierChar(sourceBodyWithoutHeader[start - 1]);
            if (!startsIdentifier)
            {
                continue;
            }

            var identifier = new StringBuilder(end - start).Append(sourceBodyWithoutHeader, start, end - start).ToString();
            if (!seen.Add(identifier))
            {
                continue;
            }

            string? csName = null;
            var full = resolveShortened?.Invoke(identifier);
            if (full is not null)
            {
                TryDecode(full, out csName);
            }

            if (csName is null || csName.Length == 0)
            {
                TryDecode(identifier, out csName);
            }

            if (csName is null || csName.Length == 0)
            {
                continue;
            }

            var kind = identifier.StartsWith(IdentifierPrefix, StringComparison.Ordinal) ||
                       identifier.StartsWith(InlinedIdentifierPrefix, StringComparison.Ordinal) ? "id"
                : identifier.StartsWith(MemberPrefix, StringComparison.Ordinal) ||
                  identifier.StartsWith(BackingFieldPrefix, StringComparison.Ordinal) ? "member"
                : identifier.StartsWith(TypePrefix, StringComparison.Ordinal) ? "type"
                : identifier.StartsWith(FunctionPrefix, StringComparison.Ordinal) ? "function"
                : identifier.StartsWith(KernelPrefix, StringComparison.Ordinal) ? "kernel"
                : identifier.StartsWith(TypeParameterPrefix, StringComparison.Ordinal) ? "typeparam"
                : "id";

            builder.Append("{\"cpp\":\"")
                .Append(EscapeJsonString(identifier))
                .Append("\",\"kind\":\"")
                .Append(kind)
                .Append("\",\"cs\":\"")
                .Append(EscapeJsonString(csName))
                .Append("\",\"symbols\":\"")
                .Append(hash)
                .Append("\",\"dialect\":\"")
                .Append(dialect)
                .Append("\"}\n");
        }

        if (errorSites is not null)
        {
            WriteErrorSites(builder, errorSites, hash, dialect);
        }

        return builder.ToString();
    }

    private static void WriteErrorSites(
        StringBuilder builder,
        IReadOnlyList<SymbolErrorSiteRecord> errorSites,
        string hash,
        string dialect)
    {
        for (var i = 0; i < errorSites.Count; i++)
        {
            var site = errorSites[i];
            builder.Append("{\"error_site\":\"")
                .Append(site.SiteId.ToString(CultureInfo.InvariantCulture))
                .Append("\",\"file\":\"")
                .Append(EscapeJsonString(site.File))
                .Append("\",\"line\":\"")
                .Append(site.Line.ToString(CultureInfo.InvariantCulture))
                .Append("\",\"column\":\"")
                .Append(site.Column.ToString(CultureInfo.InvariantCulture))
                .Append("\",\"member\":\"")
                .Append(EscapeJsonString(site.Member))
                .Append("\",\"message\":\"")
                .Append(EscapeJsonString(site.Message))
                .Append("\",\"symbols\":\"")
                .Append(hash)
                .Append("\",\"dialect\":\"")
                .Append(dialect)
                .Append("\"}\n");
        }
    }

    /// <summary>
    /// Reads error site entries from <paramref name="gsymContent"/> and returns siteId -&gt; <see cref="SymbolErrorSiteRecord"/> mappings.
    /// </summary>
    public static IReadOnlyDictionary<uint, SymbolErrorSiteRecord> ParseErrorSitesFromSymbolFile(string gsymContent, string? expectedHash = null)
    {
        var result = new Dictionary<uint, SymbolErrorSiteRecord>();
        if (string.IsNullOrEmpty(gsymContent))
        {
            return result;
        }

        string? headerHash = null;
        var pos = 0;
        while (pos < gsymContent.Length)
        {
            var eol = gsymContent.IndexOf("\n", pos, StringComparison.Ordinal);
            var lineEnd = eol >= 0 ? eol : gsymContent.Length;
            var lineStart = pos;
            pos = eol >= 0 ? eol + 1 : gsymContent.Length;

            while (lineStart < lineEnd && char.IsWhiteSpace(gsymContent[lineStart]))
            {
                lineStart++;
            }

            while (lineEnd > lineStart && char.IsWhiteSpace(gsymContent[lineEnd - 1]))
            {
                lineEnd--;
            }

            if (lineEnd <= lineStart)
            {
                continue;
            }

            if (headerHash is null && TryExtractJsonKeyValue(gsymContent, lineStart, lineEnd, "gsharp_symbols", out var fileHash))
            {
                headerHash = fileHash;
                if (expectedHash is not null && !string.Equals(headerHash, expectedHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Symbol file hash ({headerHash}) does not match source hash ({expectedHash}).");
                }

                continue;
            }

            if (TryExtractJsonKeyValue(gsymContent, lineStart, lineEnd, "error_site", out var siteIdStr) &&
                uint.TryParse(siteIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var siteId) &&
                TryExtractJsonKeyValue(gsymContent, lineStart, lineEnd, "file", out var file) &&
                TryExtractJsonKeyValue(gsymContent, lineStart, lineEnd, "line", out var lineStr) &&
                int.TryParse(lineStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var line) &&
                TryExtractJsonKeyValue(gsymContent, lineStart, lineEnd, "column", out var colStr) &&
                int.TryParse(colStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var column) &&
                TryExtractJsonKeyValue(gsymContent, lineStart, lineEnd, "member", out var member) &&
                TryExtractJsonKeyValue(gsymContent, lineStart, lineEnd, "message", out var message))
            {
                result[siteId] = new SymbolErrorSiteRecord(siteId, file, line, column, member, message);
            }
        }

        return result;
    }

    /// <summary>
    /// Reads lines from gsymContent and returns cpp -> cs mappings.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseSymbolFile(string gsymContent, string? expectedHash = null)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(gsymContent))
        {
            return result;
        }

        string? headerHash = null;
        var pos = 0;
        while (pos < gsymContent.Length)
        {
            var eol = gsymContent.IndexOf('\n', pos);
            var lineEnd = eol >= 0 ? eol : gsymContent.Length;
            var lineStart = pos;
            pos = eol >= 0 ? eol + 1 : gsymContent.Length;

            while (lineStart < lineEnd && char.IsWhiteSpace(gsymContent[lineStart]))
            {
                lineStart++;
            }

            while (lineEnd > lineStart && char.IsWhiteSpace(gsymContent[lineEnd - 1]))
            {
                lineEnd--;
            }

            if (lineEnd <= lineStart)
            {
                continue;
            }

            if (headerHash is null && TryExtractJsonKeyValue(gsymContent, lineStart, lineEnd, "gsharp_symbols", out var fileHash))
            {
                headerHash = fileHash;
                if (expectedHash is not null && !string.Equals(headerHash, expectedHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Symbol file hash '{headerHash}' does not match source hash '{expectedHash}'.");
                }

                continue;
            }

            if (expectedHash is not null && headerHash is null && TryExtractJsonKeyValue(gsymContent, lineStart, lineEnd, "symbols", out var itemHash))
            {
                if (!string.Equals(itemHash, expectedHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Symbol file hash '{itemHash}' does not match source hash '{expectedHash}'.");
                }
            }

            if (TryExtractJsonKeyValue(gsymContent, lineStart, lineEnd, "cpp", out var cpp) &&
                TryExtractJsonKeyValue(gsymContent, lineStart, lineEnd, "cs", out var cs))
            {
                result[cpp] = cs;
            }
        }

        return result;
    }

    /// <summary>Replaces every renamed identifier in <paramref name="text"/> using symbolMap or default rules.</summary>
    public static string DecodeText(string text, IReadOnlyDictionary<string, string>? symbolMap)
    {
        if (symbolMap is null)
        {
            return DecodeText(text);
        }

        var builder = new StringBuilder(text.Length);
        var copied = 0;
        var index = 0;
        var finder = new RenamedIdentifierFinder(text);
        while (index < text.Length)
        {
            var start = finder.Next(index);
            if (start < 0)
            {
                break;
            }

            var end = start;
            while (end < text.Length && IsIdentifierChar(text[end]))
            {
                end++;
            }

            index = end;
            var startsIdentifier = start == 0 || !IsIdentifierChar(text[start - 1]);
            if (!startsIdentifier)
            {
                continue;
            }

            var identifier = new StringBuilder(end - start).Append(text, start, end - start).ToString();
            if (!symbolMap.TryGetValue(identifier, out var mapped) || mapped is null || mapped.Length == 0)
            {
                TryDecode(identifier, out mapped);
            }

            if (mapped is not null && mapped.Length > 0)
            {
                builder.Append(text, copied, start - copied).Append(mapped);
                copied = end;
            }
        }

        return builder.Append(text, copied, text.Length - copied).ToString();
    }

    private static string EscapeJsonString(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c == '"')
            {
                sb.Append("\\\"");
            }
            else if (c == '\\')
            {
                sb.Append("\\\\");
            }
            else if (c == '\n')
            {
                sb.Append("\\n");
            }
            else if (c == '\r')
            {
                sb.Append("\\r");
            }
            else if (c == '\t')
            {
                sb.Append("\\t");
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
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

#if GSHARP_COMPILER
    /// <summary>
    /// Encodes a C# path as the body of a global C++ name. The encoding is injective: <see cref="TryDecodePath(string, out GlobalNamePath?)"/> recovers
    /// the path from the result.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description>Segments are joined with <c>__</c>. GSH0055 guarantees that no segment contains <c>__</c>.</description></item>
    ///   <item><description>Inside a segment, <c>_</c> becomes <c>_u</c> and a character other than an ASCII letter or digit becomes <c>_x</c> followed by its code point as six lowercase hex digits.</description></item>
    ///   <item><description>After the name come, in this order: type arguments <c>_g{count}</c>, each argument as <c>_b{path}</c>, then <c>_e</c>; the definition arity <c>_a{n}</c>; the file identity of a <c>file</c> type <c>_f{hex}</c>; the overload ordinal <c>_o{n}</c>.</description></item>
    ///   <item><description>A path may end with element-type modifiers: <c>_p</c> for a pointer and <c>_r{rank}</c> for an array.</description></item>
    ///   <item><description><c>_h</c> is reserved for names that the compiler shortens with a hash; those cannot be decoded.</description></item>
    /// </list>
    /// After a <c>_</c>, the next character always tells a separator, an escape and each marker apart, which is what makes the
    /// encoding decodable. Only the compiler encodes; the build tasks decode, so the encoder is compiled into the compiler only.
    /// </remarks>
    public static string Encode(GlobalNamePath path)
    {
        using var writer = new SourceWriter();
        AppendEncoded(writer, path);
        return writer.ToString();
    }

    /// <summary>Appends <see cref="Encode(GlobalNamePath)"/> of <paramref name="path"/> to <paramref name="writer"/>.</summary>
    public static void AppendEncoded(in SourceWriter writer, GlobalNamePath path)
    {
        for (var i = 0; i < path.Segments.Count; i++)
        {
            if (i > 0)
            {
                writer.Append("__");
            }

            AppendSegment(writer, path.Segments[i]);
        }

        foreach (var modifier in path.Modifiers)
        {
            if (modifier == 0)
            {
                writer.Append("_p");
            }
            else
            {
                writer.Append("_r").Append(modifier);
            }
        }
    }

    /// <summary>
    /// Returns <paramref name="name"/> with <c>_</c> and non-ASCII characters escaped, as described in
    /// <see cref="Encode(GlobalNamePath)"/>; a name of ASCII letters and digits only is returned as is.
    /// </summary>
    public static string EscapeName(string name)
    {
        foreach (var c in name)
        {
            if (!IsAsciiLetterOrDigit(c))
            {
                using var writer = new SourceWriter();
                AppendEscapedName(writer, name);
                return writer.ToString();
            }
        }

        return name;
    }

    /// <summary>
    /// The name of the module context class of a module: <c>GSharpModuleContext_</c> and the module name escaped as a
    /// segment of <see cref="Encode(GlobalNamePath)"/> (<c>Henicos.Benchmarks</c> gives
    /// <c>GSharpModuleContext_Henicos_x00002eBenchmarks</c>), so that any name gives an identifier, two names give two,
    /// and <see cref="TryDecode"/> recovers the name.
    /// </summary>
    public static string GetModuleContextName(string moduleName) => ModuleContextPrefix + EscapeName(moduleName);

    /// <summary>
    /// The name of the field that holds the fatbin of an external module (<c>[CudaImport]</c>, YAML
    /// <c>moduleName</c>): <c>__gsharp_module_</c> and the module name escaped as in
    /// <see cref="GetModuleContextName"/>, which <see cref="TryDecode"/> recovers.
    /// </summary>
    public static string GetModuleFieldName(string moduleName) => ModuleFieldPrefix + EscapeName(moduleName);

    /// <summary>Appends <paramref name="name"/> escaped as described in <see cref="Encode(GlobalNamePath)"/>.</summary>
    public static void AppendEscapedName(in SourceWriter writer, string name)
    {
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (IsAsciiLetterOrDigit(c))
            {
                writer.Append(c);
            }
            else if (c == '_')
            {
                writer.Append("_u");
            }
            else
            {
                // A surrogate pair is one code point; a lone surrogate is escaped as its UTF-16 code unit
                int codePoint = c;
                if (char.IsHighSurrogate(c) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(c, name[i + 1]);
                    i++;
                }

                writer.Append("_x");
                for (var shift = 20; shift >= 0; shift -= 4)
                {
                    writer.Append("0123456789abcdef"[(codePoint >> shift) & 0xF]);
                }
            }
        }
    }

    private static void AppendSegment(in SourceWriter writer, GlobalNameSegment segment)
    {
        AppendEscapedName(writer, segment.Name);
        if (segment.TypeArguments.Count > 0)
        {
            writer.Append("_g").Append(segment.TypeArguments.Count);
            foreach (var argument in segment.TypeArguments)
            {
                writer.Append("_b");
                AppendEncoded(writer, argument);
            }

            writer.Append("_e");
        }

        if (segment.Arity > 0)
        {
            writer.Append("_a").Append(segment.Arity);
        }

        if (segment.FileId is { } fileId)
        {
            writer.Append("_f").Append(fileId);
        }

        if (segment.OverloadOrdinal >= 0)
        {
            writer.Append("_o").Append(segment.OverloadOrdinal);
        }
    }
#endif

    /// <summary>Decodes a whole string produced by <c>Encode</c>.</summary>
    /// <returns><see langword="false"/> if <paramref name="encoded"/> is not exactly one encoded path.</returns>
    public static bool TryDecodePath(string encoded, out GlobalNamePath? path) => TryDecodePath(encoded, 0, out path);

    /// <summary>Recovers the C# name from a renamed identifier.</summary>
    /// <param name="identifier">A complete C++ identifier.</param>
    /// <param name="csharpName">
    /// The C# name without the verbatim <c>@</c>, or a C#-style display of the path for a global name, or an empty
    /// string when decoding fails.
    /// </param>
    /// <returns><see langword="true"/> if <paramref name="identifier"/> is a renamed user identifier.</returns>
    public static bool TryDecode(string identifier, out string csharpName)
    {
        csharpName = string.Empty;
        if (identifier.StartsWith(MemberPrefix, StringComparison.Ordinal))
        {
            return TrySetName(identifier, MemberPrefix.Length, ref csharpName);
        }

        if (identifier.StartsWith(BackingFieldPrefix, StringComparison.Ordinal))
        {
            if (!TrySetName(identifier, BackingFieldPrefix.Length, ref csharpName))
            {
                return false;
            }

            csharpName += ".field";
            return true;
        }

        if (TryDecodeModuleIdentifier(identifier, ModuleContextPrefix, "GSharpModuleContext", ref csharpName) ||
            TryDecodeModuleIdentifier(identifier, ModuleFieldPrefix, "__gsharp_module", ref csharpName))
        {
            return true;
        }

        if (TryDecodeGlobal(identifier, out var display))
        {
            csharpName = display;
            return true;
        }

        if (TryDecodeInstance(identifier, out var instanceDisplay))
        {
            csharpName = instanceDisplay;
            return true;
        }

        if (identifier.StartsWith(TypeParameterPrefix, StringComparison.Ordinal))
        {
            var index = TypeParameterPrefix.Length;
            var name = new StringBuilder();
            if (TryParseName(identifier, ref index, name) && index == identifier.Length)
            {
                csharpName = name.ToString();
                return true;
            }

            return false;
        }

        if (identifier.StartsWith(InlinedIdentifierPrefix, StringComparison.Ordinal))
        {
            var ilStart = InlinedIdentifierPrefix.Length;
            var ilDigitsEnd = ilStart;
            while (ilDigitsEnd < identifier.Length && identifier[ilDigitsEnd] is >= '0' and <= '9')
            {
                ilDigitsEnd++;
            }

            if (ilDigitsEnd == ilStart || ilDigitsEnd >= identifier.Length || identifier[ilDigitsEnd] != '_')
            {
                return false;
            }

            var ordStart = ilDigitsEnd + 1;
            var ordDigitsEnd = ordStart;
            while (ordDigitsEnd < identifier.Length && identifier[ordDigitsEnd] is >= '0' and <= '9')
            {
                ordDigitsEnd++;
            }

            if (ordDigitsEnd > ordStart && ordDigitsEnd < identifier.Length && identifier[ordDigitsEnd] == '_')
            {
                ordStart = ordDigitsEnd + 1;
            }

            return TrySetName(identifier, ordStart, ref csharpName);
        }

        if (!identifier.StartsWith(IdentifierPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var start = IdentifierPrefix.Length;
        var digitsEnd = start;
        while (digitsEnd < identifier.Length && identifier[digitsEnd] is >= '0' and <= '9')
        {
            digitsEnd++;
        }

        if (digitsEnd > start && digitsEnd < identifier.Length && identifier[digitsEnd] == '_')
        {
            start = digitsEnd + 1;
        }

        return TrySetName(identifier, start, ref csharpName);
    }

    /// <summary>
    /// Lists the renamed identifiers that occur in <paramref name="text"/> together with their C# names, for example
    /// <c>__gsharp_id_0_data = data, __gsharp_mb_register = register</c>.
    /// </summary>
    /// <returns>The list, or an empty string when the text contains no renamed identifier.</returns>
    public static string DescribeRenamedIdentifiers(string text)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var builder = new StringBuilder();
        var index = 0;
        var finder = new RenamedIdentifierFinder(text);
        while (TryFindRenamedIdentifier(text, ref finder, ref index, out _, out var identifier, out var csharpName))
        {
            if (!seen.Add(identifier))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(identifier).Append(" = ").Append(csharpName);
        }

        return builder.ToString();
    }

    /// <summary>Replaces every renamed identifier in <paramref name="text"/> with its C# name.</summary>
    public static string DecodeText(string text)
    {
        var builder = new StringBuilder(text.Length);
        var copied = 0;
        var index = 0;
        var finder = new RenamedIdentifierFinder(text);
        while (TryFindRenamedIdentifier(text, ref finder, ref index, out var start, out var identifier,
                   out var csharpName))
        {
            builder.Append(text, copied, start - copied).Append(csharpName);
            copied = start + identifier.Length;
        }

        return builder.Append(text, copied, text.Length - copied).ToString();
    }

    private static bool TryFindRenamedIdentifier(string text, ref RenamedIdentifierFinder finder, ref int index,
        out int start, out string identifier, out string csharpName)
    {
        while (index < text.Length)
        {
            start = finder.Next(index);
            if (start < 0)
            {
                break;
            }

            var end = start;
            while (end < text.Length && IsIdentifierChar(text[end]))
            {
                end++;
            }

            index = end;
            identifier = new System.Text.StringBuilder(end - start).Append(text, start, end - start).ToString();
            var startsIdentifier = start == 0 || !IsIdentifierChar(text[start - 1]);
            if (startsIdentifier && TryDecode(identifier, out csharpName))
            {
                return true;
            }
        }

        start = -1;
        identifier = string.Empty;
        csharpName = string.Empty;
        return false;
    }

    // The name escaped in <s> from <start> to its end; an empty rest is the empty name
    private static bool TryParseWholeName(string s, int start, out string name)
    {
        name = string.Empty;
        if (start == s.Length)
        {
            return true;
        }

        var index = start;
        var builder = new StringBuilder(s.Length - start);
        if (!TryParseName(s, ref index, builder) || index != s.Length)
        {
            return false;
        }

        name = builder.ToString();
        return true;
    }

    // <display>(<module>) for <prefix><escaped module>
    private static bool TryDecodeModuleIdentifier(string identifier, string prefix, string display,
        ref string csharpName)
    {
        if (!identifier.StartsWith(prefix, StringComparison.Ordinal) ||
            !TryParseWholeName(identifier, prefix.Length, out var moduleName))
        {
            return false;
        }

        csharpName = display + "(" + moduleName + ")";
        return true;
    }

    // The first place at or after an index where a renamed identifier may start: a __gsharp_ name or a module context
    // class. Each prefix is searched again only once the index has passed its last hit, and never again once it is
    // absent, so a scan of a whole log stays linear in its length
    private struct RenamedIdentifierFinder(string text)
    {
        private int _internal = -2;
        private int _context = -2;

        // -1 when no renamed identifier starts at or after <index>
        public int Next(int index)
        {
            _internal = Advance(_internal, InternalPrefix, index);
            _context = Advance(_context, ModuleContextPrefix, index);
            return _internal < 0 ? _context : _context < 0 || _internal < _context ? _internal : _context;
        }

        // -2: not searched yet; -1: absent from the rest of the text
        private readonly int Advance(int cached, string prefix, int index) =>
            cached == -1 || cached >= index ? cached : text.IndexOf(prefix, index, StringComparison.Ordinal);
    }

    private static bool TrySetName(string identifier, int start, ref string csharpName)
    {
        if (start >= identifier.Length)
        {
            return false;
        }

        csharpName = new System.Text.StringBuilder(identifier.Length - start)
            .Append(identifier, start, identifier.Length - start).ToString();
        return true;
    }

    private static bool IsIdentifierChar(char c) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_';

    private static bool IsAsciiLetterOrDigit(char c) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static bool IsLowerHexDigit(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static bool TryDecodeGlobal(string identifier, out string display)
    {
        display = string.Empty;
        var start = identifier.StartsWith(TypePrefix, StringComparison.Ordinal) ? TypePrefix.Length
            : identifier.StartsWith(FunctionPrefix, StringComparison.Ordinal) ? FunctionPrefix.Length
            : identifier.StartsWith(KernelPrefix, StringComparison.Ordinal) ? KernelPrefix.Length
            : -1;
        if (start < 0 || !TryDecodePath(identifier, start, out var path) || path is null)
        {
            return false;
        }

        display = path.ToDisplayString();
        return true;
    }

    // __gsharp_gi_ and exactly one segment
    private static bool TryDecodeInstance(string identifier, out string display)
    {
        display = string.Empty;
        if (!identifier.StartsWith(InstancePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var index = InstancePrefix.Length;
        if (!TryParseSegment(identifier, ref index, out var segment) || segment is null || index != identifier.Length)
        {
            return false;
        }

        var builder = new StringBuilder();
        segment.AppendDisplay(builder);
        display = builder.ToString();
        return true;
    }

    private static bool TryDecodePath(string encoded, int start, out GlobalNamePath? path)
    {
        var index = start;
        if (TryParsePath(encoded, ref index, out path) && index == encoded.Length)
        {
            return true;
        }

        path = null;
        return false;
    }

    private static bool TryParsePath(string s, ref int index, out GlobalNamePath? path)
    {
        path = null;
        var segments = new GlobalNameSegment[1];
        var segmentCount = 0;
        if (!TryParseSegment(s, ref index, out var first) || first is null)
        {
            return false;
        }

        Add(ref segments, ref segmentCount, first);
        while (MatchesAt(s, index, '_') && MatchesAt(s, index + 1, '_'))
        {
            index += 2;
            if (!TryParseSegment(s, ref index, out var next) || next is null)
            {
                return false;
            }

            Add(ref segments, ref segmentCount, next);
        }

        var modifiers = Array.Empty<int>();
        var modifierCount = 0;
        while (true)
        {
            if (MatchesMarkerAt(s, index, 'p'))
            {
                index += 2;
                Add(ref modifiers, ref modifierCount, 0);
            }
            else if (MatchesMarkerAt(s, index, 'r'))
            {
                index += 2;
                if (!TryParseNumber(s, ref index, out var rank) || rank < 1)
                {
                    return false;
                }

                Add(ref modifiers, ref modifierCount, rank);
            }
            else
            {
                break;
            }
        }

        path = new GlobalNamePath(Trim(segments, segmentCount), Trim(modifiers, modifierCount));
        return true;
    }

    private static bool TryParseSegment(string s, ref int index, out GlobalNameSegment? segment)
    {
        segment = null;
        var name = new StringBuilder();
        if (!TryParseName(s, ref index, name))
        {
            return false;
        }

        var arguments = Array.Empty<GlobalNamePath>();
        var argumentCount = 0;
        if (MatchesMarkerAt(s, index, 'g'))
        {
            index += 2;
            if (!TryParseNumber(s, ref index, out var count) || count < 1)
            {
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                if (!MatchesMarkerAt(s, index, 'b'))
                {
                    return false;
                }

                index += 2;
                if (!TryParsePath(s, ref index, out var argument) || argument is null)
                {
                    return false;
                }

                Add(ref arguments, ref argumentCount, argument);
            }

            if (!MatchesMarkerAt(s, index, 'e'))
            {
                return false;
            }

            index += 2;
        }

        var arity = 0;
        if (MatchesMarkerAt(s, index, 'a'))
        {
            index += 2;
            if (!TryParseNumber(s, ref index, out arity) || arity < 1)
            {
                return false;
            }
        }

        string? fileId = null;
        if (MatchesMarkerAt(s, index, 'f'))
        {
            index += 2;
            var hexStart = index;
            while (index < s.Length && IsLowerHexDigit(s[index]))
            {
                index++;
            }

            if (index == hexStart)
            {
                return false;
            }

            fileId = new StringBuilder().Append(s, hexStart, index - hexStart).ToString();
        }

        var ordinal = -1;
        if (MatchesMarkerAt(s, index, 'o'))
        {
            index += 2;
            if (!TryParseNumber(s, ref index, out ordinal))
            {
                return false;
            }
        }

        segment = new GlobalNameSegment(name.ToString(), Trim(arguments, argumentCount), arity, fileId, ordinal);
        return true;
    }

    private static bool TryParseName(string s, ref int index, StringBuilder name)
    {
        while (index < s.Length)
        {
            var c = s[index];
            if (IsAsciiLetterOrDigit(c))
            {
                name.Append(c);
                index++;
            }
            else if (MatchesMarkerAt(s, index, 'u'))
            {
                name.Append('_');
                index += 2;
            }
            else if (MatchesMarkerAt(s, index, 'x'))
            {
                if (!TryParseCodePoint(s, index + 2, out var codePoint) ||
                    (IsLowSurrogate(codePoint) && name.Length > 0 && char.IsHighSurrogate(name[name.Length - 1])))
                {
                    // A high and a low surrogate escaped one by one would be a second spelling of their pair
                    return false;
                }

                if (codePoint > 0xFFFF)
                {
                    name.Append(char.ConvertFromUtf32(codePoint));
                }
                else
                {
                    name.Append((char)codePoint);
                }

                index += 8;
            }
            else
            {
                break;
            }
        }

        return name.Length > 0;
    }

    private static bool TryParseCodePoint(string s, int start, out int codePoint)
    {
        codePoint = 0;
        if (start + 6 > s.Length)
        {
            return false;
        }

        for (var i = start; i < start + 6; i++)
        {
            var c = s[i];
            if (!IsLowerHexDigit(c))
            {
                return false;
            }

            codePoint = codePoint * 16 + (c <= '9' ? c - '0' : c - 'a' + 10);
        }

        // Only characters that the encoder escapes: no ASCII letters or digits; a surrogate half stands for a lone
        // surrogate, which TryParseName keeps from following a high surrogate.
        return codePoint <= 0x10FFFF &&
               (codePoint > 0x7F || !IsAsciiLetterOrDigit((char)codePoint)) && codePoint != '_';
    }

    private static bool IsLowSurrogate(int codePoint) => codePoint is >= 0xDC00 and <= 0xDFFF;

    // Leading zeros are rejected so that every number has exactly one spelling.
    private static bool TryParseNumber(string s, ref int index, out int value)
    {
        value = 0;
        var start = index;
        while (index < s.Length && s[index] is >= '0' and <= '9')
        {
            if (index - start >= 9)
            {
                return false;
            }

            value = value * 10 + (s[index] - '0');
            index++;
        }

        return index > start && (s[start] != '0' || index == start + 1);
    }

    private static bool MatchesAt(string s, int index, char c) => index < s.Length && s[index] == c;

    // [WHY] This file is also compiled into the build tasks, which have no System.Collections.Immutable, and the compiler
    // bans List<T>, so the parser grows plain arrays.
    private static void Add<T>(ref T[] items, ref int count, T item)
    {
        if (count == items.Length)
        {
            Array.Resize(ref items, Math.Max(4, items.Length * 2));
        }

        items[count++] = item;
    }

    private static T[] Trim<T>(T[] items, int count)
    {
        if (count != items.Length)
        {
            Array.Resize(ref items, count);
        }

        return items;
    }

    private static bool MatchesMarkerAt(string s, int index, char marker) =>
        MatchesAt(s, index, '_') && MatchesAt(s, index + 1, marker);
}
