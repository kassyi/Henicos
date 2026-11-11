namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// How the CPU lowering passes an argument of a call that may suspend
/// (<see cref="SpilledArguments.ClassifyByReference"/>).
/// </summary>
internal enum ByReferencePassing
{
    /// <summary>By value: evaluated into a temporary before the call unless it can stay in place.</summary>
    Value,

    /// <summary>
    /// By reference, as a location whose array, pointer and indices are evaluated once before the call, so that every
    /// resume reaches the same variable.
    /// </summary>
    Location,

    /// <summary>
    /// By reference, through a call, a property or a ref conditional that every resume would evaluate again (GSH0062).
    /// </summary>
    ReselectedLocation
}
