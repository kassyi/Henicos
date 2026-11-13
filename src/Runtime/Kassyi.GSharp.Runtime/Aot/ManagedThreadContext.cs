namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// The thread context of the managed path (<see cref="ManagedGpuContext"/>), which only Core, the runtime and the code
/// G# generates set. The tests of the managed runtime reach it here, through the runtime's InternalsVisibleTo: they
/// call its primitives (shared memory, warp exchange, barriers) directly, with the context of one GPU thread set. Tests
/// that run a kernel as C# use its launcher under <see cref="KernelExecution.CSharp"/> instead.
/// </summary>
internal static class ManagedThreadContext
{
    /// <summary>Whether the current host thread runs the C# body of a kernel one GPU thread at a time.</summary>
    public static bool IsCSharpExecution => ManagedGpuContext.IsCSharpExecution;

    /// <summary>Sets the coordinates of the GPU thread that this host thread runs, and marks it active.</summary>
    /// <param name="ctx">The coordinates of the GPU thread.</param>
    public static void Set(in GpuThreadContext ctx) => ManagedGpuContext.SetThreadContext(ctx);

    /// <summary>Clears the thread context of the current host thread.</summary>
    public static void Clear() => ManagedGpuContext.ClearThreadContext();
}
