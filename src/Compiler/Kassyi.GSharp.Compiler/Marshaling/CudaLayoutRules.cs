namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Computes memory layout and alignment for types according to CUDA ABI rules.</summary>
internal static class CudaLayoutRules
{
    internal record struct CudaLayoutInfo(int Size, int Alignment);

    /// <summary>Determines whether the type requires custom native struct generation.</summary>
    internal static bool IsCustomStruct(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { TypeKind: TypeKind.Struct } named &&
               !named.ContainingNamespace.IsSystemOrNumerics() &&
               !named.ContainingNamespace.IsCoreNamespace();
    }

    /// <summary>Recursively collects all custom structs referenced within the type tree.</summary>
    internal static void CollectStructs(ITypeSymbol type, HashSet<INamedTypeSymbol> structs)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } named ||
            named.ContainingNamespace.IsSystemOrNumerics() ||
            named.ContainingNamespace.IsCoreNamespace())
        {
            return;
        }

        if (!structs.Add(named))
        {
            return;
        }

        foreach (var member in named.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false } field)
            {
                CollectStructs(field.Type, structs);
            }
        }
    }

    /// <summary>Calculates the size and alignment requirements for a type according to CUDA ABI rules.</summary>
    internal static CudaLayoutInfo GetCudaLayout(ITypeSymbol type)
    {
        if (TryGetPredefinedLayout(type, out var layout))
        {
            return layout;
        }

        // Fallback for custom structs (or unexpected types)
        if (type is not INamedTypeSymbol namedStruct)
        {
            return new CudaLayoutInfo(4, 4);
        }

        var size = 0;
        var maxAlign = 1;
        var pack = 0;

        AttributeData? structLayoutAttr = null;
        foreach (var a in namedStruct.GetAttributes())
        {
            if (!a.IsStructLayoutAttribute())
            {
                continue;
            }

            structLayoutAttr = a;
            break;
        }

        if (structLayoutAttr != null)
        {
            foreach (var arg in structLayoutAttr.NamedArguments)
            {
                if (arg.Key != "Pack" || arg.Value.Value is not int packValue)
                {
                    continue;
                }

                pack = packValue;
                break;
            }
        }

        foreach (var member in namedStruct.GetMembers())
        {
            if (member is not IFieldSymbol { IsStatic: false } structField)
            {
                continue;
            }

            var fieldLayout = GetCudaLayout(structField.Type);
            var effectiveAlign = pack > 0 ? Math.Min(fieldLayout.Alignment, pack) : fieldLayout.Alignment;

            size = AlignUp(size, effectiveAlign) + fieldLayout.Size;
            maxAlign = Math.Max(maxAlign, effectiveAlign);
        }

        size = AlignUp(size, maxAlign);
        return new CudaLayoutInfo(Math.Max(size, 1), maxAlign);
    }

    /// <summary>Aligns a given byte offset up to the specified boundary.</summary>
    internal static int AlignUp(int offset, int alignment)
    {
        var remainder = offset % alignment;
        return remainder == 0 ? offset : offset + (alignment - remainder);
    }


    private static bool TryGetPredefinedLayout(ITypeSymbol type, out CudaLayoutInfo layout)
    {
        if (KernelLaunchEmitter.IsArrayLikeType(type, out _))
        {
            layout = new CudaLayoutInfo(16, 8); // Device pointer (8) + Length (4) + Padding (4)
            return true;
        }

        if (type is IPointerTypeSymbol)
        {
            layout = new CudaLayoutInfo(8, 8); // Device pointer size/alignment (64-bit CUDA)
            return true;
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol namedEnum &&
            namedEnum.EnumUnderlyingType != null)
        {
            return TryGetPredefinedLayout(namedEnum.EnumUnderlyingType, out layout);
        }

        if (TryGetPrimitiveLayout(type, out layout))
        {
            return true;
        }

        if (TryGetVectorLayout(type, out layout))
        {
            return true;
        }

        layout = default;
        return false;
    }

    private static bool TryGetPrimitiveLayout(ITypeSymbol type, out CudaLayoutInfo layout)
    {
        var primitive = type.SpecialType switch
        {
            SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => new CudaLayoutInfo(1, 1),
            SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16 => new CudaLayoutInfo(2, 2),
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => new CudaLayoutInfo(4, 4),
            SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double => new CudaLayoutInfo(8, 8),
            _ => default(CudaLayoutInfo?)
        };

        if (primitive.HasValue)
        {
            layout = primitive.Value;
            return true;
        }

        layout = default;
        return false;
    }

    private static bool TryGetVectorLayout(ITypeSymbol type, out CudaLayoutInfo layout)
    {
        if (!type.IsSystemNumericsVector())
        {
            layout = default;
            return false;
        }

        var vectorLayout = type.Name switch
        {
            "Float2" or "Int2" or "UInt2" or "Vector2" => new CudaLayoutInfo(8, 8),
            "Float3" or "Int3" or "UInt3" or "Vector3" => new CudaLayoutInfo(12, 4),
            "Float4" or "Int4" or "UInt4" or "Vector4" => new CudaLayoutInfo(16, 16),
            "Double2" or "Long2" or "ULong2" => new CudaLayoutInfo(16, 16),
            "Double3" or "Long3" or "ULong3" => new CudaLayoutInfo(24, 8),
            "Double4" or "Long4" or "ULong4" => new CudaLayoutInfo(32, 16),
            _ => default(CudaLayoutInfo?)
        };

        if (vectorLayout.HasValue)
        {
            layout = vectorLayout.Value;
            return true;
        }

        layout = default;
        return false;
    }
}
