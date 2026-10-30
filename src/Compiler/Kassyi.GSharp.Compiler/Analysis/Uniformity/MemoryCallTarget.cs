namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>Memory that a call may write, as <see cref="MemoryUniformity"/> records it.</summary>
/// <param name="Kind">How the call reaches the memory.</param>
/// <param name="Operation">
/// The location (<see cref="MemoryCallTargetKind.ByReference"/>) or the reference to the memory (an argument or the
/// receiver); none for <see cref="MemoryCallTargetKind.Field"/>.
/// </param>
/// <param name="Memory">The value index of the memory of a <see cref="MemoryCallTargetKind.Field"/>.</param>
/// <param name="Ordinal">The ordinal of the parameter that receives it, or -1.</param>
internal readonly record struct MemoryCallTarget(
    MemoryCallTargetKind Kind,
    IOperation? Operation,
    int Memory = -1,
    int Ordinal = -1);
