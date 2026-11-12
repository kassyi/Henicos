namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>The complete device and host layout model for a user struct.</summary>
/// <remarks>
/// <see cref="DeviceFields"/> are in declaration order. For <see cref="TypeLayoutKind.Explicit"/> each offset is the
/// <c>[FieldOffset]</c> value, so fields that share an offset form one union group.
/// </remarks>
public readonly record struct StructLayoutInfo(
    TypeLayoutKind Kind,
    int Pack,
    TypeLayout Device,
    ImmutableArray<FieldLayout> DeviceFields,
    HostLayout Host,
    TransferClass Transfer,
    UnsupportedLayout? Unsupported = null);
