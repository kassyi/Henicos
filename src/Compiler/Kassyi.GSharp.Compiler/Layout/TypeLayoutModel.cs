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

/// <summary>The complete device and host layout model for a user struct.</summary>
public readonly record struct StructLayoutInfo(
    TypeLayoutKind Kind,
    int Pack,
    TypeLayout Device,
    ImmutableArray<FieldLayout> DeviceFields,
    HostLayout Host,
    TransferClass Transfer);
