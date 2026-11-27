namespace Kassyi.GSharp.Compiler.Semantics;

/// <summary>
/// Decides whether calls or references to a device function route to its generated managed version
/// (<see cref="ManagedVersionAnalysis"/>).
/// </summary>
internal static class ManagedVersionRouting
{
    /// <summary>
    /// Whether <paramref name="method"/> has a managed version.
    /// </summary>
    public static bool IsRouted(IMethodSymbol method, Compilation compilation) =>
        ManagedVersionAnalysis.HasManagedVersion(method, compilation);

    /// <summary>
    /// Whether <paramref name="symbol"/> is an unreduced device function reference that has a managed version.
    /// </summary>
    public static bool IsRoutedReference(IMethodSymbol symbol, Compilation compilation) =>
        symbol.ReducedFrom is null &&
        symbol.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
        IsRouted(symbol, compilation);
}
