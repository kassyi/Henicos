using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.Generators.GSharp.Transpiler;

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
        if (symbol.ContainingSymbol != null && !(symbol.ContainingSymbol is INamespaceSymbol ns && ns.IsGlobalNamespace))
        {
            return GetFullyQualifiedCppName(symbol.ContainingSymbol!) + "_" + symbol.Name;
        }
        return symbol.Name;
    }

    public static int GetApproximateSize(ITypeSymbol typeSymbol)
    {
        if (typeSymbol.SpecialType != SpecialType.None)
        {
            return typeSymbol.SpecialType switch
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
        }

        if (typeSymbol is not INamedTypeSymbol named || named.TypeKind != TypeKind.Struct)
        {
            return 8; // arrays/pointers
        }

        var size = 0;
        foreach (var field in named.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
        {
            size += GetApproximateSize(field.Type);
        }

        return size == 0 ? 1 : size;
    }

    public static (string CppType, bool IsPointer) GetParameterCppType(ParameterSyntax p, ITypeSymbol? typeSymbol)
    {
        var cppType = MapType(typeSymbol);
        var isPointer = false;

        if (typeSymbol is { TypeKind: TypeKind.Array })
        {
            cppType = MapType(((IArrayTypeSymbol)typeSymbol).ElementType) + "*";
        }

        foreach (var m in p.Modifiers)
        {
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

