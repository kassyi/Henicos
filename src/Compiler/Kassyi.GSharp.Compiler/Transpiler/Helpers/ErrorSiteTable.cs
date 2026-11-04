namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Represents a deterministic 31-bit compile-time error site entry recorded for device and native CPU runtime trap reporting (#729).
/// </summary>
internal readonly record struct ErrorSiteEntry(
    uint SiteId,
    string File,
    int Line,
    int Column,
    string MemberName,
    string DefaultMessage);

/// <summary>
/// Computes stable, deterministic 31-bit site IDs and collects source locations (<c>file</c>, <c>line</c>, <c>column</c>, <c>memberName</c>, <c>defaultMessage</c>)
/// for runtime error reporting on CUDA and Native CPU (#729).
/// </summary>
internal sealed class ErrorSiteTable
{
    private readonly ImmutableArray<ErrorSiteEntry>.Builder _entries = ImmutableArray.CreateBuilder<ErrorSiteEntry>();
    private readonly Dictionary<uint, ErrorSiteEntry> _bySiteId = [];

    /// <summary>Gets the number of registered error sites.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets the registered error site entries in registration order.</summary>
    public ImmutableArray<ErrorSiteEntry> Entries => _entries.ToImmutable();

    /// <summary>
    /// Computes a deterministic 31-bit site ID from source location and trap kind.
    /// Bit 31 is always 0 to avoid collision with BuiltinSiteFlag (0x80000000u).
    /// </summary>
    public static uint ComputeSiteId(string file, int start, int length, string trapKind)
    {
        var key = $"{file}#{start}#{length}#{trapKind}";
        var hash = HashHelper.ComputeFnv1A64(key);
        var siteId = (uint)(hash & 0x7FFFFFFFu);
        return siteId == 0u ? 1u : siteId;
    }

    /// <summary>
    /// Registers an error site for <paramref name="node"/> and returns its deterministic 31-bit site ID.
    /// </summary>
    public uint Register(SyntaxNode node, SemanticModel? semanticModel, string memberName, string defaultMessage, string trapKind = "")
    {
        var entry = CreateSite(node, semanticModel, memberName, defaultMessage, trapKind);
        return RegisterEntry(entry);
    }

    /// <summary>
    /// Registers an error site with line/column coordinates and returns its deterministic 31-bit site ID.
    /// </summary>
    public uint Register(string file, int line, int column, string memberName, string defaultMessage, string trapKind = "")
    {
        return Register(file, line, column, line, column, memberName, defaultMessage, trapKind);
    }

    /// <summary>
    /// Registers an error site with explicit coordinates and returns its deterministic 31-bit site ID.
    /// </summary>
    public uint Register(string file, int line, int column, int start, int length, string memberName, string defaultMessage, string trapKind = "")
    {
        var kind = string.IsNullOrEmpty(trapKind) ? defaultMessage : trapKind;
        var siteId = ComputeSiteId(file, start, length, kind);
        var entry = new ErrorSiteEntry(siteId, file, line, column, memberName, defaultMessage);
        return RegisterEntry(entry);
    }

    /// <summary>
    /// Registers a pre-computed entry.
    /// </summary>
    public uint RegisterEntry(ErrorSiteEntry entry)
    {
        if (_bySiteId.TryGetValue(entry.SiteId, out var existing))
        {
            return existing.SiteId;
        }

        _entries.Add(entry);
        _bySiteId[entry.SiteId] = entry;
        return entry.SiteId;
    }

    /// <summary>
    /// Creates an <see cref="ErrorSiteEntry"/> from a syntax node and semantic model using <see cref="SemanticModelExtensions.GetReportLocation"/>.
    /// </summary>
    public static ErrorSiteEntry CreateSite(
        SyntaxNode node,
        SemanticModel? semanticModel,
        string memberName,
        string defaultMessage,
        string trapKind = "")
    {
        var kind = string.IsNullOrEmpty(trapKind) ? defaultMessage : trapKind;
        if (semanticModel?.GetReportLocation(node) is { } location && location != Location.None)
        {
            var span = location.GetLineSpan();
            var fileName = Path.GetFileName(span.Path);
            if (!string.IsNullOrEmpty(fileName))
            {
                var start = span.StartLinePosition;
                var sourceSpan = location.SourceSpan;
                var siteId = ComputeSiteId(fileName, sourceSpan.Start, sourceSpan.Length, kind);
                return new ErrorSiteEntry(siteId, fileName, start.Line + 1, start.Character + 1, memberName, defaultMessage);
            }
        }

        var fallbackId = ComputeSiteId(SourcePlace.Unknown, 0, 0, kind);
        return new ErrorSiteEntry(fallbackId, SourcePlace.Unknown, 0, 0, memberName, defaultMessage);
    }

    /// <summary>
    /// Aggregates error sites across all kernels in a module, deduplicating identical entries
    /// and detecting site ID collisions with different entries.
    /// </summary>
    public static bool TryBuildModuleSites(
        IEnumerable<ErrorSiteEntry> sites,
        out ImmutableArray<ErrorSiteEntry> uniqueSites,
        out ErrorSiteEntry collisionFirst,
        out ErrorSiteEntry collisionSecond)
    {
        var builder = ImmutableArray.CreateBuilder<ErrorSiteEntry>();
        var seen = new Dictionary<uint, ErrorSiteEntry>();
        foreach (var site in sites)
        {
            if (seen.TryGetValue(site.SiteId, out var existing))
            {
                if (existing != site)
                {
                    uniqueSites = ImmutableArray<ErrorSiteEntry>.Empty;
                    collisionFirst = existing;
                    collisionSecond = site;
                    return false;
                }
                continue;
            }

            seen[site.SiteId] = site;
            builder.Add(site);
        }

        uniqueSites = builder.ToImmutable();
        collisionFirst = default;
        collisionSecond = default;
        return true;
    }

    /// <summary>
    /// Appends a <c>__gsharp_trap_site(GSharpErrorCode::&lt;code&gt;, &lt;siteId&gt;u)</c> call to <paramref name="writer"/>.
    /// </summary>
    public static void AppendTrapSiteCall(SourceWriter writer, string errorCodeEnumMember, uint siteId) =>
        writer.Append($"{GSharpInternalNames.Prelude.TrapSite}(GSharpErrorCode::{errorCodeEnumMember}, {siteId}u)");
}
