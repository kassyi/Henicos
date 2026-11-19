namespace Kassyi.GSharp.Compiler.Tasks.NativeUnits;

/// <summary>What a native build has to do: which units to compile, which are current, and what to link.</summary>
/// <param name="PendingUnits">The units whose object file is missing or out of date.</param>
/// <param name="UpToDateUnits">The files of the units that are skipped because their object file is current.</param>
/// <param name="LinkTargets">The modules to link, one per module and architecture.</param>
internal sealed record NativeUnitBuildPlan(
    NativeUnitCompileWork[] PendingUnits,
    NativeUnitArtifacts[] UpToDateUnits,
    NativeLinkTarget[] LinkTargets);
