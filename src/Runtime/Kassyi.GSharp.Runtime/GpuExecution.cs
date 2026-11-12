namespace Kassyi.GSharp.Runtime;

/// <summary>
/// Selects how the generated <c>Launch{Kernel}</c> methods run kernels (<see cref="KernelExecution"/>) for the code
/// inside a scope, the launches of libraries included.
/// </summary>
/// <remarks>
/// The selection follows the asynchronous flow, as <see cref="AsyncLocal{T}"/> does: tasks started and continuations
/// that run inside the scope see it, and so do threads started inside it; other threads and code after the scope do
/// not.
/// </remarks>
/// <example>
/// <code>
/// using (GpuExecution.Use(KernelExecution.CSharp))
/// {
///     AddKernels.LaunchAddKernel(a, b, result, blockDimX: 256);   // the C# value of each thread
/// }
/// </code>
/// </example>
public static class GpuExecution
{
    private static readonly AsyncLocal<KernelExecution> s_current = new();

    /// <summary>Gets how the launches of the current asynchronous flow run kernels.</summary>
    public static KernelExecution Current => s_current.Value;

    /// <summary>
    /// Runs the launches of the current asynchronous flow as <paramref name="execution"/> until the returned scope is
    /// disposed, which restores the previous selection. Scopes nest; dispose them in the reverse order.
    /// </summary>
    /// <param name="execution">How the launches inside the scope run kernels.</param>
    /// <returns>The scope, to dispose with <see langword="using"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="execution"/> is not a defined value.</exception>
    public static GpuExecutionScope Use(KernelExecution execution)
    {
        if (execution is not (KernelExecution.Default or KernelExecution.Managed or KernelExecution.CSharp))
        {
            throw new ArgumentOutOfRangeException(nameof(execution), execution, "Not a KernelExecution value.");
        }

        var previous = s_current.Value;
        s_current.Value = execution;
        return new GpuExecutionScope(previous);
    }

    /// <summary>Restores the selection that a scope replaced.</summary>
    internal static void Restore(KernelExecution previous) => s_current.Value = previous;
}
