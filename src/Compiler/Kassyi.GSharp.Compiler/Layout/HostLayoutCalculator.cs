namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>Calculates the size, alignment, and field offsets of types in host CLR memory.</summary>
internal static class HostLayoutCalculator
{

    internal static HostLayout ComputeStructHostLayout(INamedTypeSymbol structType, TypeLayoutKind kind, int pack,
        int explicitSize)
    {
        if (kind == TypeLayoutKind.Auto)
        {
            return new HostLayout(false, 0, 0, ImmutableArray<FieldLayout>.Empty);
        }

        var hostFieldsBuilder = ImmutableArray.CreateBuilder<FieldLayout>();
        var hostEnd = 0;
        var hostMaxAlign = 1;

        foreach (var member in StructAbiMembers.Get(structType))
        {
            if (!TryGetHostLayout(member.Type, out var elemHost))
            {
                return new HostLayout(false, 0, 0, ImmutableArray<FieldLayout>.Empty);
            }

            var fieldSize = elemHost.Size * member.ElementCount;
            var effectiveAlign = pack > 0 ? Math.Min(elemHost.Alignment, pack) : elemHost.Alignment;

            int offset;
            if (kind == TypeLayoutKind.Explicit)
            {
                offset = StructLayoutReader.GetFieldOffset(member.StorageField);
                hostEnd = Math.Max(hostEnd, offset + fieldSize);
            }
            else
            {
                offset = TypeLayoutCalculator.AlignUp(hostEnd, effectiveAlign);
                hostEnd = offset + fieldSize;
            }

            hostMaxAlign = Math.Max(hostMaxAlign, effectiveAlign);
            hostFieldsBuilder.Add(new FieldLayout(member, offset, new TypeLayout(fieldSize, effectiveAlign)));
        }

        // [WHY] The CLR takes max(StructLayout.Size, end of the last field) as it is, of any layout kind, and rounds up
        // to the alignment only when no Size is given (EEClassLayoutInfo, CalculateSizeWithMetadataSize / AlignSize in
        // dotnet/runtime classlayoutinfo.cpp): [StructLayout(Size = 6)] with one int is 6 bytes, with a 6-byte stride
        var hostSize = explicitSize > 0
            ? Math.Max(explicitSize, hostEnd)
            : Math.Max(TypeLayoutCalculator.AlignUp(hostEnd, hostMaxAlign), 1);
        return new HostLayout(true, hostSize, hostMaxAlign, hostFieldsBuilder.ToImmutable());
    }


    /// <summary>
    /// The size and alignment of <paramref name="type"/> in host CLR memory; false when the CLR decides it at run time
    /// (a reference, an array, or a struct with one of them or with <c>LayoutKind.Auto</c>).
    /// </summary>
    /// <remarks>
    /// A struct is laid out from the fields Roslyn shows. A BCL struct read from a reference assembly shows none of its
    /// private fields, so the answer is wrong for it (<c>decimal</c> and <c>DateTime</c> come out as 1 byte); such a
    /// struct has no device representation, so a layout that holds one is reported before this size is used.
    /// </remarks>
    public static bool TryGetHostLayout(ITypeSymbol type, out TypeLayout layout)
    {
        if (type.IsReferenceType || type.IsArrayLikeType() || type.IsNullableArrayLike())
        {
            layout = default;
            return false;
        }

        if (type is IPointerTypeSymbol or IFunctionPointerTypeSymbol ||
            type.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr)
        {
            layout = HostLeafLayouts.Pointer;
            return true;
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } enumUnderlyingType })
        {
            return TryGetHostLayout(enumUnderlyingType, out layout);
        }

        if (HostLeafLayouts.TryGet(type, out layout))
        {
            return true;
        }

        if (type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1
            } nullableType)
        {
            if (!TryGetHostLayout(nullableType.TypeArguments[0], out var valLayout))
            {
                layout = default;
                return false;
            }

            var valOffset = TypeLayoutCalculator.AlignUp(1, valLayout.Alignment);
            var size = TypeLayoutCalculator.AlignUp(valOffset + valLayout.Size, valLayout.Alignment);
            layout = new TypeLayout(size, valLayout.Alignment);
            return true;
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct } namedStruct)
        {
            StructLayoutReader.Read(namedStruct, out var kind, out var pack, out var explicitSize);
            var structHost = ComputeStructHostLayout(namedStruct, kind, pack, explicitSize);
            if (!structHost.IsKnown)
            {
                layout = default;
                return false;
            }

            layout = new TypeLayout(structHost.Size, structHost.Alignment);
            return true;
        }

        layout = default;
        return false;
    }

}
