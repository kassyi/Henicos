namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>
/// Whether a condition has the same value in every thread of a warp, which decides whether the threads
/// that took different branches are made to wait for each other where the branch ends.
/// </summary>
internal enum IrUniformity
{
    /// <summary>
    /// Not decided yet: the builder's value. Reading it is an internal error once the pass that decides it has
    /// run.
    /// </summary>
    Unclassified,

    /// <summary>The method does not reach <c>Warp.ActiveMask()</c>: no thread waits, so nobody reads it.</summary>
    NotRequired,

    /// <summary>The same value in every thread of a warp.</summary>
    Uniform,

    /// <summary>May differ between the threads of a warp.</summary>
    ThreadVariant
}
