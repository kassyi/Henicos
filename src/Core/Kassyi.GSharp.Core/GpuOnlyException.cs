namespace Kassyi.GSharp.Core;

/// <summary>
/// The exception that is thrown when a GPU-only intrinsic, macro, or device syntax marker is executed on the host CPU.
/// </summary>
/// <remarks>
/// Members decorated with GSharp intrinsic attributes or GPU markers exist to provide C# type-safety, IntelliSense, and Roslyn syntax trees. They are replaced during source generation and cannot be invoked at CPU runtime.
/// </remarks>
public class GpuOnlyException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="GpuOnlyException"/> class.</summary>
    public GpuOnlyException()
        : base("This code can only be executed on the GPU.")
    {
    }

    // The exception of a GPU-only operation reached by a kernel that runs as C# one GPU thread at a time
    private GpuOnlyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Throws a new instance of <see cref="GpuOnlyException"/>.
    /// </summary>
    /// <exception cref="GpuOnlyException">Always thrown upon invocation.</exception>
    public static void Throw() => throw new GpuOnlyException();

    /// <summary>
    /// Throws a new instance of <see cref="GpuOnlyException"/> with a generic return type placeholder for expressions.
    /// </summary>
    /// <typeparam name="T">The expected return type of the enclosing expression.</typeparam>
    /// <returns>This method never returns normally.</returns>
    /// <exception cref="GpuOnlyException">Always thrown upon invocation.</exception>
    public static T Throw<T>() => throw new GpuOnlyException();

    /// <summary>
    /// Creates the exception of <paramref name="construct"/> reached by a kernel that runs as C# one GPU thread at a
    /// time (<c>KernelExecution.CSharp</c>): no other thread of its block or grid runs beside it, so an operation that
    /// waits for them or reads their values has no meaning there.
    /// </summary>
    /// <param name="construct">The operation, as the user writes it (<c>Gpu.SyncThreads()</c>).</param>
    internal static GpuOnlyException InCSharpExecution(string construct) =>
        new($"G#: {construct} cannot run in a kernel launched under KernelExecution.CSharp, which runs the C# " +
            "body of the kernel one GPU thread at a time, so no other thread of the block or grid runs beside it. " +
            "Launch the kernel under KernelExecution.Default or KernelExecution.Managed.");
}
