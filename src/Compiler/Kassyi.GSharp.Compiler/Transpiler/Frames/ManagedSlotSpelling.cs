namespace Kassyi.GSharp.Compiler.Transpiler.Frames;

/// <summary>The text the managed writer needs for a slot, beyond its type symbol.</summary>
/// <param name="WrittenType">
/// The type as written in the source, qualified for the generated file and without trivia; null when there is none
/// to write (<c>var</c>, or a frame that was not built from syntax), and the writer then spells
/// <see cref="FrameSlot.Type"/>.
/// </param>
/// <param name="NamesPointer">
/// Whether the type holds a pointer or function pointer type anywhere (<c>int*[]</c> included), which makes the
/// frame unsafe.
/// </param>
/// <param name="Initializer">
/// For a <see cref="FrameSlotKind.SharedMemory"/> slot, the initializer (<c>= ...</c>) written with its declaration
/// before the state switch, without trivia; otherwise null.
/// </param>
internal sealed record ManagedSlotSpelling(string? WrittenType, bool NamesPointer, string? Initializer);
