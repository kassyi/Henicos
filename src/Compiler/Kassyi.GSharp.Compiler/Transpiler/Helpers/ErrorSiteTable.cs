namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Represents a 1-based compile-time error site entry recorded for device and native CPU runtime trap reporting (#729).
/// </summary>
internal readonly record struct ErrorSiteEntry(
    uint SiteId,
    string File,
    int Line,
    int Column,
    string MemberName,
    string DefaultMessage);

/// <summary>
/// Assigns 1-based error site IDs and collects source locations (<c>file</c>, <c>line</c>, <c>column</c>, <c>memberName</c>, <c>defaultMessage</c>)
/// for runtime error reporting on CUDA and Native CPU (#729).
/// </summary>
internal sealed class ErrorSiteTable
{
    private readonly ImmutableArray<ErrorSiteEntry>.Builder _entries = ImmutableArray.CreateBuilder<ErrorSiteEntry>();
    private readonly Dictionary<(string File, int Line, int Column, string MemberName, string DefaultMessage), uint> _byKey = [];

    /// <summary>Gets the number of registered error sites.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets the registered error site entries in 1-based site ID order.</summary>
    public ImmutableArray<ErrorSiteEntry> Entries => _entries.ToImmutable();

    /// <summary>
    /// Registers an error site for <paramref name="node"/> and returns its 1-based site ID.
    /// </summary>
    public uint Register(SyntaxNode node, SemanticModel? semanticModel, string memberName, string defaultMessage)
    {
        var site = CreateSite(0u, node, semanticModel, memberName, defaultMessage);
        return Register(site.File, site.Line, site.Column, site.MemberName, site.DefaultMessage);
    }

    /// <summary>
    /// Registers an error site with explicit coordinates and returns its 1-based site ID.
    /// </summary>
    public uint Register(string file, int line, int column, string memberName, string defaultMessage)
    {
        var key = (file, line, column, memberName, defaultMessage);
        if (_byKey.TryGetValue(key, out var existingId))
        {
            return existingId;
        }

        var siteId = (uint)(_entries.Count + 1);
        var entry = new ErrorSiteEntry(siteId, file, line, column, memberName, defaultMessage);
        _entries.Add(entry);
        _byKey[key] = siteId;
        return siteId;
    }

    /// <summary>
    /// Creates an <see cref="ErrorSiteEntry"/> from a syntax node and semantic model using <see cref="SemanticModelExtensions.GetReportLocation"/>.
    /// </summary>
    public static ErrorSiteEntry CreateSite(
        uint siteId,
        SyntaxNode node,
        SemanticModel? semanticModel,
        string memberName,
        string defaultMessage)
    {
        if (semanticModel?.GetReportLocation(node) is { } location && location != Location.None)
        {
            var span = location.GetLineSpan();
            var fileName = Path.GetFileName(span.Path);
            if (!string.IsNullOrEmpty(fileName))
            {
                var start = span.StartLinePosition;
                return new ErrorSiteEntry(siteId, fileName, start.Line + 1, start.Character + 1, memberName, defaultMessage);
            }
        }

        return new ErrorSiteEntry(siteId, SourcePlace.Unknown, 0, 0, memberName, defaultMessage);
    }

    /// <summary>
    /// Appends a <c>__gsharp_trap_site(GSharpErrorCode::&lt;code&gt;, &lt;siteId&gt;u)</c> call to <paramref name="writer"/>.
    /// </summary>
    public static void AppendTrapSiteCall(SourceWriter writer, string errorCodeEnumMember, uint siteId) =>
        writer.Append($"{GSharpInternalNames.Prelude.TrapSite}(GSharpErrorCode::{errorCodeEnumMember}, {siteId}u)");
}
