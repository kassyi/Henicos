namespace Kassyi.GSharp.Compiler.Tasks.NativeUnits;

/// <summary>The task settings that the build plan depends on, decided before planning starts.</summary>
/// <param name="ObjectRoot">The root directory of the per-architecture object directories.</param>
/// <param name="OutputDirectory">The directory that receives the linked libraries.</param>
/// <param name="ObjectExtension">The object file extension, including the dot.</param>
/// <param name="UsesPerUnitVcPdb">Whether each unit writes its own program database.</param>
/// <param name="Sanitizers">The sanitizers the units are compiled with.</param>
/// <param name="PathComparer">The comparer that matches paths on the current platform.</param>
/// <param name="RunsOnNativeStacks">Whether the module runs on per-GPU-thread native stacks.</param>
internal readonly record struct NativeUnitBuildSettings(
    string ObjectRoot,
    string OutputDirectory,
    string ObjectExtension,
    bool UsesPerUnitVcPdb,
    NativeSanitizers Sanitizers,
    StringComparer PathComparer,
    bool RunsOnNativeStacks = false);
