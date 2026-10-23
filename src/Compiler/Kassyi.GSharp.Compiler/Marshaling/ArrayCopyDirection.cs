namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Which way the host copies an array argument, from <c>[In]</c> / <c>[Out]</c> on the parameter: both ways without
/// either attribute or with both, only to the device with <c>[In]</c>, only back to the host with <c>[Out]</c>.
/// </summary>
internal static class ArrayCopyDirection
{
    /// <summary>Gets whether the host copies <paramref name="parameter"/> to the device and back.</summary>
    /// <remarks>
    /// Only <c>System.Runtime.InteropServices.InAttribute</c> / <c>OutAttribute</c> count; a user's own attribute with the
    /// same name does not (<see cref="KnownAttributes.IsDeclaredInOwnerNamespace"/>).
    /// </remarks>
    public static (bool CopyIn, bool CopyOut) Get(IParameterSymbol parameter)
    {
        var hasIn = parameter.HasAttribute(KnownAttributes.In);
        var hasOut = parameter.HasAttribute(KnownAttributes.Out);
        return (hasIn || !hasOut, hasOut || !hasIn);
    }
}
