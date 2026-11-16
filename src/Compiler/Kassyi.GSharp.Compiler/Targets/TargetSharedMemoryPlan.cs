namespace Kassyi.GSharp.Compiler.Targets;

/// <summary>
/// The planned target GPU shared memory check to emit in launcher code (#799).
/// </summary>
/// <param name="MaxBytes">The maximum shared memory per block in bytes.</param>
/// <param name="Architecture">The SASS name of the target GPU imposing the limit (<c>sm_86</c>).</param>
/// <param name="HintMaxBytes">
/// The oversized shared memory limit in bytes if the kernel could use it by enabling
/// <c>[GpuKernel(AllowOversizedSharedMemory = true)]</c>, or 0 when no hint is applicable.
/// </param>
internal readonly record struct TargetSharedMemoryPlan(
    int MaxBytes,
    string Architecture,
    int HintMaxBytes = 0);
