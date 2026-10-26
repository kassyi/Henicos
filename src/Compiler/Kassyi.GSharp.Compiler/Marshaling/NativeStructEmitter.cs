namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits explicit layout shadow structs representing C# structs in native device memory.</summary>
internal static class NativeStructEmitter
{
    /// <summary>Emits the explicit struct declaration with computed field offsets for <paramref name="dialect"/>.</summary>
    internal static void WriteNativeStructDeclaration(SourceWriter writer, INamedTypeSymbol s, ITranspilerDialect dialect) =>
        WriteNativeStructDeclaration(writer, s, TypeLayoutCalculator.GetStructLayout(s, dialect));

    /// <summary>Emits the explicit struct declaration with computed field offsets from <paramref name="structLayout"/>.</summary>
    internal static void WriteNativeStructDeclaration(SourceWriter writer, INamedTypeSymbol s, StructLayoutInfo structLayout)
    {
        var parents = AotHostNames.GetEnclosingCustomStructs(s);

        foreach (var p in parents)
        {
            writer.AppendLine($"public unsafe partial struct {AotHostNames.GetNativeStructName(p)}");
            writer.AppendLine("{");
        }

        writer.AppendLine(
            $"[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = {structLayout.Device.Size})]");
        writer.Append($"public unsafe partial struct {AotHostNames.GetNativeStructName(s)}");
        writer.AppendLine();

        using (writer.Scope())
        {
            foreach (var (field, offset, fieldLayout) in structLayout.DeviceFields)
            {
                // [WHY] A reference type other than an array has no device layout: GSH0078 reports it, and the shadow
                // leaves it out so that the generated code still compiles.
                if (IsUnrepresentableReference(field))
                {
                    continue;
                }

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

    /// <summary>
    /// Whether <paramref name="member"/> is a reference type other than an array-like one, which the shadow struct and
    /// its marshaling leave out (GSH0078).
    /// </summary>
    internal static bool IsUnrepresentableReference(StructAbiMember member) =>
        member.Type.IsReferenceType && !member.Type.IsArrayLikeType();

    private static void MapToNativeField(SourceWriter writer, StructAbiMember field, int fieldSize)
    {
        if (field.Kind is StructAbiMemberKind.FixedBuffer or StructAbiMemberKind.InlineArrayElement)
        {
            // [WHY] An element block is copied as bytes, so the shadow holds all ElementCount elements and never names the (possibly private) element field's type.
            writer.AppendLine($"public fixed byte {field.Name.ToCSharpIdentifier()}[{fieldSize}];");
        }
        else if (field.Type.IsArrayLikeType())
        {
            writer.AppendLine($"public global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy {field.Name.ToCSharpIdentifier()};");
        }
        else if (TypeLayoutCalculator.IsUserStruct(field.Type))
        {
            writer.Append("public ");
            writer.AppendAotStructPath(field.Type);
            writer.AppendLine($" {field.Name.ToCSharpIdentifier()};");
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
