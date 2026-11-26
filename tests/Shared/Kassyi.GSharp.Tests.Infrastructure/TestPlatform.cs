namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Operating system platform required by a test when it cannot run meaningfully elsewhere.
/// </summary>
public enum TestPlatform
{
    /// <summary>Windows host only (for example MSVC toolchain, COM apartments, or Windows job objects).</summary>
    Windows,
}
