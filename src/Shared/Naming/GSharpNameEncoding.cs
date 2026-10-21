using System.Globalization;
using System.Text;
#if GSHARP_COMPILER
using Kassyi.GSharp.Compiler;
#endif

namespace Kassyi.GSharp.Naming;

/// <summary>
/// Spelling of renamed user identifiers in generated C++. Shared by the compiler, which emits the names, and the build
/// tasks, which translate nvcc / host compiler output back to C# names.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description><c>__gsharp_id_{ordinal}_{name}</c>: parameters, locals, labels and local functions. The ordinal is omitted when the declaration could not be located.</description></item>
///   <item><description><c>__gsharp_mb_{name}</c>: instance fields and properties of structs that G# emits.</description></item>
///   <item><description><c>__gsharp_ty_</c> / <c>__gsharp_fn_</c> / <c>__gsharp_k_</c> followed by the encoded C# path (<c>Encode</c>): types, device functions and kernel entry points, which share one global C++ scope.</description></item>
///   <item><description><c>__gsharp_tp_{name}</c>: template parameters.</description></item>
/// </list>
/// GSH0057 rejects <c>__</c> in user identifiers, so a C# name never starts with a digit-and-underscore ordinal and
/// decoding is unambiguous.
/// </remarks>
internal static class GSharpNameEncoding
{
    public const string IdentifierPrefix = "__gsharp_id_";
    public const string MemberPrefix = "__gsharp_mb_";
    public const string TypePrefix = "__gsharp_ty_";
    public const string FunctionPrefix = "__gsharp_fn_";
    public const string KernelPrefix = "__gsharp_k_";
    public const string TypeParameterPrefix = "__gsharp_tp_";

#if GSHARP_COMPILER
    /// <summary>
    /// Encodes a C# path as the body of a global C++ name. The encoding is injective: <see cref="TryDecodePath(string, out GlobalNamePath?)"/> recovers
    /// the path from the result.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description>Segments are joined with <c>__</c>. GSH0057 guarantees that no segment contains <c>__</c>.</description></item>
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
                var codePoint = char.ConvertToUtf32(name, i);
                if (char.IsHighSurrogate(c))
                {
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

        if (TryDecodeGlobal(identifier, out var display))
        {
            csharpName = display;
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
        while (TryFindRenamedIdentifier(text, ref index, out _, out var identifier, out var csharpName))
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
        while (TryFindRenamedIdentifier(text, ref index, out var start, out var identifier, out var csharpName))
        {
            builder.Append(text, copied, start - copied).Append(csharpName);
            copied = start + identifier.Length;
        }

        return builder.Append(text, copied, text.Length - copied).ToString();
    }

    private static bool TryFindRenamedIdentifier(string text, ref int index, out int start, out string identifier,
        out string csharpName)
    {
        while (index < text.Length)
        {
            start = text.IndexOf("__gsharp_", index, StringComparison.Ordinal);
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
        while (Peek(s, index, '_') && Peek(s, index + 1, '_'))
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
            if (PeekMarker(s, index, 'p'))
            {
                index += 2;
                Add(ref modifiers, ref modifierCount, 0);
            }
            else if (PeekMarker(s, index, 'r'))
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
        if (PeekMarker(s, index, 'g'))
        {
            index += 2;
            if (!TryParseNumber(s, ref index, out var count) || count < 1)
            {
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                if (!PeekMarker(s, index, 'b'))
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

            if (!PeekMarker(s, index, 'e'))
            {
                return false;
            }

            index += 2;
        }

        var arity = 0;
        if (PeekMarker(s, index, 'a'))
        {
            index += 2;
            if (!TryParseNumber(s, ref index, out arity) || arity < 1)
            {
                return false;
            }
        }

        string? fileId = null;
        if (PeekMarker(s, index, 'f'))
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
        if (PeekMarker(s, index, 'o'))
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
            else if (PeekMarker(s, index, 'u'))
            {
                name.Append('_');
                index += 2;
            }
            else if (PeekMarker(s, index, 'x'))
            {
                if (!TryParseCodePoint(s, index + 2, out var codePoint))
                {
                    return false;
                }

                name.Append(char.ConvertFromUtf32(codePoint));
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

        // Only characters that the encoder escapes: no ASCII letters or digits, no surrogate halves.
        return codePoint <= 0x10FFFF && codePoint is not (>= 0xD800 and <= 0xDFFF) &&
               (codePoint > 0x7F || !IsAsciiLetterOrDigit((char)codePoint)) && codePoint != '_';
    }

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

    private static bool Peek(string s, int index, char c) => index < s.Length && s[index] == c;

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

    private static bool PeekMarker(string s, int index, char marker) =>
        Peek(s, index, '_') && Peek(s, index + 1, marker);
}

/// <summary>A C# path that becomes one global C++ name: namespaces, containing types and the symbol itself.</summary>
internal sealed class GlobalNamePath : IEquatable<GlobalNamePath>
{
    public GlobalNamePath(IReadOnlyList<GlobalNameSegment> segments, IReadOnlyList<int>? modifiers = null)
    {
        if (segments.Count == 0)
        {
            throw new ArgumentException("A global name path needs at least one segment.", nameof(segments));
        }

        Segments = segments;
        Modifiers = modifiers ?? Array.Empty<int>();
        foreach (var modifier in Modifiers)
        {
            if (modifier < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(modifiers), modifier,
                    "A modifier is 0 for a pointer or the rank of an array.");
            }
        }
    }

    public IReadOnlyList<GlobalNameSegment> Segments { get; }

    /// <summary>Element-type modifiers after the last segment, innermost first: 0 for a pointer, n for an array of rank n.</summary>
    public IReadOnlyList<int> Modifiers { get; }

    /// <summary>Formats the path like a C# name, for example <c>N.G&lt;System.Int32, System.Single[]&gt;</c>.</summary>
    public string ToDisplayString()
    {
        var builder = new StringBuilder();
        AppendDisplay(builder);
        return builder.ToString();
    }

    public bool Equals(GlobalNamePath? other)
    {
        if (other is null || other.Segments.Count != Segments.Count || other.Modifiers.Count != Modifiers.Count)
        {
            return false;
        }

        for (var i = 0; i < Segments.Count; i++)
        {
            if (!Segments[i].Equals(other.Segments[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < Modifiers.Count; i++)
        {
            if (Modifiers[i] != other.Modifiers[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is GlobalNamePath other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var segment in Segments)
            {
                hash = hash * 31 + segment.GetHashCode();
            }

            foreach (var modifier in Modifiers)
            {
                hash = hash * 31 + modifier;
            }

            return hash;
        }
    }

    public override string ToString() => ToDisplayString();

    internal void AppendDisplay(StringBuilder builder)
    {
        for (var i = 0; i < Segments.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('.');
            }

            Segments[i].AppendDisplay(builder);
        }

        foreach (var modifier in Modifiers)
        {
            if (modifier == 0)
            {
                builder.Append('*');
            }
            else
            {
                builder.Append('[').Append(',', modifier - 1).Append(']');
            }
        }
    }
}

/// <summary>One segment of a <see cref="GlobalNamePath"/> with its generic, file and overload markers.</summary>
internal sealed class GlobalNameSegment : IEquatable<GlobalNameSegment>
{
    public GlobalNameSegment(string name, IReadOnlyList<GlobalNamePath>? typeArguments = null, int arity = 0,
        string? fileId = null, int overloadOrdinal = -1)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("A segment needs a name.", nameof(name));
        }

        if (arity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(arity), arity, "The arity must not be negative.");
        }

        if (overloadOrdinal < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(overloadOrdinal), overloadOrdinal,
                "The overload ordinal is -1 (none) or a non-negative index.");
        }

        if (fileId is not null)
        {
            foreach (var c in fileId)
            {
                if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                {
                    throw new ArgumentException("The file identity must be lowercase hex.", nameof(fileId));
                }
            }

            if (fileId.Length == 0)
            {
                throw new ArgumentException("The file identity must not be empty.", nameof(fileId));
            }
        }

        Name = name;
        TypeArguments = typeArguments ?? Array.Empty<GlobalNamePath>();
        Arity = arity;
        FileId = fileId;
        OverloadOrdinal = overloadOrdinal;
    }

    /// <summary>The C# name without the verbatim <c>@</c>.</summary>
    public string Name { get; }

    /// <summary>The type arguments of a constructed generic type, or empty.</summary>
    public IReadOnlyList<GlobalNamePath> TypeArguments { get; }

    /// <summary>The number of type parameters of a generic definition, or 0.</summary>
    public int Arity { get; }

    /// <summary>A lowercase hex identity of the declaring file for a <c>file</c> type, or <see langword="null"/>.</summary>
    public string? FileId { get; }

    /// <summary>The index of the method within its overload group, or -1 when the method is not overloaded.</summary>
    public int OverloadOrdinal { get; }

    public bool Equals(GlobalNameSegment? other)
    {
        if (other is null || !string.Equals(Name, other.Name, StringComparison.Ordinal) || Arity != other.Arity ||
            OverloadOrdinal != other.OverloadOrdinal ||
            !string.Equals(FileId, other.FileId, StringComparison.Ordinal) ||
            TypeArguments.Count != other.TypeArguments.Count)
        {
            return false;
        }

        for (var i = 0; i < TypeArguments.Count; i++)
        {
            if (!TypeArguments[i].Equals(other.TypeArguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is GlobalNameSegment other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StringComparer.Ordinal.GetHashCode(Name);
            hash = hash * 31 + Arity;
            hash = hash * 31 + OverloadOrdinal;
            hash = hash * 31 + (FileId is null ? 0 : StringComparer.Ordinal.GetHashCode(FileId));
            foreach (var argument in TypeArguments)
            {
                hash = hash * 31 + argument.GetHashCode();
            }

            return hash;
        }
    }

    internal void AppendDisplay(StringBuilder builder)
    {
        builder.Append(Name);
        if (TypeArguments.Count > 0)
        {
            builder.Append('<');
            for (var i = 0; i < TypeArguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                TypeArguments[i].AppendDisplay(builder);
            }

            builder.Append('>');
        }

        if (Arity > 0)
        {
            builder.Append('`').Append(Arity.ToString(CultureInfo.InvariantCulture));
        }

        if (FileId is not null)
        {
            builder.Append("(file ").Append(FileId).Append(')');
        }

        if (OverloadOrdinal >= 0)
        {
            builder.Append('#').Append(OverloadOrdinal.ToString(CultureInfo.InvariantCulture));
        }
    }
}
