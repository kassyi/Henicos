namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>Validates whether a struct or type can be expressed and transferred on the GPU device.</summary>
public static class StructLayoutValidator
{

    /// <summary>
    /// Whether the device has a representation for a member of type <paramref name="type"/>: an array-like value,
    /// a pointer, an enum, a type of the dialect's type map, a user struct, or a <c>Nullable&lt;T&gt;</c> of one.
    /// </summary>
    /// <remarks>
    /// A reference type other than an array-like one is not asked about here; <see cref="TypeLayoutCalculator.GetStructLayout"/> reports it
    /// with its own reason. <c>decimal</c>, <c>DateTime</c> and the other BCL structs without a mapping have none.
    /// </remarks>
    public static bool HasDeviceRepresentation(ITypeSymbol type, ITranspilerDialect dialect)
    {
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

    // [WHY] The device can express an explicit layout only as union groups (fields at the same offset) that do not
    // overlap each other, each at an offset its alignment allows (design §3.3-4); a member must be a value the device
    // has a type for. A nested struct passes its own reason up.
    internal static UnsupportedLayout? FindUnsupportedLayout(INamedTypeSymbol structType, ITranspilerDialect dialect,
        TypeLayoutKind kind, int explicitSize, ImmutableArray<FieldLayout> deviceFields)
    {
        // [WHY] C# accepts StructLayout.Size on an [InlineArray] struct, but the runtime refuses to load the type
        // (TypeLoadException), so there is no host layout to follow
        if (explicitSize > 0 && structType.GetAttribute(KnownAttributes.InlineArray) is not null)
        {
            return new UnsupportedLayout(
                "an [InlineArray] struct cannot have StructLayout.Size; the runtime does not load it",
                null,
                null);
        }

        if (kind == TypeLayoutKind.Explicit && FindUnsupportedExplicitPlacement(deviceFields) is { } placementProblem)
        {
            return placementProblem;
        }

        foreach (var member in StructAbiMembers.Get(structType))
        {
            if (member.Kind == StructAbiMemberKind.UnknownHiddenField)
            {
                return new UnsupportedLayout(
                    $"field '{member.Name}' is a compiler-generated field that G# does not lay out",
                    null,
                    null);
            }

            var type = member.Type;
            if (type.IsArrayLikeType() || type.IsNullableArrayLike())
            {
                continue;
            }

            if (type.IsReferenceType)
            {
                return new UnsupportedLayout(
                    $"field '{member.Name}' has the reference type '{TypeLayoutCalculator.FormatTypeName(type)}'",
                    null,
                    null);
            }

            if (!HasDeviceRepresentation(type, dialect))
            {
                return new UnsupportedLayout(
                    $"field '{member.Name}' has the type '{TypeLayoutCalculator.FormatTypeName(type)}', which has no device representation",
                    member.Name,
                    type);
            }

            if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct } nested &&
                TypeLayoutCalculator.IsUserStruct(nested) &&
                TypeLayoutCalculator.GetStructLayout(nested, dialect).Unsupported is { } nestedProblem)
            {
                var path = member.Kind == StructAbiMemberKind.InlineArrayElement || nestedProblem.FieldPath is null
                    ? nestedProblem.FieldPath
                    : $"{member.Name}.{nestedProblem.FieldPath}";
                return new UnsupportedLayout(
                    $"field '{member.Name}' of type '{TypeLayoutCalculator.FormatTypeName(nested)}': {nestedProblem.Reason}",
                    path,
                    nestedProblem.FieldType);
            }
        }

        // [WHY] After the member checks: a union arm with a reference type, a type without a device representation or
        // an unsupported nested struct is reported for that member (GSH0078 / GSH0079 with the field), not as an arm
        // whose host and device layouts differ
        return kind == TypeLayoutKind.Explicit ? FindUnsupportedUnionArm(deviceFields, dialect) : null;
    }

    // A field at an offset its device alignment does not allow, or two fields that overlap without starting at the
    // same offset
    private static UnsupportedLayout? FindUnsupportedExplicitPlacement(ImmutableArray<FieldLayout> deviceFields)
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

        return null;
    }

    // Two fields at the same offset (a union group) where one of them has different host and device layouts
    private static UnsupportedLayout? FindUnsupportedUnionArm(ImmutableArray<FieldLayout> deviceFields, ITranspilerDialect dialect)
    {
        for (var i = 0; i < deviceFields.Length; i++)
        {
            var field = deviceFields[i];
            for (var j = 0; j < i; j++)
            {
                var earlier = deviceFields[j];
                if (earlier.Offset == field.Offset &&
                    (!IsUnionArmBlittable(earlier, dialect) || !IsUnionArmBlittable(field, dialect)))
                {
                    var badMember = !IsUnionArmBlittable(field, dialect) ? field.Member : earlier.Member;
                    return new UnsupportedLayout(
                        $"field '{field.Member.Name}' at offset {field.Offset} overlaps field '{earlier.Member.Name}', " +
                        $"and '{badMember.Name}' has different host and device layouts",
                        null,
                        null);
                }
            }
        }

        return null;
    }

    private static bool IsUnionArmBlittable(FieldLayout field, ITranspilerDialect dialect) =>
        !field.Member.Type.IsArrayLikeType() &&
        !field.Member.Type.IsNullableArrayLike() &&
        HostLayoutCalculator.TryGetHostLayout(field.Member.Type, out var elemHost) &&
        elemHost.Size * field.Member.ElementCount == field.Layout.Size &&
        TransferClassifier.IsMemberTransferIdentical(field.Member.Type, dialect);

}
