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
    /// <remarks>
    /// The element of an array field is collected too: its shadow carries the array's elements to the device.
    /// </remarks>
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
            var memberType = member.Type.UnwrapNullableArrayLike() ?? member.Type;
            CollectStructs(memberType.TryGetArrayLikeElementType(out var element) ? element : memberType, structs);
        }
    }

    /// <summary>
    /// The C# spelling of <paramref name="type"/> for a diagnostic message (<c>decimal</c>, <c>N.Pair</c>).
    /// </summary>
    public static string FormatTypeName(ITypeSymbol type) =>
#pragma warning disable RS0030 // Message text only (not a hot path); keeps the C# spelling of the type.
        type.ToDisplayString();
#pragma warning restore RS0030

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
            ReadStructLayoutAttribute(namedStruct, out var kind, out var pack, out var explicitSize);
            return ComputeDeviceStructLayout(namedStruct, dialect, kind, pack, explicitSize, fields: null);
        }

        return new TypeLayout(4, 4);
    }

    /// <summary>Calculates the complete device (D), host (H), and transfer classification model for <paramref name="structType"/>.</summary>
    public static StructLayoutInfo GetStructLayout(INamedTypeSymbol structType, ITranspilerDialect dialect)
    {
        ReadStructLayoutAttribute(structType, out var kind, out var pack, out var explicitSize);

        // 1. Device (D)
        var deviceFieldsBuilder = ImmutableArray.CreateBuilder<FieldLayout>();
        var deviceLayout =
            ComputeDeviceStructLayout(structType, dialect, kind, pack, explicitSize, deviceFieldsBuilder);
        var deviceFields = deviceFieldsBuilder.ToImmutable();

        // 2. Host (H)
        var hostLayout = ComputeStructHostLayout(structType, kind, pack, explicitSize);

        // 3. TransferClass
        var unsupported = FindUnsupportedLayout(structType, dialect, kind, deviceFields);
        var transfer = unsupported.HasValue
            ? TransferClass.Unsupported
            : ClassifyTransfer(dialect, deviceLayout, deviceFields, hostLayout);

        return new StructLayoutInfo(kind, pack, deviceLayout, deviceFields, hostLayout, transfer, unsupported);
    }

    /// <summary>
    /// Whether the device has a representation for a member of type <paramref name="type"/>: an array-like value,
    /// a pointer, an enum, a type of the dialect's type map, a user struct, or a <c>Nullable&lt;T&gt;</c> of one.
    /// </summary>
    /// <remarks>
    /// A reference type other than an array-like one is not asked about here; <see cref="GetStructLayout"/> reports it
    /// with its own reason. <c>decimal</c>, <c>DateTime</c> and the other BCL structs without a mapping have none.
    /// </remarks>
    public static bool HasDeviceRepresentation(ITypeSymbol type, ITranspilerDialect dialect)
    {
        type = type.UnwrapNullableArrayLike() ?? type;
        if (type.IsArrayLikeType() ||
            type is IPointerTypeSymbol or IFunctionPointerTypeSymbol or ITypeParameterSymbol ||
            type.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr ||
            type.TypeKind == TypeKind.Enum ||
            dialect.IsMappedType(type))
        {
            return true;
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named is { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 })
        {
            return HasDeviceRepresentation(named.TypeArguments[0], dialect);
        }

        return GlobalCppNames.IsEmittedUserType(named) || named.IsSharedMemoryType() || named.IsWmmaFragmentType() ||
               named.IsWmmaMarkerType();
    }

    /// <summary>
    /// Describes the first difference between the host and device layouts of a <see cref="TransferClass.Convert"/>
    /// struct, as the <c>{1}</c> of GSH0080 (<c>field 'V' is at offset 4 on the host and 8 on the device</c>).
    /// </summary>
    public static string DescribeLayoutDifference(INamedTypeSymbol structType, ITranspilerDialect dialect)
    {
        var layout = GetStructLayout(structType, dialect);
        if (layout.Kind == TypeLayoutKind.Auto)
        {
            return "LayoutKind.Auto lets the runtime reorder the fields on the host";
        }

        if (!layout.Host.IsKnown)
        {
            foreach (var member in StructAbiMembers.Get(structType))
            {
                if (member.Type.IsArrayLikeType() || member.Type.IsNullableArrayLike())
                {
                    return $"field '{member.Name}' is an array, so the runtime may reorder the fields on the host";
                }

                if (member.Type is INamedTypeSymbol { TypeKind: TypeKind.Struct } nested &&
                    !TryGetHostElementLayout(nested, out _))
                {
                    return $"field '{member.Name}' of type '{FormatTypeName(nested)}': " +
                           DescribeLayoutDifference(nested, dialect);
                }
            }

            return "the host layout is not known";
        }

        for (var i = 0; i < layout.DeviceFields.Length && i < layout.Host.Fields.Length; i++)
        {
            var device = layout.DeviceFields[i];
            var host = layout.Host.Fields[i];
            if (device.Offset != host.Offset)
            {
                return $"field '{device.Member.Name}' is at offset {host.Offset} on the host " +
                       $"and {device.Offset} on the device";
            }

            if (device.Layout.Size != host.Layout.Size)
            {
                return $"field '{device.Member.Name}' is {host.Layout.Size} bytes on the host " +
                       $"and {device.Layout.Size} bytes on the device";
            }

            if (device.Member.Type is INamedTypeSymbol { TypeKind: TypeKind.Struct } nested &&
                IsUserStruct(nested) &&
                GetStructLayout(nested, dialect).Transfer != TransferClass.Identical)
            {
                return $"field '{device.Member.Name}' of type '{FormatTypeName(nested)}': " +
                       DescribeLayoutDifference(nested, dialect);
            }
        }

        return $"the struct is {layout.Host.Size} bytes on the host and {layout.Device.Size} bytes on the device";
    }

    /// <summary>
    /// Finds the first array-like member of <paramref name="structType"/>, its nested structs and inline array
    /// elements, as a dotted path (<c>Inner.Values</c>): the arrays that the launcher allocates and copies for each element.
    /// </summary>
    public static bool TryFindArrayField(INamedTypeSymbol structType, out string path)
    {
        foreach (var member in StructAbiMembers.Get(structType))
        {
            if (member.Type.IsArrayLikeType() || member.Type.IsNullableArrayLike())
            {
                path = member.Name;
                return true;
            }

            if (member.Type is INamedTypeSymbol { TypeKind: TypeKind.Struct } nested &&
                IsUserStruct(nested) &&
                TryFindArrayField(nested, out var nestedPath))
            {
                path = member.Kind == StructAbiMemberKind.InlineArrayElement
                    ? nestedPath
                    : $"{member.Name}.{nestedPath}";
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    // Sequential: declaration order, each field aligned up (Pack caps the alignment). Explicit: the [FieldOffset]
    // values, with StructLayout.Size as the minimum size. Either way the size is rounded up to the largest alignment.
    private static TypeLayout ComputeDeviceStructLayout(INamedTypeSymbol structType, ITranspilerDialect dialect,
        TypeLayoutKind kind, int pack, int explicitSize, ImmutableArray<FieldLayout>.Builder? fields)
    {
        var end = 0;
        var maxAlign = 1;
        foreach (var member in StructAbiMembers.Get(structType))
        {
            var elemLayout = GetDeviceLayout(member.Type, dialect);
            var fieldSize = elemLayout.Size * member.ElementCount;
            var effectiveAlign = pack > 0 ? Math.Min(elemLayout.Alignment, pack) : elemLayout.Alignment;
            int offset;
            if (kind == TypeLayoutKind.Explicit)
            {
                offset = GetFieldOffset(member.Symbol);
                end = Math.Max(end, offset + fieldSize);
            }
            else
            {
                offset = AlignUp(end, effectiveAlign);
                end = offset + fieldSize;
            }

            maxAlign = Math.Max(maxAlign, effectiveAlign);
            fields?.Add(new FieldLayout(member, offset, new TypeLayout(fieldSize, effectiveAlign)));
        }

        if (kind == TypeLayoutKind.Explicit && explicitSize > 0)
        {
            end = Math.Max(end, explicitSize);
        }

        return new TypeLayout(Math.Max(AlignUp(end, maxAlign), 1), maxAlign);
    }

    // [WHY] The device can express an explicit layout only as union groups (fields at the same offset) that do not
    // overlap each other, each at an offset its alignment allows (design §3.3-4); a member must be a value the device
    // has a type for. A nested struct passes its own reason up.
    private static UnsupportedLayout? FindUnsupportedLayout(INamedTypeSymbol structType, ITranspilerDialect dialect,
        TypeLayoutKind kind, ImmutableArray<FieldLayout> deviceFields)
    {
        if (kind == TypeLayoutKind.Explicit)
        {
            for (var i = 0; i < deviceFields.Length; i++)
            {
                var field = deviceFields[i];
                if (field.Offset % field.Layout.Alignment != 0)
                {
                    return new UnsupportedLayout(
                        $"field '{field.Member.Name}' at offset {field.Offset} is not a multiple of its " +
                        $"{field.Layout.Alignment}-byte device alignment",
                        null,
                        null);
                }

                for (var j = 0; j < i; j++)
                {
                    var earlier = deviceFields[j];
                    if (earlier.Offset != field.Offset &&
                        earlier.Offset < field.Offset + field.Layout.Size &&
                        field.Offset < earlier.Offset + earlier.Layout.Size)
                    {
                        return new UnsupportedLayout(
                            $"field '{field.Member.Name}' at offset {field.Offset} partially overlaps " +
                            $"field '{earlier.Member.Name}'",
                            null,
                            null);
                    }
                }
            }
        }

        foreach (var member in StructAbiMembers.Get(structType))
        {
            var type = member.Type.UnwrapNullableArrayLike() ?? member.Type;
            if (type.IsArrayLikeType())
            {
                continue;
            }

            if (type.IsReferenceType)
            {
                return new UnsupportedLayout(
                    $"field '{member.Name}' has the reference type '{FormatTypeName(type)}'",
                    null,
                    null);
            }

            if (!HasDeviceRepresentation(type, dialect))
            {
                return new UnsupportedLayout(
                    $"field '{member.Name}' has the type '{FormatTypeName(type)}', which has no device representation",
                    member.Name,
                    type);
            }

            if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct } nested &&
                IsUserStruct(nested) &&
                GetStructLayout(nested, dialect).Unsupported is { } nestedProblem)
            {
                var path = member.Kind == StructAbiMemberKind.InlineArrayElement || nestedProblem.FieldPath is null
                    ? nestedProblem.FieldPath
                    : $"{member.Name}.{nestedProblem.FieldPath}";
                return new UnsupportedLayout(
                    $"field '{member.Name}' of type '{FormatTypeName(nested)}': {nestedProblem.Reason}",
                    path,
                    nestedProblem.FieldType);
            }
        }

        return null;
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
        ITranspilerDialect dialect,
        TypeLayout deviceLayout,
        ImmutableArray<FieldLayout> deviceFields,
        HostLayout hostLayout)
    {
        if (!hostLayout.IsKnown)
        {
            return TransferClass.Convert;
        }

        // [WHY] Only sizes and offsets decide (design §3.3): with the same stride and offsets the bytes are the same,
        // and the device alignment (Float2 / Vector4 ...) is met by the allocation and the stride, not by the host
        if (deviceLayout.Size != hostLayout.Size || deviceFields.Length != hostLayout.Fields.Length)
        {
            return TransferClass.Convert;
        }

        for (var i = 0; i < deviceFields.Length; i++)
        {
            var dField = deviceFields[i];
            var hField = hostLayout.Fields[i];
            if (dField.Offset != hField.Offset || dField.Layout.Size != hField.Layout.Size)
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
            if (!TryGetHostElementLayout(nullableType.TypeArguments[0], out var elemH) || elemD.Size != elemH.Size)
            {
                return false;
            }

            return IsMemberTransferIdentical(nullableType.TypeArguments[0], dialect);
        }

        if (dialect.TryGetTypeLayout(memberType, out var mappedD))
        {
            return TryGetHostElementLayout(memberType, out var mappedH) && mappedD.Size == mappedH.Size;
        }

        if (memberType is INamedTypeSymbol { TypeKind: TypeKind.Struct } nestedStruct)
        {
            return GetStructLayout(nestedStruct, dialect).Transfer == TransferClass.Identical;
        }

        return true;
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

    // [field: FieldOffset] on an auto-property sits on its backing field, not on the property
    private static int GetFieldOffset(ISymbol memberSymbol)
    {
        var fieldSymbol = memberSymbol is IPropertySymbol property
            ? property.ContainingType.GetMembers()
                .AsStackEnumerable()
                .FirstOrDefault(property, static (m, p) => m is IFieldSymbol f && f.AssociatedSymbol.IsEqualTo(p))
            : memberSymbol;
        if (fieldSymbol?.GetAttribute(KnownAttributes.FieldOffset) is { ConstructorArguments.Length: > 0 } attr &&
            attr.ConstructorArguments[0].Value is int offset and >= 0)
        {
            return offset;
        }

        return 0;
    }
}
