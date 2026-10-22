namespace Kassyi.GSharp.Compiler;

/// <summary>Answers whether a symbol, or a position in source, belongs to device code.</summary>
/// <remarks>
/// A G# function runs on the device when it carries <c>[GpuKernel]</c> or <c>[GpuDeviceFunction]</c>. Checks that need
/// one of the two specifically (a kernel entry point, an instance device function) still test that attribute directly.
/// </remarks>
internal static class GpuExecutionSpace
{
    /// <summary>Whether <paramref name="symbol"/> is a kernel or a device function.</summary>
    public static bool IsGpuFunction(ISymbol? symbol) =>
        symbol is not null &&
        (symbol.HasAttribute(KnownAttributes.GpuKernel) || symbol.HasAttribute(KnownAttributes.GpuDeviceFunction));

    /// <summary>
    /// Whether <paramref name="enclosing"/> (typically <see cref="SemanticModel.GetEnclosingSymbol"/>) or any symbol that
    /// contains it is a GPU function, so that lambdas and local functions inside a GPU function count as device code.
    /// </summary>
    public static bool IsWithinGpuFunction(ISymbol? enclosing)
    {
        for (var symbol = enclosing; symbol is not null; symbol = symbol.ContainingSymbol)
        {
            if (IsGpuFunction(symbol))
            {
                return true;
            }
        }

        return false;
    }
}
