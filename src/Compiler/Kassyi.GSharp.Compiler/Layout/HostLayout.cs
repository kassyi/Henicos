namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>The layout of a C# struct in host CLR memory.</summary>
public readonly record struct HostLayout(
    bool IsKnown,
    int Size,
    int Alignment,
    ImmutableArray<FieldLayout> Fields);
