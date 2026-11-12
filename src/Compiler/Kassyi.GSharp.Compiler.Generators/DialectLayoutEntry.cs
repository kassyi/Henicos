namespace Kassyi.GSharp.Compiler.Generators;

/// <summary>Represents a byte size and alignment pair in a YAML dialect definition.</summary>
public readonly record struct DialectLayoutEntry(int Size, int Align);
