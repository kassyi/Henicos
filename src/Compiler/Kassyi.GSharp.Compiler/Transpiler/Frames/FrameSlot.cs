namespace Kassyi.GSharp.Compiler.Transpiler.Frames;

/// <summary>A hoisted local of a CPU state machine, decided before the frame is written.</summary>
/// <param name="Kind">Where it is kept.</param>
/// <param name="Type">Its type (<see cref="HoistedFieldTypeResolver.Resolve"/>).</param>
/// <param name="Name">
/// Its name as the writer spells it: the C++ name on the native path, the field or local name on the managed path.
/// </param>
/// <param name="ElementType">
/// The element type of a <see cref="FrameSlotKind.RedeclaredStack"/> slot, otherwise null.
/// </param>
/// <param name="Length">The element count of a <see cref="FrameSlotKind.RedeclaredStack"/> slot, otherwise 0.</param>
/// <param name="UnsupportedLocation">
/// Where a <see cref="FrameSlotKind.Unsupported"/> slot is reported, otherwise null.
/// </param>
/// <param name="Managed">How the managed writer spells it; null on the native path.</param>
/// <param name="HoldsReadOnlyArray">
/// Whether it holds a <c>[GpuReadOnly]</c> array (<see cref="HoistedFieldTypeResolver.HoldsReadOnlyArray"/>); the
/// native field then keeps the <c>const</c> elements. Always false on the managed path.
/// </param>
internal sealed record FrameSlot(
    FrameSlotKind Kind,
    ITypeSymbol Type,
    string Name,
    ITypeSymbol? ElementType,
    int Length,
    Location? UnsupportedLocation,
    ManagedSlotSpelling? Managed,
    bool HoldsReadOnlyArray = false);
