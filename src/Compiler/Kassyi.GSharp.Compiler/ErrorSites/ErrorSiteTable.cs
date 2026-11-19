namespace Kassyi.GSharp.Compiler.ErrorSites;

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
    /// Computes a deterministic 31-bit site ID from the file name, the 1-based start and end line and column of the site,
    /// and the trap kind. Lines and columns (not character offsets) keep the ID the same for LF and CRLF checkouts.
    /// Bit 31 is always 0 to avoid collision with BuiltinSiteFlag (0x80000000u).
    /// </summary>
    public static uint ComputeSiteId(string file, int startLine, int startColumn, int endLine, int endColumn, string trapKind)
    {
        var key = $"{file}#{startLine}:{startColumn}#{endLine}:{endColumn}#{trapKind}";
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
    /// Registers the error site of a <c>throw</c> at <paramref name="node"/>: the place, the message and the exception
    /// type that the host builds the exception from.
    /// </summary>
    public uint RegisterThrow(SyntaxNode node, SemanticModel? semanticModel, string memberName, in ThrownException thrown)
    {
        var entry = CreateSite(node, semanticModel, memberName, thrown.Message, ThrownException.TrapKind) with
        {
            ExceptionType = thrown.ExceptionType
        };
        return RegisterEntry(entry);
    }

    /// <summary>
    /// Registers an error site for <paramref name="location"/> and returns its deterministic 31-bit site ID.
    /// </summary>
    public uint Register(Location location, string memberName, string defaultMessage, string trapKind = "")
    {
        var entry = CreateSite(location, memberName, defaultMessage, trapKind);
        return RegisterEntry(entry);
    }

    /// <summary>
    /// Registers an error site with line/column coordinates and returns its deterministic 31-bit site ID.
    /// </summary>
    public uint Register(string file, int line, int column, string memberName, string defaultMessage, string trapKind = "")
    {
        var kind = string.IsNullOrEmpty(trapKind) ? defaultMessage : trapKind;
        var siteId = ComputeSiteId(file, line, column, line, column, kind);
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
    /// Creates an <see cref="ErrorSiteEntry"/> from a report location.
    /// </summary>
    public static ErrorSiteEntry CreateSite(
        Location location,
        string memberName,
        string defaultMessage,
        string trapKind = "")
    {
        var kind = string.IsNullOrEmpty(trapKind) ? defaultMessage : trapKind;
        if (location != Location.None)
        {
            var span = location.GetLineSpan();
            var fileName = Path.GetFileName(span.Path);
            if (!string.IsNullOrEmpty(fileName))
            {
                var start = span.StartLinePosition;
                var end = span.EndLinePosition;
                var siteId = ComputeSiteId(
                    fileName, start.Line + 1, start.Character + 1, end.Line + 1, end.Character + 1, kind);
                return new ErrorSiteEntry(
                    siteId, fileName, start.Line + 1, start.Character + 1, memberName, defaultMessage);
            }
        }

        // [WHY] Every place of a tree without a file path is unknown: the member keeps the sites of two kernels apart,
        // whose one ID would be a collision (GSH0103) and drop the error site table of their class
        var fallbackId = ComputeSiteId(SourcePlace.Unknown, 0, 0, 0, 0, kind + "@" + memberName);
        return new ErrorSiteEntry(fallbackId, SourcePlace.Unknown, 0, 0, memberName, defaultMessage);
    }

    /// <summary>
    /// Creates an <see cref="ErrorSiteEntry"/> from a syntax node and semantic model, at the location
    /// <see cref="SemanticModelExtensions.GetReportLocation"/> gives.
    /// </summary>
    public static ErrorSiteEntry CreateSite(
        SyntaxNode node,
        SemanticModel? semanticModel,
        string memberName,
        string defaultMessage,
        string trapKind = "") =>
        CreateSite(semanticModel?.GetReportLocation(node) ?? Location.None, memberName, defaultMessage, trapKind);

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

    /// <summary>
    /// Merges <paramref name="additions"/> into <paramref name="existing"/>, preserving order and deduplicating by
    /// <see cref="ErrorSiteEntry.SiteId"/>.
    /// </summary>
    public static EquatableArray<ErrorSiteEntry> MergeSites(
        EquatableArray<ErrorSiteEntry> existing,
        EquatableArray<ErrorSiteEntry> additions)
    {
        if (additions.IsDefaultOrEmpty)
        {
            return existing;
        }

        if (existing.IsDefaultOrEmpty)
        {
            return additions;
        }

        var seen = new HashSet<uint>();
        var builder = ImmutableArray.CreateBuilder<ErrorSiteEntry>(existing.Length + additions.Length);
        foreach (var site in existing)
        {
            if (seen.Add(site.SiteId))
            {
                builder.Add(site);
            }
        }

        foreach (var site in additions)
        {
            if (seen.Add(site.SiteId))
            {
                builder.Add(site);
            }
        }

        return builder.ToImmutable().AsEquatableArray();
    }
}
