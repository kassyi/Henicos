namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>The layout kind of a C# struct on the host.</summary>
public enum TypeLayoutKind
{
    Sequential,
    Explicit,
    Auto
}

/// <summary>How a user struct transfers between the C# host and the native device.</summary>
public enum TransferClass
{
    Identical,
    Convert,
    Unsupported
}

/// <summary>Byte size and alignment of a type.</summary>
public readonly record struct TypeLayout(int Size, int Alignment);

/// <summary>One member of a struct with its byte offset and member layout.</summary>
public readonly record struct FieldLayout(StructAbiMember Member, int Offset, TypeLayout Layout);

/// <summary>The layout of a C# struct in host CLR memory.</summary>
public readonly record struct HostLayout(
    bool IsKnown,
    int Size,
    int Alignment,
    ImmutableArray<FieldLayout> Fields);

/// <summary>Why a struct is <see cref="TransferClass.Unsupported"/>.</summary>
/// <param name="Reason">
/// The reason, as the end of the GSH0078 message (<c>field 'B' at offset 4 partially overlaps field 'A'</c>).
/// </param>
/// <param name="FieldPath">
/// The dotted path of the member whose type has no device representation (GSH0079), otherwise <see langword="null"/>.
/// </param>
/// <param name="FieldType">The type of that member, otherwise <see langword="null"/>.</param>
public readonly record struct UnsupportedLayout(string Reason, string? FieldPath, ITypeSymbol? FieldType)
{
    /// <summary>
    /// Whether a member type has no device representation (GSH0079) rather than the layout being one the device cannot
    /// express (GSH0078).
    /// </summary>
    public bool IsMissingDeviceType => FieldType is not null;
}

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
