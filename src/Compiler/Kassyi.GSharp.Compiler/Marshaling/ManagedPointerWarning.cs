namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// CS8500 (a pointer to, the address of or the size of a managed type), which the generated launchers and unsafe
/// managed state machines suppress where the pointer is to memory they pinned.
/// </summary>
internal static class ManagedPointerWarning
{
    /// <summary>The warning ID.</summary>
    public const string Id = "CS8500";

    /// <summary>The comment of the suppression around a launcher and around the marshaled structs.</summary>
    public const string Description = "Takes the address of, gets the size of, or declares a pointer to a managed type";

    // [WHY] An unsafe context or frame is a struct that holds managed references (arrays, the frames of its calls),
    // and the state machine takes it by pointer. The pointer is sound: the launcher pins the contexts with fixed for
    // the whole launch, and a frame is a field of its caller's pinned context, so the GC neither moves them nor loses
    // the references they hold
    /// <summary>The comment of the suppression around an unsafe managed state machine.</summary>
    public const string PinnedContext =
        "The context is pinned by the launcher for the whole launch; a frame is a field of a pinned context";
}
