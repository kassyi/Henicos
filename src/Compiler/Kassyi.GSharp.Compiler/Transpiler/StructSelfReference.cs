namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The struct type and <see cref="SelfForm"/> of the struct body currently being emitted.
/// </summary>
internal readonly record struct StructSelfReference(INamedTypeSymbol? Type, SelfForm Form);
