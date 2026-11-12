namespace Kassyi.GSharp.Core;

// The C# execution of a kernel (KernelExecution.CSharp of Kassyi.GSharp.Runtime): the runtime runs the kernel's own C#
// method once per GPU thread, one thread after another on one host thread, with the context of that thread set. An
// operation that waits for the other threads of the block or grid, or reads their values, has no meaning there: it
// throws, instead of returning at once (a barrier) or reading lanes that never run beside it.
public static partial class ManagedGpuContext
{
    /// <summary>Whether the current host thread runs the C# body of a kernel one GPU thread at a time.</summary>
    [field: ThreadStatic]
    internal static bool IsCSharpExecution { get; private set; }

    /// <summary>Sets the context of the GPU thread whose C# body the current host thread runs next.</summary>
    /// <param name="ctx">The coordinates of the GPU thread.</param>
    internal static void BeginCSharpThread(in GpuThreadContext ctx)
    {
        SetThreadContext(ctx);
        IsCSharpExecution = true;
    }

    /// <summary>Clears the context after the last GPU thread of a C# execution, also when one of them threw.</summary>
    internal static void EndCSharpExecution()
    {
        IsCSharpExecution = false;
        ClearThreadContext();
    }

    /// <summary>
    /// Throws <see cref="GpuOnlyException"/> when the current host thread runs a kernel as C# one GPU thread at a time,
    /// which <paramref name="construct"/> cannot take part in.
    /// </summary>
    /// <param name="construct">The operation, as the user writes it (<c>Gpu.SyncThreads()</c>).</param>
    /// <exception cref="GpuOnlyException">The current host thread runs a kernel as C#.</exception>
    internal static void ThrowIfCSharpExecution(string construct)
    {
        if (IsCSharpExecution)
        {
            throw GpuOnlyException.InCSharpExecution(construct);
        }
    }
}
