namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// The error site and the thread and block coordinates of a runtime error, as one value to map to an exception.
/// </summary>
/// <param name="SiteId">
/// The local error site ID, or a builtin message ID with <see cref="GSharpErrorMapper.BuiltinSiteFlag"/> set.
/// </param>
/// <param name="ModuleId">The module ID associated with the site table.</param>
/// <param name="ThreadLinear">The packed thread coordinates.</param>
/// <param name="BlockCoord">The unpacked 3D block coordinates.</param>
internal readonly record struct ErrorSiteLocation(
    uint SiteId,
    uint ModuleId,
    uint ThreadLinear,
    (int X, int Y, int Z) BlockCoord);
