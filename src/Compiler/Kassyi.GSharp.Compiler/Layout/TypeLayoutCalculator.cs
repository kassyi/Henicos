namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>
/// Single source of truth for struct and type layout calculations across Device (D), Host (H), and TransferClass.
/// </summary>
internal static class TypeLayoutCalculator
{
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
            StructLayoutReader.Read(namedStruct, out var kind, out var pack, out var explicitSize);
            return ComputeDeviceStructLayout(namedStruct, dialect, kind, pack, explicitSize, fields: null);
        }

        return new TypeLayout(4, 4);
    }


    /// <summary>Calculates the complete device (D), host (H), and transfer classification model for <paramref name="structType"/>.</summary>
    public static StructLayoutInfo GetStructLayout(INamedTypeSymbol structType, ITranspilerDialect dialect)
    {
        StructLayoutReader.Read(structType, out var kind, out var pack, out var explicitSize);

        // 1. Device (D)
        var deviceFieldsBuilder = ImmutableArray.CreateBuilder<FieldLayout>();
        var deviceLayout =
            ComputeDeviceStructLayout(structType, dialect, kind, pack, explicitSize, deviceFieldsBuilder);
        var deviceFields = MarkCopiedFields(deviceFieldsBuilder.ToImmutable(), kind);

        // 2. Host (H)
        var hostLayout = HostLayoutCalculator.ComputeStructHostLayout(structType, kind, pack, explicitSize);

        // 3. TransferClass
        var unsupported = StructLayoutValidator.FindUnsupportedLayout(structType, dialect, kind, explicitSize, deviceFields);
        var transfer = unsupported.HasValue
            ? TransferClass.Unsupported
            : TransferClassifier.ClassifyTransfer(dialect, deviceLayout, deviceFields, hostLayout);

        return new StructLayoutInfo(kind, pack, deviceLayout, deviceFields, hostLayout, transfer, unsupported);
    }


    // Sequential: declaration order, each field aligned up (Pack caps the alignment). Explicit: the [FieldOffset]
    // values. StructLayout.Size, of any layout kind, is the minimum size. Either way the size is rounded up to the
    // largest alignment, as C++ sizeof is a multiple of alignof.
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
                offset = StructLayoutReader.GetFieldOffset(member.StorageField);
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

        if (explicitSize > 0)
        {
            end = Math.Max(end, explicitSize);
        }

        return new TypeLayout(Math.Max(AlignUp(end, maxAlign), 1), maxAlign);
    }

    // [WHY] The device can express an explicit layout only as union groups (fields at the same offset) that do not

    private static ImmutableArray<FieldLayout> MarkCopiedFields(ImmutableArray<FieldLayout> fields, TypeLayoutKind kind)
    {
        if (kind != TypeLayoutKind.Explicit || fields.Length <= 1)
        {
            return fields;
        }

        var builder = fields.ToBuilder();
        var processedOffsets = new HashSet<int>();

        for (var i = 0; i < builder.Count; i++)
        {
            var offset = builder[i].Offset;
            if (!processedOffsets.Add(offset))
            {
                continue;
            }

            var bestIdx = i;
            var bestSize = builder[i].Layout.Size;
            var bestIsFp = IsFloatingPoint(builder[i].Member.Type);

            for (var j = i + 1; j < builder.Count; j++)
            {
                if (builder[j].Offset != offset)
                {
                    continue;
                }

                var jSize = builder[j].Layout.Size;
                var jIsFp = IsFloatingPoint(builder[j].Member.Type);

                // [WHY] Prefer larger arm to copy more bytes. If sizes are equal, prefer non-floating-point (int/bytes)
                // arm because float/double bit patterns can have trap representations or normalize NaNs, whereas
                // integer types faithfully preserve arbitrary bit patterns without loss or side-effects.
                if (jSize > bestSize || (jSize == bestSize && bestIsFp && !jIsFp))
                {
                    bestIdx = j;
                    bestSize = jSize;
                    bestIsFp = jIsFp;
                }
            }

            for (var j = i; j < builder.Count; j++)
            {
                if (builder[j].Offset == offset)
                {
                    builder[j] = builder[j] with { IsCopied = (j == bestIdx) };
                }
            }
        }

        return builder.ToImmutable();
    }

    private static bool IsFloatingPoint(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Single or SpecialType.System_Double;

}
