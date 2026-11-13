namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Decides the CPU <c>Launch{Kernel}Ptr</c> launcher of a kernel: the native arguments that it passes to the native
/// launcher export through an unmanaged function pointer, and what it does on the managed CPU path.
/// <see cref="CpuPointerLaunchEmitter"/> writes it.
/// </summary>
internal readonly struct CpuPointerLaunchPlan
{
    private CpuPointerLaunchPlan(CpuNativeArgumentPlan arguments, string managedExecutionMessage)
    {
        Arguments = arguments;
        ManagedExecutionMessage = managedExecutionMessage;
    }

    /// <summary>
    /// The parameters of the native launcher and the form of each argument
    /// (<see cref="CpuNativeArgumentPlan.CreateForPointerLauncher"/>).
    /// </summary>
    public CpuNativeArgumentPlan Arguments { get; }

    /// <summary>
    /// The message of the <c>NotSupportedException</c> that the launcher throws on the managed CPU path
    /// (<c>CpuKernelLoader.ShouldUseManagedExecution</c>), which does not run a pointer launcher yet.
    /// </summary>
    public string ManagedExecutionMessage { get; }

    /// <summary>Plans the CPU pointer launcher of <paramref name="kernel"/>.</summary>
    public static CpuPointerLaunchPlan Create(in KernelHostModel kernel) =>
        new(CpuNativeArgumentPlan.CreateForPointerLauncher(kernel), GetManagedExecutionMessage(kernel));

    // [WHY] The managed path runs the user's C# kernel, whose parameters are the declared types (T[], Span<T>, ...);
    // binding the caller's addresses to them is not written yet, so the launcher names the two ways that run
    private static string GetManagedExecutionMessage(in KernelHostModel kernel) =>
        $"{KernelHostNames.GetTypedPointerLaunchName(kernel)} runs only on the native CPU path: the managed CPU path " +
        "(KernelExecution.Managed, or a build without the native library) does not run pointer launchers yet. " +
        $"Build the native library, or call {KernelHostNames.GetLaunchName(kernel)}, which runs on both paths.";
}
