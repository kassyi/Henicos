namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>Classifies how a C# struct transfers between host and device memory.</summary>
public static class TransferClassifier
{
    /// <summary>
    /// Describes the first difference between the host and device layouts of a <see cref="TransferClass.Convert"/>
    /// struct or of a converted <c>Nullable&lt;T&gt;</c>, as the <c>{1}</c> of GSH0080 (<c>field 'V' is at offset 4 on
    /// the host and 8 on the device</c>, <c>the value is at offset 4 on the host and 8 on the device</c>).
    /// </summary>
    public static string DescribeLayoutDifference(ITypeSymbol type, ITranspilerDialect dialect)
    {
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            TransferPlanner.Plan(type, dialect) is TransferPlan.Nullable nullable)
        {
            return DescribeNullableDifference(type.UnwrapNullable(), nullable, dialect);
        }

        var structType = (INamedTypeSymbol)type;
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

    // The value of a Nullable whose plan is not identical: its own difference, or where it sits
    private static string DescribeNullableDifference(ITypeSymbol valueType, TransferPlan.Nullable nullable,
        ITranspilerDialect dialect)
    {
        if (nullable.Value is not TransferPlan.Blit &&
            valueType is INamedTypeSymbol { TypeKind: TypeKind.Struct } value)
        {
            return $"the value of type '{TypeLayoutCalculator.FormatTypeName(value)}': " +
                   DescribeLayoutDifference(value, dialect);
        }

        if (nullable.ValueHostOffset != nullable.ValueDeviceOffset)
        {
            return $"the value is at offset {nullable.ValueHostOffset} on the host " +
                   $"and {nullable.ValueDeviceOffset} on the device";
        }

        return $"it is {nullable.HostSize} bytes on the host and {nullable.DeviceSize} bytes on the device";
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

            // A Nullable<S> holds the arrays of S (#972)
            if (member.Type.UnwrapNullable() is INamedTypeSymbol { TypeKind: TypeKind.Struct } nested &&
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

        // [WHY] Only sizes and offsets decide: with the same stride and offsets the bytes are the same,
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
        // [WHY] A Nullable is identical only when its value is and sits at the same offset: Vector2? has an 8-byte
        // value on both sides, at 4 on the host and at 8 on CUDA (#961)
        if (memberType is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1
            })
        {
            return TransferPlanner.Plan(memberType, dialect) is TransferPlan.Nullable { IsIdentical: true };
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
