namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Maps Roslyn type symbols to CUDA C++ type strings, pointers, and memory alignment layouts.</summary>
internal static class CudaTypeMapper
{
    private static readonly (string? CudaName, byte Size)[] s_specialTypes =
        new (string?, byte)[(int)SpecialType.System_Double + 1];

    static CudaTypeMapper()
    {
        register(SpecialType.System_Void, TypeConstants.CudaTypes.Void, 0);
        register(SpecialType.System_Boolean, TypeConstants.CudaTypes.Bool, 1);
        register(SpecialType.System_Byte, TypeConstants.CudaTypes.UnsignedChar, 1);
        register(SpecialType.System_SByte, TypeConstants.CudaTypes.Char, 1);
        register(SpecialType.System_Int16, TypeConstants.CudaTypes.Short, 2);
        register(SpecialType.System_UInt16, TypeConstants.CudaTypes.UnsignedShort, 2);
        register(SpecialType.System_Int32, TypeConstants.CudaTypes.Int, 4);
        register(SpecialType.System_UInt32, TypeConstants.CudaTypes.UnsignedInt, 4);
        register(SpecialType.System_Int64, TypeConstants.CudaTypes.LongLong, 8);
        register(SpecialType.System_UInt64, TypeConstants.CudaTypes.UnsignedLongLong, 8);
        register(SpecialType.System_Single, TypeConstants.CudaTypes.Float, 4);
        register(SpecialType.System_Double, TypeConstants.CudaTypes.Double, 8);
        return;

        void register(SpecialType type, string cudaName, byte size) =>
            s_specialTypes[(int)type] = (cudaName, size);
    }

    public static void AppendType(this ref SourceWriter writer, ITypeSymbol? typeSymbol)
    {
        switch (typeSymbol)
        {
            case null:
                writer.Append(TypeConstants.CudaTypes.Void);
                return;
            // 1. Indirection Types (Arrays & Pointers)
            case IArrayTypeSymbol array:
                writer.AppendType(array.ElementType);
                writer.Append("*");
                return;
            case IPointerTypeSymbol pointer:
                writer.AppendType(pointer.PointedAtType);
                writer.Append("*");
                return;
        }

        // 2. Primitive / Special Types (O(1) table lookup)
        if (TryGetSpecialTypeName(typeSymbol.SpecialType, out var specialName))
        {
            writer.Append(specialName);
            return;
        }

        // 3. Named Types (Generics, Framework Types, Vectors, Structs)
        if (typeSymbol is INamedTypeSymbol namedType)
        {
            if (namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                writer.Append("System_Nullable<");
                writer.AppendType(namedType.TypeArguments[0]);
                writer.Append(">");
                return;
            }

            if (namedType.Name == TypeConstants.GpuBigIntRefType)
            {
                writer.Append(TypeConstants.CudaTypes.UnsignedIntPtr);
                return;
            }

            if (TryGetVectorTypeName(namedType, out var vectorName))
            {
                writer.Append(vectorName);
                return;
            }
        }

        // 4. Fallback: User Defined Structs / Custom Types
        writer.AppendFullyQualifiedCppName(typeSymbol);
    }

    private static bool TryGetSpecialTypeName(SpecialType specialType, out string cudaName)
    {
        if ((uint)specialType < (uint)s_specialTypes.Length &&
            s_specialTypes[(int)specialType].CudaName is { } name)
        {
            cudaName = name;
            return true;
        }

        cudaName = string.Empty;
        return false;
    }

    private static bool TryGetVectorTypeName(ITypeSymbol typeSymbol, out string cudaName)
    {
        if (typeSymbol.IsSystemNumericsVector())
        {
            cudaName = typeSymbol.Name switch
            {
                TypeConstants.Vector2Type => TypeConstants.CudaTypes.Float2,
                TypeConstants.Vector3Type => TypeConstants.CudaTypes.Float3,
                TypeConstants.Vector4Type => TypeConstants.CudaTypes.Float4,
                _ => string.Empty
            };
            return cudaName.Length > 0;
        }

        cudaName = string.Empty;
        return false;
    }

    public static void AppendFullyQualifiedCppName(this ref SourceWriter writer, ISymbol symbol)
    {
        if (symbol is INamespaceSymbol { IsGlobalNamespace: true })
        {
            return;
        }

        var parent = symbol.ContainingSymbol;
        if (parent != null && parent is not INamespaceSymbol { IsGlobalNamespace: true })
        {
            writer.AppendFullyQualifiedCppName(parent);
            writer.Append('_');
        }

        writer.Append(symbol.Name);
    }

    public static int GetApproximateSize(ITypeSymbol typeSymbol) => GetSizeAndAlignment(typeSymbol).Size;

    private static (int Size, int Alignment) GetSizeAndAlignment(ITypeSymbol typeSymbol)
    {
        if (TryGetSpecialTypeSize(typeSymbol.SpecialType, out var specialSize))
        {
            return (specialSize, specialSize);
        }

        if (typeSymbol is not INamedTypeSymbol named || named.TypeKind != TypeKind.Struct)
        {
            return (8, 8); // arrays/pointers
        }

        var currentOffset = 0;
        var maxAlignment = 1;

        foreach (var field in named.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
        {
            var (fieldSize, fieldAlign) = GetSizeAndAlignment(field.Type);

            // Align current offset
            if (currentOffset % fieldAlign != 0)
            {
                currentOffset += fieldAlign - (currentOffset % fieldAlign);
            }

            currentOffset += fieldSize;
            if (fieldAlign > maxAlignment)
            {
                maxAlignment = fieldAlign;
            }
        }

        // Padding at the end of the struct
        if (currentOffset % maxAlignment != 0)
        {
            currentOffset += maxAlignment - (currentOffset % maxAlignment);
        }

        return (currentOffset == 0 ? 1 : currentOffset, maxAlignment);
    }

    private static bool TryGetSpecialTypeSize(SpecialType specialType, out byte size)
    {
        if ((uint)specialType < (uint)s_specialTypes.Length &&
            s_specialTypes[(int)specialType].Size is var s and > 0)
        {
            size = s;
            return true;
        }

        size = 0;
        return false;
    }

    public static (bool IsRefPointer, bool IsPointerType, bool HasRestrict) AppendParameterCppType(
        this ref SourceWriter writer, ParameterSyntax p, ITypeSymbol? typeSymbol)
    {
        var isRefPointer = false;
        var isIn = false;
        var isRefOut = false;

        foreach (var m in p.Modifiers)
        {
            if (m.IsKind(SyntaxKind.InKeyword))
            {
                isIn = true;
            }

            if (!m.IsKind(SyntaxKind.RefKeyword) && !m.IsKind(SyntaxKind.OutKeyword))
            {
                continue;
            }

            isRefPointer = true;
            isRefOut = true;
        }

        if (isIn)
        {
            writer.Append("const ");
        }

        var isPointerType = typeSymbol is IPointerTypeSymbol;
        var hasRestrict = false;

        if (typeSymbol is { TypeKind: TypeKind.Array })
        {
            writer.AppendType(((IArrayTypeSymbol)typeSymbol).ElementType);
            writer.Append("* __restrict__");
            isPointerType = true;
            hasRestrict = true;
        }
        else
        {
            writer.AppendType(typeSymbol);
        }

        if (isIn)
        {
            writer.Append("&");
        }
        else if (isRefOut)
        {
            writer.Append("*");
            isPointerType = true;
        }

        return (isRefPointer, isPointerType, hasRestrict);
    }
}
