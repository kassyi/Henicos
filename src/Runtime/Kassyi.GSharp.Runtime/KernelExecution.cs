namespace Kassyi.GSharp.Runtime;

/// <summary>How the generated <c>Launch{Kernel}</c> methods run a kernel (<see cref="GpuExecution.Use"/>).</summary>
public enum KernelExecution
{
    /// <summary>
    /// The build's own path: the GPU on a CUDA build; the native CPU library on a CPU build, or the managed CPU path
    /// when the library is not there.
    /// </summary>
    Default,

    /// <summary>
    /// The managed CPU path on a CPU build, even when the native CPU library is there: the kernel runs as the C# state
    /// machine that G# generates for it, which reproduces barriers, warp intrinsics and shared memory. A CUDA build
    /// still runs on the GPU.
    /// </summary>
    Managed,

    /// <summary>
    /// The kernel's own C# method, once per GPU thread of the launch, one thread after another in the order of their
    /// linear index, on a host thread of its own: the value that C# gives for each thread. A kernel that reaches a
    /// barrier, a warp intrinsic, shared memory or code that only the managed path rewrites throws
    /// <see cref="InvalidOperationException"/> instead of running, since no other thread runs beside the one being run.
    /// </summary>
    CSharp
}
