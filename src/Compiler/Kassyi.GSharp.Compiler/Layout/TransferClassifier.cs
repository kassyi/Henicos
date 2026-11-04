namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>Classifies how a C# struct transfers between host and device memory.</summary>
public static class TransferClassifier
{
    /// <summary>
    /// Describes the first difference between the host and device layouts of a <see cref="TransferClass.Convert"/>
    /// struct, as the <c>{1}</c> of GSH0080 (<c>field 'V' is at offset 4 on the host and 8 on the device</c>).
    /// </summary>
    public static string DescribeLayoutDifference(INamedTypeSymbol structType, ITranspilerDialect dialect)
    {
        var layout = TypeLayoutCalculator.GetStructLayout(structType, dialect);
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
                    !HostLayoutCalculator.TryGetHostLayout(nested, out _))
                {
                    return $"field '{member.Name}' of type '{TypeLayoutCalculator.FormatTypeName(nested)}': " +
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
                TypeLayoutCalculator.IsUserStruct(nested) &&
                TypeLayoutCalculator.GetStructLayout(nested, dialect).Transfer != TransferClass.Identical)
            {
                return $"field '{device.Member.Name}' of type '{TypeLayoutCalculator.FormatTypeName(nested)}': " +
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
                TypeLayoutCalculator.IsUserStruct(nested) &&
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


    internal static TransferClass ClassifyTransfer(
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


    internal static bool IsMemberTransferIdentical(ITypeSymbol memberType, ITranspilerDialect dialect)
    {
        if (memberType is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1
            } nullableType)
        {
            var elemD = TypeLayoutCalculator.GetDeviceLayout(nullableType.TypeArguments[0], dialect);
            if (!HostLayoutCalculator.TryGetHostLayout(nullableType.TypeArguments[0], out var elemH) || elemD.Size != elemH.Size)
            {
                return false;
            }

            return IsMemberTransferIdentical(nullableType.TypeArguments[0], dialect);
        }

        if (dialect.TryGetTypeLayout(memberType, out var mappedD))
        {
            return HostLayoutCalculator.TryGetHostLayout(memberType, out var mappedH) && mappedD.Size == mappedH.Size;
        }

        if (memberType is INamedTypeSymbol { TypeKind: TypeKind.Struct } nestedStruct)
        {
            return TypeLayoutCalculator.GetStructLayout(nestedStruct, dialect).Transfer == TransferClass.Identical;
        }

        return true;
    }

}
