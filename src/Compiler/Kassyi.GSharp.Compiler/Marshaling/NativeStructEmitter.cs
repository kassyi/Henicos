using System;
using Microsoft.CodeAnalysis;
using Kassyi.GSharp.Compiler.Extensions;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits the native shadow structs ([StructLayout(Explicit)]) that correspond to C# custom structs.
/// </summary>
internal static class NativeStructEmitter
{
    internal static void WriteNativeStructDeclaration(SourceWriter writer, INamedTypeSymbol s)
    {
        var layout = CudaLayoutRules.GetCudaLayout(s);

        // We strictly use LayoutKind.Explicit to match CUDA ABI alignment rules perfectly.
        // This also bypasses a known CLR TypeLoadException bug that occurs when nesting
        // structs containing explicit layouts or arrays if LayoutKind.Sequential is used.
        writer.AppendLine($"[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = {layout.Size})]");
        writer.Append("public unsafe struct __Native_");
        writer.AppendFullyQualifiedCppName(s);
        writer.AppendLine();
        
        using (writer.Scope())
        {
            var currentOffset = 0;
            foreach (var member in s.GetMembers())
            {
                if (member is not IFieldSymbol { IsStatic: false } field)
                {
                    continue;
                }

                var fieldLayout = CudaLayoutRules.GetCudaLayout(field.Type);
                currentOffset = CudaLayoutRules.AlignUp(currentOffset, fieldLayout.Alignment);

                writer.AppendLine($"public const string DebugName_{field.Name} = \"{field.Type.GetFullyQualifiedName()}\";");
                writer.AppendLine($"[System.Runtime.InteropServices.FieldOffset({currentOffset})]");

                MapToNativeField(writer, field, fieldLayout.Size);

                currentOffset += fieldLayout.Size;
            }
        }

        writer.AppendLine();
    }

    private static void MapToNativeField(SourceWriter writer, IFieldSymbol field, int fieldSize)
    {
        if (field.Type is IArrayTypeSymbol)
        {
            writer.AppendLine($"public IntPtr {field.Name};");
        }
        else if (CudaLayoutRules.IsCustomStruct(field.Type))
        {
            writer.Append("public __Native_");
            writer.AppendFullyQualifiedCppName(field.Type);
            writer.AppendLine($" {field.Name};");
        }
        else if (field.Type.IsReferenceType)
        {
            writer.AppendLine($"#error Unsupported reference type in struct field: {field.Name}");
        }
        else if (field.Type.SpecialType != SpecialType.None)
        {
            writer.Append("public ");
            writer.AppendFullyQualifiedCsharpName(field.Type);
            writer.AppendLine($" {field.Name};");
        }
        else
        {
            // Core vector types (Float4, etc.) or explicit structs are emitted as fixed byte arrays.
            // This is a workaround to bypass CLR nesting bugs (TypeLoadException) when explicit structs are nested inside other structs.
            writer.AppendLine($"public fixed byte {field.Name}[{fieldSize}];");
        }
    }
}
