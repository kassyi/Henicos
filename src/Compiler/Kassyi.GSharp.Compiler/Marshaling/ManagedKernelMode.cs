namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>How the managed fallback of the CPU launcher runs a kernel (<see cref="KernelHostModel.ManagedMode"/>).</summary>
internal enum ManagedKernelMode
{
    /// <summary>The kernel's C# state machine, a copy of its body in the generated file.</summary>
    Copy,

    /// <summary>
    /// The user's method, one GPU thread after another as the C# execution runs it: the body names a file-local type,
    /// which a copy cannot name, and needs no copy (GSH0119).
    /// </summary>
    UserMethod,

    /// <summary>
    /// Nothing: the body, or one that its copy needs, names a file-local type and has to be copied, so the managed
    /// launch throws (<see cref="KernelHostModel.ManagedUnavailableReason"/>, GSH0119).
    /// </summary>
    Unavailable
}
