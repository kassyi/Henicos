namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// Where a failing runtime check reports (<see cref="NullCheckSiteWriter.PlanSite"/>):
/// a site of the kernel's error site table (<paramref name="SiteId"/>, never 0), or <paramref name="Message"/>, which
/// names the place, when the method has no table.
/// </summary>
internal readonly record struct NullCheckSite(uint SiteId, string Message);
