namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>The C# container of an array-like parameter.</summary>
internal enum ArrayContainerKind
{
    None,
    Array,
    Span,
    ReadOnlySpan,
    Memory,
    ReadOnlyMemory,
    ArraySegment,
    ImmutableArray
}
