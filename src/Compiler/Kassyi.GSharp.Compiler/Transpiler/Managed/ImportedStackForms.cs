namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Eligibility of a suspending <c>[GpuDeviceFunction]</c> declared in another assembly for the managed stack model
/// (<c>GSharpManagedBarrierModel=Stack</c>).
/// </summary>
internal static class ImportedStackForms
{
    /// <summary>
    /// Whether <paramref name="function"/> has a usable managed stack form in its own assembly: its export record is
    /// compatible with this G#, did not fail for the C++ dialect, carries <c>ManagedStackForm</c>, and reaches neither
    /// a grid barrier nor a warp collective (warp collectives across assemblies need PR-X3, and grid barriers need
    /// PR-M2 / D1-X4).
    /// </summary>
    public static bool HasManagedForm(IMethodSymbol function, Compilation compilation) =>
        DeviceExportTable.TryFindFunction(function, compilation, out var imported) &&
        imported.IsCompatible &&
        !imported.FailedFor(isCuda: false) &&
        imported.Record.Traits.ManagedStackForm &&
        !SuspensionAnalysis.ReachesGridBarrierOnStack(function, compilation) &&
        !SuspensionAnalysis.ReachesWarpShuffleOnStack(function, compilation);
}
