namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>
/// Reads the layout of a struct from a metadata reference: the layout kind from its TypeDef flags, <c>Pack</c> and
/// <c>Size</c> from its ClassLayout row, and a field's offset from its FieldLayout row.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] <c>[StructLayout]</c> and <c>[FieldOffset]</c> are pseudo-custom attributes: for a type from metadata, Roslyn's
/// public API returns neither (<c>GetAttributes()</c> is empty, and no symbol member exposes the layout; checked with
/// Roslyn 4.8 and 5.0), so the only public way to them is the module's metadata, which
/// <see cref="IModuleSymbol.GetMetadata"/> hands out. Reading it goes through <c>System.Reflection.Metadata</c>, a
/// namespace the compiler bans (BannedSymbols.txt) because reflection would slow the compilation down. This reader only
/// reads rows of the tables Roslyn has already loaded; it loads no type at run time.
/// </para>
/// <para>
/// Measured for #736: 0.3 to 2.4 us per struct, at most 0.4 ms per compilation (the Kassyi.GSharp.Tests.Cpu build: 1095
/// reads), under 0.01% of the generator's time. A type of the compilation, or of a compilation reference (the IDE),
/// has no metadata and is read from its attributes by <see cref="StructLayoutReader"/>.
/// </para>
/// </remarks>
internal static class MetadataStructLayoutReader
{
    /// <summary>
    /// Reads the layout of <paramref name="structType"/> when it comes from a metadata reference; returns false for a
    /// type that has no metadata (one of the compilation or of a compilation reference).
    /// </summary>
    public static bool TryRead(INamedTypeSymbol structType, out TypeLayoutKind kind, out int pack, out int size)
    {
        kind = TypeLayoutKind.Sequential;
        pack = 0;
        size = 0;

        // A constructed generic type shares the row of its definition
        var definition = structType.OriginalDefinition;
        if (definition.ContainingModule?.GetMetadata() is not { } module || definition.MetadataToken == 0)
        {
            return false;
        }

#pragma warning disable RS0030 // Reads the ClassLayout table of a metadata reference; see the remarks of the class (#736)
        var reader = module.GetMetadataReader();
        var handle = (System.Reflection.Metadata.TypeDefinitionHandle)
            System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(definition.MetadataToken);
        var typeDefinition = reader.GetTypeDefinition(handle);
        var classLayout = typeDefinition.GetLayout();
        kind = (typeDefinition.Attributes & System.Reflection.TypeAttributes.LayoutMask) switch
        {
            System.Reflection.TypeAttributes.ExplicitLayout => TypeLayoutKind.Explicit,
            System.Reflection.TypeAttributes.AutoLayout => TypeLayoutKind.Auto,
            _ => TypeLayoutKind.Sequential
        };
        pack = classLayout.PackingSize;
        size = classLayout.Size;
#pragma warning restore RS0030
        return true;
    }

    /// <summary>
    /// Reads the offset of <paramref name="field"/> when it comes from a metadata reference (0 when its FieldLayout row
    /// is missing); returns false for a field that has no metadata.
    /// </summary>
    public static bool TryGetFieldOffset(IFieldSymbol field, out int offset)
    {
        offset = 0;
        var definition = field.OriginalDefinition;
        if (definition.ContainingModule?.GetMetadata() is not { } module || definition.MetadataToken == 0)
        {
            return false;
        }

#pragma warning disable RS0030 // Reads the FieldLayout table of a metadata reference; see the remarks of the class (#736)
        var handle = (System.Reflection.Metadata.FieldDefinitionHandle)
            System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(definition.MetadataToken);
        offset = Math.Max(module.GetMetadataReader().GetFieldDefinition(handle).GetOffset(), 0);
#pragma warning restore RS0030
        return true;
    }
}
