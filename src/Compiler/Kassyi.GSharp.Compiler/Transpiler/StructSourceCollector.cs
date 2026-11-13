namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Receives, struct by struct, the struct definitions and <c>Dispose</c> definitions that a method transpilation writes
/// in front of the method, so that a device code export can record each struct once instead of inside every function.
/// </summary>
/// <remarks>
/// The text the method returns does not change: each part in <see cref="Definitions"/> and <see cref="Disposes"/> is
/// also written where it always was. <see cref="Discovered"/> holds the structs that the definitions name without the
/// method's text defining them (a constructor parameter, an operator's result), which only the export needs.
/// </remarks>
internal sealed class StructSourceCollector
{
    /// <summary>The definitions (<c>StructTranspiler.WriteStructDefinition</c>) written in front of the method, in order.</summary>
    public ImmutableArray<StructSourcePart>.Builder Definitions { get; } = ImmutableArray.CreateBuilder<StructSourcePart>();

    /// <summary>The <c>Dispose</c> definitions (<c>UsingTranspiler.WriteDisposeDefinitions</c>) written in front of the method.</summary>
    public ImmutableArray<StructSourcePart>.Builder Disposes { get; } = ImmutableArray.CreateBuilder<StructSourcePart>();

    /// <summary>The definitions of the structs that the definitions name and the method's text does not define.</summary>
    public ImmutableArray<StructSourcePart>.Builder Discovered { get; } = ImmutableArray.CreateBuilder<StructSourcePart>();

    /// <summary>Whether a definition of <paramref name="type"/> was collected.</summary>
    public bool HasDefinition(INamedTypeSymbol type) =>
        Definitions
            .AsStackEnumerable()
            .Any(type, static (part, t) => part.Type.IsEqualTo(t)) ||
        Discovered
            .AsStackEnumerable()
            .Any(type, static (part, t) => part.Type.IsEqualTo(t));
}
