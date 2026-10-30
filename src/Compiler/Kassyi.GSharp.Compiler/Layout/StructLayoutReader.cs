namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>
/// Reads <c>[StructLayout]</c> (kind, <c>Pack</c>, <c>Size</c>) of a struct and <c>[FieldOffset]</c> of its fields,
/// whether the struct is declared in source or comes from a referenced assembly.
/// </summary>
/// <remarks>
/// [WHY] Both are pseudo-custom attributes. For a type of the compilation (or of a compilation reference), Roslyn
/// returns them from <c>GetAttributes()</c>; for a type of a metadata reference it returns neither, because the C#
/// compiler stores them in the TypeDef flags and the ClassLayout and FieldLayout tables instead of the custom attribute
/// table. Those types are read by <see cref="MetadataStructLayoutReader"/> (#736).
/// </remarks>
internal static class StructLayoutReader
{
    /// <summary>
    /// The layout kind, <c>Pack</c> and <c>Size</c> of <paramref name="structType"/>: <see cref="TypeLayoutKind.Sequential"/>,
    /// 0 and 0 when it states none.
    /// </summary>
    public static void Read(INamedTypeSymbol structType, out TypeLayoutKind kind, out int pack, out int size)
    {
        if (MetadataStructLayoutReader.TryRead(structType, out kind, out pack, out size))
        {
            return;
        }

        ReadAttribute(structType, out kind, out pack, out size);
    }

    /// <summary>Gets a positive <c>Pack</c> of <paramref name="structType"/>; returns false when it has none.</summary>
    public static bool TryGetPack(INamedTypeSymbol structType, out int pack)
    {
        Read(structType, out _, out pack, out _);
        return pack > 0;
    }

    /// <summary>Gets a positive <c>Size</c> of <paramref name="structType"/>; returns false when it has none.</summary>
    public static bool TryGetSize(INamedTypeSymbol structType, out int size)
    {
        Read(structType, out _, out _, out size);
        return size > 0;
    }

    /// <summary>
    /// The <c>[FieldOffset]</c> of a member of a <see cref="TypeLayoutKind.Explicit"/> struct, read from the field that
    /// holds it (<see cref="StructAbiMember.StorageField"/>), or 0 when it has none.
    /// </summary>
    /// <remarks>
    /// Ask only for a member of an explicit struct: a sequential one has no offset to read, so the metadata path does
    /// not look up its FieldLayout row. <c>[field: FieldOffset]</c> on an auto-property sits on its backing field.
    /// </remarks>
    public static int GetFieldOffset(IFieldSymbol? field)
    {
        if (field is null)
        {
            return 0;
        }

        if (MetadataStructLayoutReader.TryGetFieldOffset(field, out var metadataOffset))
        {
            return metadataOffset;
        }

        return field.GetAttribute(KnownAttributes.FieldOffset) is { ConstructorArguments.Length: > 0 } attribute &&
               attribute.ConstructorArguments[0].Value is int offset and >= 0
            ? offset
            : 0;
    }

    private static void ReadAttribute(INamedTypeSymbol structType, out TypeLayoutKind kind, out int pack, out int size)
    {
        kind = TypeLayoutKind.Sequential;
        pack = 0;
        size = 0;

        // Only the first [StructLayout] counts
        if (!structType.GetAttributes()
                .AsStackEnumerable()
                .TryFirst(static attr => attr.IsStructLayoutAttribute(), out var layoutAttr))
        {
            return;
        }

        if (layoutAttr.ConstructorArguments is { Length: > 0 } ctorArgs &&
            ctorArgs[0].Value is { } rawKind)
        {
            var kindValue = rawKind switch
            {
                int i => i,
                short s => s,
                _ => 0
            };

            // System.Runtime.InteropServices.LayoutKind: Sequential = 0, Explicit = 2, Auto = 3
            kind = kindValue switch
            {
                2 => TypeLayoutKind.Explicit,
                3 => TypeLayoutKind.Auto,
                _ => TypeLayoutKind.Sequential
            };
        }

        foreach (var namedArg in layoutAttr.NamedArguments)
        {
            if (namedArg is { Key: "Pack", Value.Value: int p and > 0 })
            {
                pack = p;
            }
            else if (namedArg is { Key: "Size", Value.Value: int s and > 0 })
            {
                size = s;
            }
        }
    }
}
