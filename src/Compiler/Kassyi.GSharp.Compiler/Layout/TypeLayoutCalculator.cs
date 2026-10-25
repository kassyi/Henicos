namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>
/// Single source of truth for struct and type layout calculations across Device (D), Host (H), and TransferClass.
/// </summary>
internal static class TypeLayoutCalculator
{
    private static readonly ITranspilerDialect s_hostDialect = new StandardCppDialect();

    /// <summary>Determines whether the type requires custom native struct generation on the host.</summary>
    /// <remarks>
    /// This is the host marshaling classification (native shadow structs), not the device one: unlike
    /// <see cref="Transpiler.GlobalCppNames.IsEmittedUserType"/> it leaves out array-like structs and Core types.
    /// </remarks>
    public static bool IsUserStruct(ITypeSymbol type)
    {
        // [WHY] Array-like structs (ImmutableArray<T> in System.Collections.Immutable) are marshaled as GSharpArray, not as native shadow structs.
        return type is INamedTypeSymbol { TypeKind: TypeKind.Struct } named &&
               !named.IsArrayLikeType() &&
               !named.ContainingNamespace.IsSystemOrNumerics() &&
               !named.ContainingNamespace.IsCoreNamespace();
    }

    /// <summary>Recursively collects all user structs referenced within the type tree.</summary>
    public static void CollectStructs(ITypeSymbol type, HashSet<INamedTypeSymbol> structs)
    {
        if (!IsUserStruct(type))
        {
            return;
        }

        var named = (INamedTypeSymbol)type;

        if (!structs.Add(named))
        {
            return;
        }

        foreach (var member in StructAbiMembers.Get(named))
        {
            CollectStructs(member.Type, structs);
        }
    }

    /// <summary>Aligns a given byte offset up to the specified boundary.</summary>
    public static int AlignUp(int offset, int alignment)
    {
        if (alignment <= 1)
        {
            return offset;
        }

        var remainder = offset % alignment;
        return remainder == 0 ? offset : offset + (alignment - remainder);
    }

    /// <summary>Calculates the device size and alignment requirements for <paramref name="type"/> under <paramref name="dialect"/>.</summary>
    public static TypeLayout GetDeviceLayout(ITypeSymbol type, ITranspilerDialect dialect)
    {
        type = type.UnwrapNullableArrayLike() ?? type;

        if (type.IsArrayLikeType())
        {
            return dialect.ArrayViewLayout;
        }

        if (type is IPointerTypeSymbol or IFunctionPointerTypeSymbol ||
            type.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr)
        {
            return dialect.PointerLayout;
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } enumUnderlyingType })
        {
            return GetDeviceLayout(enumUnderlyingType, dialect);
        }

        if (dialect.TryGetTypeLayout(type, out var mappedLayout))
        {
            return mappedLayout;
        }

        if (type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1
            } nullableType)
        {
            var valLayout = GetDeviceLayout(nullableType.TypeArguments[0], dialect);
            var valOffset = AlignUp(1, valLayout.Alignment);
            var size = AlignUp(valOffset + valLayout.Size, valLayout.Alignment);
            return new TypeLayout(size, valLayout.Alignment);
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct } namedStruct)
        {
            var pack = namedStruct.TryGetStructLayoutPack(out var p) ? p : 0;
            var end = 0;
            var maxAlign = 1;
            foreach (var member in StructAbiMembers.Get(namedStruct))
            {
                var elemLayout = GetDeviceLayout(member.Type, dialect);
                var fieldSize = elemLayout.Size * member.ElementCount;
                var effectiveAlign = pack > 0 ? Math.Min(elemLayout.Alignment, pack) : elemLayout.Alignment;
                var offset = AlignUp(end, effectiveAlign);
                end = offset + fieldSize;
                maxAlign = Math.Max(maxAlign, effectiveAlign);
            }

            return new TypeLayout(Math.Max(AlignUp(end, maxAlign), 1), maxAlign);
        }

        return new TypeLayout(4, 4);
    }

    /// <summary>Calculates the complete device (D), host (H), and transfer classification model for <paramref name="structType"/>.</summary>
    public static StructLayoutInfo GetStructLayout(INamedTypeSymbol structType, ITranspilerDialect dialect)
    {
        ReadStructLayoutAttribute(structType, out var kind, out var pack, out var explicitSize);

        // 1. Device (D)
        var deviceFieldsBuilder = ImmutableArray.CreateBuilder<FieldLayout>();
        var deviceEnd = 0;
        var deviceMaxAlign = 1;
        foreach (var member in StructAbiMembers.Get(structType))
        {
            var elemLayout = GetDeviceLayout(member.Type, dialect);
            var fieldSize = elemLayout.Size * member.ElementCount;
            var effectiveAlign = pack > 0 ? Math.Min(elemLayout.Alignment, pack) : elemLayout.Alignment;
            var offset = AlignUp(deviceEnd, effectiveAlign);
            deviceEnd = offset + fieldSize;
            deviceMaxAlign = Math.Max(deviceMaxAlign, effectiveAlign);
            deviceFieldsBuilder.Add(new FieldLayout(member, offset, new TypeLayout(fieldSize, effectiveAlign)));
        }

        var deviceLayout = new TypeLayout(Math.Max(AlignUp(deviceEnd, deviceMaxAlign), 1), deviceMaxAlign);
        var deviceFields = deviceFieldsBuilder.ToImmutable();

        // 2. Host (H)
        var hostLayout = ComputeStructHostLayout(structType, kind, pack, explicitSize);

        // 3. TransferClass
        var transfer = ClassifyTransfer(structType, dialect, deviceLayout, deviceFields, hostLayout);

        return new StructLayoutInfo(kind, pack, deviceLayout, deviceFields, hostLayout, transfer);
    }

    private static HostLayout ComputeStructHostLayout(INamedTypeSymbol structType, TypeLayoutKind kind, int pack,
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
            if (!TryGetHostElementLayout(member.Type, out var elemHost))
            {
                return new HostLayout(false, 0, 0, ImmutableArray<FieldLayout>.Empty);
            }

            var fieldSize = elemHost.Size * member.ElementCount;
            var effectiveAlign = pack > 0 ? Math.Min(elemHost.Alignment, pack) : elemHost.Alignment;

            int offset;
            if (kind == TypeLayoutKind.Explicit)
            {
                offset = GetFieldOffset(member.Symbol);
                hostEnd = Math.Max(hostEnd, offset + fieldSize);
            }
            else
            {
                offset = AlignUp(hostEnd, effectiveAlign);
                hostEnd = offset + fieldSize;
            }

            hostMaxAlign = Math.Max(hostMaxAlign, effectiveAlign);
            hostFieldsBuilder.Add(new FieldLayout(member, offset, new TypeLayout(fieldSize, effectiveAlign)));
        }

        if (kind == TypeLayoutKind.Explicit && explicitSize > 0)
        {
            hostEnd = Math.Max(hostEnd, explicitSize);
        }

        var hostSize = Math.Max(AlignUp(hostEnd, hostMaxAlign), 1);
        return new HostLayout(true, hostSize, hostMaxAlign, hostFieldsBuilder.ToImmutable());
    }

    private static bool TryGetHostElementLayout(ITypeSymbol type, out TypeLayout layout)
    {
        if (type.IsReferenceType || type.IsArrayLikeType() || type.IsNullableArrayLike())
        {
            layout = default;
            return false;
        }

        if (type is IPointerTypeSymbol or IFunctionPointerTypeSymbol ||
            type.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr)
        {
            layout = s_hostDialect.PointerLayout;
            return true;
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } enumUnderlyingType })
        {
            return TryGetHostElementLayout(enumUnderlyingType, out layout);
        }

        if (s_hostDialect.TryGetTypeLayout(type, out layout))
        {
            return true;
        }

        if (type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1
            } nullableType)
        {
            if (!TryGetHostElementLayout(nullableType.TypeArguments[0], out var valLayout))
            {
                layout = default;
                return false;
            }

            var valOffset = AlignUp(1, valLayout.Alignment);
            var size = AlignUp(valOffset + valLayout.Size, valLayout.Alignment);
            layout = new TypeLayout(size, valLayout.Alignment);
            return true;
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct } namedStruct)
        {
            ReadStructLayoutAttribute(namedStruct, out var kind, out var pack, out var explicitSize);
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

    private static TransferClass ClassifyTransfer(
        INamedTypeSymbol structType,
        ITranspilerDialect dialect,
        TypeLayout deviceLayout,
        ImmutableArray<FieldLayout> deviceFields,
        HostLayout hostLayout)
    {
        if (HasUnsupportedInlineArray(structType, depth: 0))
        {
            return TransferClass.Unsupported;
        }

        if (!hostLayout.IsKnown)
        {
            return TransferClass.Convert;
        }

        if (deviceLayout.Size != hostLayout.Size ||
            deviceLayout.Alignment != hostLayout.Alignment ||
            deviceFields.Length != hostLayout.Fields.Length)
        {
            return TransferClass.Convert;
        }

        for (var i = 0; i < deviceFields.Length; i++)
        {
            var dField = deviceFields[i];
            var hField = hostLayout.Fields[i];
            if (dField.Offset != hField.Offset || dField.Layout != hField.Layout)
            {
                return TransferClass.Convert;
            }

            if (!IsMemberTransferIdentical(dField.Member.Type, dialect))
            {
                return TransferClass.Convert;
            }
        }

        return TransferClass.Identical;
    }

    private static bool IsMemberTransferIdentical(ITypeSymbol memberType, ITranspilerDialect dialect)
    {
        if (memberType is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1
            } nullableType)
        {
            var elemD = GetDeviceLayout(nullableType.TypeArguments[0], dialect);
            if (!TryGetHostElementLayout(nullableType.TypeArguments[0], out var elemH) || elemD != elemH)
            {
                return false;
            }

            return IsMemberTransferIdentical(nullableType.TypeArguments[0], dialect);
        }

        if (dialect.TryGetTypeLayout(memberType, out var mappedD))
        {
            return TryGetHostElementLayout(memberType, out var mappedH) && mappedD == mappedH;
        }

        if (memberType is INamedTypeSymbol { TypeKind: TypeKind.Struct } nestedStruct)
        {
            return GetStructLayout(nestedStruct, dialect).Transfer == TransferClass.Identical;
        }

        return true;
    }

    private static bool HasUnsupportedInlineArray(INamedTypeSymbol structType, int depth)
    {
        const int MaxDepth = 16;
        if (depth > MaxDepth)
        {
            return false;
        }

        foreach (var member in StructAbiMembers.Get(structType))
        {
            if (member.Kind == StructAbiMemberKind.InlineArrayElement &&
                ContainsReferenceOrArray(member.Type, depth + 1))
            {
                return true;
            }

            if (member.Type is INamedTypeSymbol { TypeKind: TypeKind.Struct } nestedStruct &&
                HasUnsupportedInlineArray(nestedStruct, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsReferenceOrArray(ITypeSymbol type, int depth)
    {
        const int MaxDepth = 16;
        if (depth > MaxDepth)
        {
            return false;
        }

        if (type.IsReferenceType || type.IsArrayLikeType() || type.IsNullableArrayLike())
        {
            return true;
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct } structType)
        {
            foreach (var member in StructAbiMembers.Get(structType))
            {
                if (ContainsReferenceOrArray(member.Type, depth + 1))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void ReadStructLayoutAttribute(INamedTypeSymbol structType, out TypeLayoutKind kind, out int pack,
        out int size)
    {
        kind = TypeLayoutKind.Sequential;
        pack = 0;
        size = 0;

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

    private static int GetFieldOffset(ISymbol memberSymbol)
    {
        if (memberSymbol.GetAttribute(KnownAttributes.FieldOffset) is { ConstructorArguments.Length: > 0 } attr &&
            attr.ConstructorArguments[0].Value is int offset and >= 0)
        {
            return offset;
        }

        return 0;
    }
}
