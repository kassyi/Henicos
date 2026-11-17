namespace Kassyi.GSharp.Compiler.ErrorSites;

/// <summary>
/// Writes an <see cref="ErrorSiteEntry"/> as one string and reads it back: the error sites of a device function travel
/// in its export record (<c>GpuDeviceExportAttribute.ErrorSites</c>) to the assembly that places the function.
/// </summary>
internal static class ErrorSiteCodec
{
    // The unit separator, which no file name, member name or message holds
    private const char Separator = '\u001F';

    /// <summary>Writes <paramref name="entry"/>: ID, file, line, column, member, message, exception type.</summary>
    public static string Encode(in ErrorSiteEntry entry) =>
        $"{entry.SiteId}{Separator}{entry.File}{Separator}{entry.Line}{Separator}{entry.Column}{Separator}" +
        $"{entry.MemberName}{Separator}{entry.DefaultMessage}{Separator}{entry.ExceptionType}";

    /// <summary>Reads what <see cref="Encode"/> wrote; false for any other text.</summary>
    public static bool TryDecode(string text, out ErrorSiteEntry entry)
    {
        entry = default;
        var parts = new string[7];
        var rest = text.AsSpan();
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var cut = rest.IndexOf(Separator);
            if (cut < 0)
            {
                return false;
            }

            parts[i] = rest[..cut].ToString();
            rest = rest[(cut + 1)..];
        }

        parts[6] = rest.ToString();
        if (rest.IndexOf(Separator) >= 0 ||
            !uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var siteId) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var line) ||
            !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var column))
        {
            return false;
        }

        entry = new ErrorSiteEntry(siteId, parts[1], line, column, parts[4], parts[5], parts[6]);
        return true;
    }
}
