using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>A field of a kernel's struct whose type the generated launcher cannot name (GSH0108).</summary>
/// <param name="FieldType">The type that the launcher's class cannot access.</param>
/// <param name="FieldPath">The dotted path of the field from the struct (<c>Inner._hidden</c>).</param>
internal readonly record struct InaccessibleFieldType(ITypeSymbol FieldType, string FieldPath);
