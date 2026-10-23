namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits explicit layout shadow structs representing C# structs in native CUDA memory.</summary>
internal static class NativeStructEmitter
{
    /// <summary>Emits the explicit struct declaration with computed field offsets.</summary>
    internal static void WriteNativeStructDeclaration(SourceWriter writer, INamedTypeSymbol s)
    {
        var layout = CudaLayoutRules.GetCudaLayout(s);

        var parents = AotHostNames.GetEnclosingCustomStructs(s);

        foreach (var p in parents)
        {
            writer.AppendLine($"public unsafe partial struct {AotHostNames.GetNativeStructName(p)}");
            writer.AppendLine("{");
        }

        writer.AppendLine(
            $"[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = {layout.Size})]");
        writer.Append($"public unsafe partial struct {AotHostNames.GetNativeStructName(s)}");
        writer.AppendLine();

        using (writer.Scope())
        {
            foreach (var (field, offset, fieldLayout) in CudaLayoutRules.GetFieldLayouts(s))
            {
                writer.Append($"public const string DebugName_{field.Name} = \"");
                writer.AppendFullyQualifiedCsharpName(field.Type);
                writer.AppendLine("\";");
                writer.AppendLine($"[System.Runtime.InteropServices.FieldOffset({offset})]");

                MapToNativeField(writer, field, fieldLayout.Size);
            }
        }

        foreach (var dummy in parents)
        {
            writer.AppendLine("}");
        }

        writer.AppendLine();
    }

    private static void MapToNativeField(SourceWriter writer, IFieldSymbol field, int fieldSize)
    {
        if (field.Type.IsArrayLikeType())
        {
            writer.AppendLine($"public global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy {field.Name.ToCSharpIdentifier()};");
        }
        else if (CudaLayoutRules.IsCustomStruct(field.Type))
        {
            writer.Append("public ");
            writer.AppendAotStructPath(field.Type);
            writer.AppendLine($" {field.Name.ToCSharpIdentifier()};");
        }
        else if (field.Type.IsReferenceType)
        {
            writer.AppendLine($"#error Unsupported reference type in struct field: {field.Name.ToCSharpIdentifier()}");
        }
        else if (field.Type.SpecialType != SpecialType.None)
        {
            writer.Append("public ");
            writer.AppendFullyQualifiedCsharpName(field.Type);
            writer.AppendLine($" {field.Name.ToCSharpIdentifier()};");
        }
        else if (field.Type.TypeKind == TypeKind.Enum)
        {
            writer.Append("public ");
            writer.AppendFullyQualifiedCsharpName(field.Type);
            writer.AppendLine($" {field.Name.ToCSharpIdentifier()};");
        }
        else
        {
            // Core vector types (Float4, etc.) or explicit structs are emitted as fixed byte arrays.
            // This is a workaround to bypass CLR nesting bugs (TypeLoadException) when explicit structs are nested inside other structs.
            writer.AppendLine($"public fixed byte {field.Name.ToCSharpIdentifier()}[{fieldSize}];");
        }
    }
}
