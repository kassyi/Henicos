namespace Kassyi.GSharp.Naming;

/// <summary>
/// Spelling of renamed user identifiers in generated C++. Shared by the compiler, which emits the names, and the build
/// tasks, which translate nvcc / host compiler output back to C# names.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description><c>__gsharp_id_{ordinal}_{name}</c>: parameters, locals, labels and local functions. The ordinal is omitted when the declaration could not be located.</description></item>
///   <item><description><c>__gsharp_mb_{name}</c>: instance fields and properties of structs that G# emits.</description></item>
/// </list>
/// GSH0057 rejects <c>__</c> in user identifiers, so a C# name never starts with a digit-and-underscore ordinal and
/// decoding is unambiguous.
/// </remarks>
internal static class GSharpNameEncoding
{
    public const string IdentifierPrefix = "__gsharp_id_";
    public const string MemberPrefix = "__gsharp_mb_";

    /// <summary>Recovers the C# name from a renamed identifier.</summary>
    /// <param name="identifier">A complete C++ identifier.</param>
    /// <param name="csharpName">The C# name without the verbatim <c>@</c>, or an empty string when decoding fails.</param>
    /// <returns><see langword="true"/> if <paramref name="identifier"/> is a renamed user identifier.</returns>
    public static bool TryDecode(string identifier, out string csharpName)
    {
        csharpName = string.Empty;
        if (identifier.StartsWith(MemberPrefix, StringComparison.Ordinal))
        {
            return TrySetName(identifier, MemberPrefix.Length, ref csharpName);
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
        var builder = new System.Text.StringBuilder();
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
        var builder = new System.Text.StringBuilder(text.Length);
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
            identifier = text.Substring(start, end - start);
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

        csharpName = identifier.Substring(start);
        return true;
    }

    private static bool IsIdentifierChar(char c) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_';
}
