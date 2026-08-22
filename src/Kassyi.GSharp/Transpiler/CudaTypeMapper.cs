namespace Kassyi.GSharp.Transpiler;

public static class CudaTypeMapper
{
    public static string MapType(TypeReference typeRef)
    {
        if (!typeRef.IsPointer && typeRef is { IsByReference: false, IsArray: false })
        {
            return typeRef.FullName switch
            {
                "System.Void" => "void",
                "System.Boolean" => "bool",
                "System.Char" => "char",
                "System.SByte" => "signed char",
                "System.Byte" => "unsigned char",
                "System.Int16" => "short",
                "System.UInt16" => "unsigned short",
                "System.Int32" => "int",
                "System.UInt32" => "unsigned int",
                "System.Int64" => "long long",
                "System.UInt64" => "unsigned long long",
                "System.Single" => "float",
                "System.Double" => "double",
                _ => typeRef.Name // Structs, etc. fallback to type name
            };
        }

        var elementType = typeRef.GetElementType();
        return MapType(elementType) + "*";
    }
}