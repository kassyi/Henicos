namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>Byte size and alignment of a type.</summary>
public readonly record struct TypeLayout(int Size, int Alignment);
