namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The place of a failed check, decided before it is written: a numeric site of the kernel's error site table,
/// or a fallback message string (for example in a device function where no table exists).
/// </summary>
internal readonly record struct ErrorSiteArgument
{
    private readonly string? _message;

    private ErrorSiteArgument(uint siteId, string message)
    {
        SiteId = siteId;
        _message = message;
    }

    /// <summary>The site ID in the error site table, or <c>0</c> when <see cref="Message"/> is used.</summary>
    public uint SiteId { get; }

    /// <summary>The message that names the place when <see cref="SiteId"/> is <c>0</c>.</summary>
    public string Message => _message ?? string.Empty;

    /// <summary>Creates an argument for a site ID registered in the error site table.</summary>
    public static ErrorSiteArgument FromSiteId(uint siteId) => new(siteId, string.Empty);

    /// <summary>
    /// Creates an argument for a site ID as the error site table returns it (<c>int</c>), read as the unsigned ID that
    /// the C++ output writes.
    /// </summary>
    public static ErrorSiteArgument FromSiteId(int siteId) => FromSiteId(unchecked((uint)siteId));

    /// <summary>Creates an argument for a fallback message literal.</summary>
    public static ErrorSiteArgument FromMessage(string message) => new(0u, message);
}
