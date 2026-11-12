namespace Kassyi.GSharp.Compiler.Generators;

/// <summary>
/// Represents a mapped type entry in a YAML dialect definition, with optional byte size and alignment.
/// </summary>
public readonly record struct DialectTypeMapping(string CppType, int? Size = null, int? Align = null)
{
    public bool HasLayout => Size.HasValue && Align.HasValue;
}
