namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// Plans and writes C++ trap error-site arguments (<c>&lt;siteId&gt;u</c> for registered kernel error sites, or a
/// string literal in device functions without an error site table).
/// </summary>
internal static class NullCheckSiteWriter
{
    /// <summary>Separator between the error text and the formatted source place when no error site table exists.</summary>
    internal const string PlaceSeparator = " at ";

    /// <summary>
    /// Plans the error site of a check at <paramref name="location"/> (a report location, or
    /// <c>IrSource.ToLocation</c>): an entry of <paramref name="table"/> in a kernel, or the message and place in a
    /// device function, which has no table.
    /// </summary>
    internal static NullCheckSite PlanSite(
        Location location,
        string? place,
        string message,
        string trapKind,
        Compilation compilation,
        ErrorSiteTable? table,
        string? currentMemberName)
    {
        if (table is null)
        {
            return new NullCheckSite(0u, message + PlaceSeparator + (place ?? SourcePlace.Format(location)));
        }

        var memberName = ErrorSiteTable.GetSourceMemberName(location, compilation) ?? currentMemberName ?? "";
        return new NullCheckSite(table.Register(location, memberName, message, trapKind), "");
    }

    /// <summary>Writes <c>&lt;site&gt;u</c> for a registered site, otherwise the message as a string literal.</summary>
    internal static void AppendSite(SourceWriter writer, NullCheckSite site)
    {
        if (site.SiteId != 0u)
        {
            writer.Append($"{site.SiteId}u");
            return;
        }

        CppStringLiteral.Append(writer, site.Message);
    }

    /// <summary>Writes <c>,&lt;site&gt;</c>, the last argument of the helper, when there is a site.</summary>
    public static void AppendSiteArgument(SourceWriter writer, NullCheckSite? site)
    {
        if (site is not { } value)
        {
            return;
        }

        writer.Append(',');
        AppendSite(writer, value);
    }
}
