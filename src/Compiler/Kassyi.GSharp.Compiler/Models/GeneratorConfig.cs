using Kassyi.GSharp.Compiler.Targets;

namespace Kassyi.GSharp.Compiler.Models;

/// <param name="TargetDialect">The <c>GSharpTargetDialect</c> of the project.</param>
/// <param name="EmitSource">Whether <c>GSharpEmitSource</c> embeds the generated C++ as constants.</param>
/// <param name="IntermediateOutputPath">Where the AOT artifacts are written, or <see langword="null"/>.</param>
/// <param name="ExportDeviceCode">
/// Whether the assembly records its device code for other assemblies (<c>GSharpExportDeviceCode</c>, default true).
/// </param>
/// <param name="IsCheckedBuild">
/// Whether <c>GSharpCheckedBuild</c> enables checked device assertions and post-launch stream synchronization (#729).
/// </param>
/// <param name="TargetLimits">
/// The launch limits of the GPUs that <c>GSharpTargetArchitectures</c> builds for
/// (<see cref="GpuArchitectureLimits"/>).
/// </param>
internal record struct GeneratorConfig(
    string TargetDialect,
    bool EmitSource,
    string? IntermediateOutputPath,
    bool ExportDeviceCode = true,
    bool IsCheckedBuild = false,
    TargetLimits TargetLimits = default)
{
    /// <summary>The execution model for block barriers on the managed CPU path (<c>GSharpManagedBarrierModel</c>).</summary>
    public ManagedBarrierModel ManagedBarrierModel { get; init; } = ManagedBarrierModel.StateMachine;

    /// <summary>Whether <c>GSharpTargetDialect</c> selects the CUDA dialect; any other value selects the C++ dialect.</summary>
    public readonly bool IsCuda => TranspilerDialects.IsCuda(TargetDialect);

    /// <summary>The <c>GSharpNativeBarrierModel</c> of the project; only the C++ dialects read it.</summary>
    public NativeBarrierModel NativeBarrierModel { get; init; }

    /// <summary>
    /// Whether the native C++ module runs the GPU threads of a block on their own stacks
    /// (<c>GSharpNativeBarrierModel=Stack</c> on a C++ dialect). The CUDA dialect never does: the generator does not
    /// even translate the stack form then.
    /// </summary>
    public readonly bool RunsOnNativeStacks => !IsCuda && NativeBarrierModel == NativeBarrierModel.Stack;

    /// <summary>The generated class that holds the prelude source shared by every kernel.</summary>
    public readonly string SharedPreludeClassName => $"GSharpShared{TargetDialect}Prelude";

    /// <summary>The generated class that holds the device function source shared by every kernel.</summary>
    public readonly string SharedDeviceCodeClassName => $"GSharpShared{TargetDialect}DeviceCode";

    public readonly ITranspilerDialect CreateDialect() => TranspilerDialects.Create(TargetDialect);
}
