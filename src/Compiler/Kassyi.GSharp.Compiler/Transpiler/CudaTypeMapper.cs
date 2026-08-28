namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Maps Roslyn type symbols to CUDA C++ type strings, pointers, and memory alignment layouts.</summary>
internal static class CudaTypeMapper
{
    public static string MapType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
        {
            return "void";
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return MapType(arrayType.ElementType) + "*";
        }

        if (typeSymbol is IPointerTypeSymbol pointerType)
        {
            return MapType(pointerType.PointedAtType) + "*";
        }

        if (typeSymbol.Name == "GpuBigIntRef")
        {
            return "unsigned int*";
        }

        if (typeSymbol.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && typeSymbol is INamedTypeSymbol namedType)
        {
            return $"System_Nullable<{MapType(namedType.TypeArguments[0])}>";
        }

        return typeSymbol.SpecialType switch
        {
            SpecialType.System_Void => "void",
            SpecialType.System_Boolean => "bool",
            SpecialType.System_Byte => "unsigned char",
            SpecialType.System_SByte => "char",
            SpecialType.System_Int16 => "short",
            SpecialType.System_UInt16 => "unsigned short",
            SpecialType.System_Int32 => "int",
            SpecialType.System_UInt32 => "unsigned int",
            SpecialType.System_Int64 => "long long",
            SpecialType.System_UInt64 => "unsigned long long",
            SpecialType.System_Single => "float",
            SpecialType.System_Double => "double",
            _ => GetFullyQualifiedCppName(typeSymbol)
        };
    }

    public static string GetFullyQualifiedCppName(ISymbol symbol)
    {
        // Avoid string.Replace in hot paths by building the name directly
        if (symbol.ContainingSymbol != null &&
            !(symbol.ContainingSymbol is INamespaceSymbol { IsGlobalNamespace: true }))
        {
            return GetFullyQualifiedCppName(symbol.ContainingSymbol!) + "_" + symbol.Name;
        }

        return symbol.Name;
    }

    public static int GetApproximateSize(ITypeSymbol typeSymbol) => GetSizeAndAlignment(typeSymbol).Size;

    private static (int Size, int Alignment) GetSizeAndAlignment(ITypeSymbol typeSymbol)
    {
        if (typeSymbol.SpecialType != SpecialType.None)
        {
            var size = typeSymbol.SpecialType switch
            {
                SpecialType.System_Boolean => 1,
                SpecialType.System_Byte => 1,
                SpecialType.System_SByte => 1,
                SpecialType.System_Int16 => 2,
                SpecialType.System_UInt16 => 2,
                SpecialType.System_Int32 => 4,
                SpecialType.System_UInt32 => 4,
                SpecialType.System_Single => 4,
                SpecialType.System_Int64 => 8,
                SpecialType.System_UInt64 => 8,
                SpecialType.System_Double => 8,
                _ => 4
            };
            return (size, size);
        }

        if (typeSymbol is not INamedTypeSymbol named || named.TypeKind != TypeKind.Struct)
        {
            return (8, 8); // arrays/pointers
        }

        int currentOffset = 0;
        int maxAlignment = 1;

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

    public static (string CppType, bool IsPointer) GetParameterCppType(ParameterSyntax p, ITypeSymbol? typeSymbol)
    {
        var cppType = MapType(typeSymbol);
        var isPointer = false;

        if (typeSymbol is { TypeKind: TypeKind.Array })
        {
            cppType = MapType(((IArrayTypeSymbol)typeSymbol).ElementType) + "* __restrict__";
        }

        foreach (var m in p.Modifiers)
        {
            if (m.IsKind(SyntaxKind.InKeyword))
            {
                cppType = $"const {cppType}&";
                break;
            }

            if (m.IsKind(SyntaxKind.RefKeyword) || m.IsKind(SyntaxKind.OutKeyword))
            {
                cppType += "*";
                isPointer = true;
                break;
            }
        }

        return (cppType, isPointer);
    }
}

