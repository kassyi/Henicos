namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Writes the C++ literal of a constant field.</summary>
internal static class CppLiteralWriter
{
    public static ref readonly SourceWriter AppendConstantField(this in SourceWriter writer, IFieldSymbol field)
    {
        if (field.ConstantValue is null)
        {
            writer.Append("nullptr");
            return ref writer;
        }

        if (field.Type.SpecialType == SpecialType.System_Boolean)
        {
            writer.Append((bool)field.ConstantValue ? "true" : "false");
            return ref writer;
        }

        writer.Append(field.ConstantValue.ToString());

        var specialType = field.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } enumUnderlyingType }
            ? enumUnderlyingType.SpecialType
            : field.Type.SpecialType;
        var suffix = GetLiteralSuffix(specialType);
        if (suffix != null)
        {
            writer.Append(suffix);
        }

        return ref writer;
    }

    private static string? GetLiteralSuffix(SpecialType specialType) => specialType switch
    {
        SpecialType.System_UInt64 => "ULL",
        SpecialType.System_UInt32 => "U",
        SpecialType.System_Int64 => "LL",
        SpecialType.System_Single => "f",
        SpecialType.System_Double => "d",
        SpecialType.System_Decimal => "m",
        _ => null
    };
}
