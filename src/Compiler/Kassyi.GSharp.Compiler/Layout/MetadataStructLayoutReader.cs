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
/// <para>
/// The raw <c>Size</c> of an empty struct differs between the two paths: its attribute states none (0), but Roslyn emits
/// a ClassLayout row with <c>Size = 1</c> for it. The computed layout is the same, 1 byte either way.
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

        if (!TryGetRow(structType, out var module, out var token))
        {
            return false;
        }

#pragma warning disable RS0030 // TypeDef flags and ClassLayout row, which Roslyn's public API does not expose; measured: at most 0.4 ms per compilation (#736, class remarks)
        var reader = module.GetMetadataReader();
        var handle = (System.Reflection.Metadata.TypeDefinitionHandle)
            System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(token);
        var typeDefinition = reader.GetTypeDefinition(handle);
        System.Reflection.Metadata.TypeLayout classLayout;
        try
        {
            classLayout = typeDefinition.GetLayout();
        }
        catch (BadImageFormatException)
        {
            // [WHY] A ClassSize above int.MaxValue is malformed, and GetLayout throws on it. Roslyn's own
            // PEModule.GetTypeLayout catches the same exception; here the struct falls back to the attribute read
            // (StructLayoutReader), which finds none for a metadata type, instead of failing the generator (CS8785)
            return false;
        }

        kind = (typeDefinition.Attributes & System.Reflection.TypeAttributes.LayoutMask) switch
        {
            System.Reflection.TypeAttributes.ExplicitLayout => TypeLayoutKind.Explicit,
            System.Reflection.TypeAttributes.AutoLayout => TypeLayoutKind.Auto,
            _ => TypeLayoutKind.Sequential
        };

        // A packing size above 255 or a negative size is malformed; Roslyn's PEModule.GetTypeLayout drops both
        pack = classLayout.PackingSize <= MaxPackingSize ? classLayout.PackingSize : 0;
        size = Math.Max(classLayout.Size, 0);
#pragma warning restore RS0030
        return true;
    }

    // The largest packing size Roslyn reads from a ClassLayout row; ECMA-335 stores it in two bytes, and Roslyn reads a
    // larger value as none
    private const int MaxPackingSize = 255;

    /// <summary>
    /// Reads the offset of <paramref name="field"/> when it comes from a metadata reference (0 when its FieldLayout row
    /// is missing); returns false for a field that has no metadata.
    /// </summary>
    public static bool TryGetFieldOffset(IFieldSymbol field, out int offset)
    {
        offset = 0;
        if (!TryGetRow(field, out var module, out var token))
        {
            return false;
        }

#pragma warning disable RS0030 // FieldLayout row, which Roslyn's public API does not expose; measured with TryRead: at most 0.4 ms per compilation (#736, class remarks)
        var handle = (System.Reflection.Metadata.FieldDefinitionHandle)
            System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(token);
        try
        {
            offset = Math.Max(module.GetMetadataReader().GetFieldDefinition(handle).GetOffset(), 0);
        }
        catch (BadImageFormatException)
        {
            // [WHY] The same fallback as TryRead: a malformed FieldLayout row is read through the attributes instead
            return false;
        }
#pragma warning restore RS0030
        return true;
    }

    // The module and token of a symbol from a metadata reference. A constructed generic type, and a member of one, shares
    // the row of its definition; a symbol of the compilation or of a compilation reference has no metadata or token 0
    private static bool TryGetRow(ISymbol symbol, [NotNullWhen(true)] out ModuleMetadata? module, out int token)
    {
        var definition = symbol.OriginalDefinition;
        module = definition.ContainingModule?.GetMetadata();
        token = definition.MetadataToken;
        return module is not null && token != 0;
    }
}
