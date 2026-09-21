using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Kassyi.GSharp.Compiler.Extensions;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits Marshal and Unmarshal helper methods for custom structs, mapping between C# and __Native_* structs.
/// </summary>
internal static class StructMarshalingEmitter
{
    internal static void WriteStructMarshaling(SourceWriter writer, IMethodSymbol method)
    {
        var structTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var parameter in method.Parameters)
        {
            CudaLayoutRules.CollectStructs(parameter.Type, structTypes);
        }

        if (structTypes.Count > 0)
        {
            writer.AppendLine(
                "#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type");
            foreach (var s in structTypes)
            {
                NativeStructEmitter.WriteNativeStructDeclaration(writer, s);
                WriteMarshalMethod(writer, s);
                WriteUnmarshalMethod(writer, s);
            }

            writer.AppendLine("#pragma warning restore CS8500");
        }
    }

    private static void WriteMarshalMethod(SourceWriter writer, INamedTypeSymbol s)
    {
        writer.Append("public static unsafe void");
        writer.Append(" Marshal_");
        writer.AppendFullyQualifiedCppName(s);
        using (writer.Parentheses())
        {
            writer.Append("ref ");
            writer.AppendFullyQualifiedCsharpName(s);
            writer.Append(" host, ref __Native_");
            writer.AppendFullyQualifiedCppName(s);
            writer.Append(" native, bool isDevicePtr, System.Collections.Generic.List<IntPtr>? allocatedPtrs");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            foreach (var member in s.GetMembers())
            {
                if (member is IFieldSymbol { IsStatic: false } field)
                {
                    WriteMarshalMember(writer, field);
                }
            }
        }

        writer.AppendLine();
    }

    private static void WriteMarshalMember(SourceWriter writer, IFieldSymbol member)
    {
        if (member.Type is IArrayTypeSymbol arrayType)
        {
            WriteMarshalArrayMember(writer, member, arrayType);
        }
        else if (CudaLayoutRules.IsCustomStruct(member.Type))
        {
            writer.Append("Marshal_");
            writer.AppendFullyQualifiedCppName(member.Type);
            using (writer.Parentheses())
            {
                writer.Append($"ref host.{member.Name}, ref native.{member.Name}, isDevicePtr, allocatedPtrs");
            }

            writer.AppendLine(";");
        }
        else
        {
            if (member.Type.IsReferenceType || member.Type.SpecialType != SpecialType.None)
            {
                writer.AppendLine($"native.{member.Name} = host.{member.Name};");
            }
            else
            {
                writer.AppendLine($"fixed (byte* pField = native.{member.Name})");
                using (writer.Scope())
                {
                    writer.AppendLine($"*(({member.Type.GetFullyQualifiedName()}*)pField) = host.{member.Name};");
                }
            }
        }
    }

    private static void WriteMarshalArrayMember(SourceWriter writer, IFieldSymbol member, IArrayTypeSymbol arrayType)
    {
        writer.AppendLine($"if (host.{member.Name} != null)");
        using (writer.Scope())
        {
            writer.AppendLine("if (isDevicePtr)");
            using (writer.Scope())
            {
                writer.AppendLine(
                    "throw new System.NotSupportedException(\"Passing a struct with arrays is not supported when isDevicePtr is true.\");");
            }
            writer.AppendLine("else");
            using (writer.Scope())
            {
                WriteArrayElementSize(writer, "host.", member.Name, arrayType);

                writer.AppendLine("if (size > 0)");
                using (writer.Scope())
                {
                    writer.AppendLine(
                        $"CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out native.{member.Name}, size));");
                    writer.AppendLine($"allocatedPtrs?.Add(native.{member.Name});");

                    if (CudaLayoutRules.IsCustomStruct(arrayType.ElementType))
                    {
                        writer.Append($"var tempArray = new __Native_");
                        writer.AppendFullyQualifiedCppName(arrayType.ElementType);
                        writer.AppendLine($"[host.{member.Name}.Length];");
                        
                        writer.AppendLine($"for (int i = 0; i < host.{member.Name}.Length; i++)");
                        using (writer.Scope())
                        {
                            writer.Append("Marshal_");
                            writer.AppendFullyQualifiedCppName(arrayType.ElementType);
                            writer.AppendLine($"(ref host.{member.Name}[i], ref tempArray[i], false, allocatedPtrs);");
                        }
                        
                        writer.AppendLine("var handle = System.Runtime.InteropServices.GCHandle.Alloc(tempArray, System.Runtime.InteropServices.GCHandleType.Pinned);");
                        writer.AppendLine("try");
                        using (writer.Scope())
                        {
                            writer.AppendLine(
                                $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(native.{member.Name}, handle.AddrOfPinnedObject(), size));");
                        }
                        writer.AppendLine("finally");
                        using (writer.Scope())
                        {
                            writer.AppendLine("handle.Free();");
                        }
                    }
                    else
                    {
                        writer.AppendLine(
                            $"var handle = System.Runtime.InteropServices.GCHandle.Alloc(host.{member.Name}, System.Runtime.InteropServices.GCHandleType.Pinned);");
                        writer.AppendLine("try");
                        using (writer.Scope())
                        {
                            writer.AppendLine(
                                $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(native.{member.Name}, handle.AddrOfPinnedObject(), size));");
                        }
                        writer.AppendLine("finally");
                        using (writer.Scope())
                        {
                            writer.AppendLine("handle.Free();");
                        }
                    }
                }
            }
        }
    }

    private static void WriteUnmarshalMethod(SourceWriter writer, INamedTypeSymbol s)
    {
        writer.Append("public static unsafe void Unmarshal_");
        writer.AppendFullyQualifiedCppName(s);
        using (writer.Parentheses())
        {
            writer.Append("ref ");
            writer.AppendFullyQualifiedCsharpName(s);
            writer.Append(" host, ref __Native_");
            writer.AppendFullyQualifiedCppName(s);
            writer.Append(" native, bool isDevicePtr");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            foreach (var member in s.GetMembers())
            {
                if (member is IFieldSymbol { IsStatic: false } field)
                {
                    WriteUnmarshalMember(writer, field);
                }
            }
        }

        writer.AppendLine();
    }

    private static void WriteUnmarshalMember(SourceWriter writer, IFieldSymbol member)
    {
        if (member.Type is IArrayTypeSymbol arrayType)
        {
            WriteUnmarshalArrayMember(writer, member, arrayType);
        }
        else if (CudaLayoutRules.IsCustomStruct(member.Type))
        {
            writer.Append("Unmarshal_");
            writer.AppendFullyQualifiedCppName(member.Type);
            using (writer.Parentheses())
            {
                writer.Append($"ref host.{member.Name}, ref native.{member.Name}, isDevicePtr");
            }

            writer.AppendLine(";");
        }
        else
        {
            if (member.Type.IsReferenceType || member.Type.SpecialType != SpecialType.None)
            {
                writer.AppendLine($"host.{member.Name} = native.{member.Name};");
            }
            else
            {
                writer.AppendLine($"fixed (byte* pField = native.{member.Name})");
                using (writer.Scope())
                {
                    writer.AppendLine($"host.{member.Name} = *(({member.Type.GetFullyQualifiedName()}*)pField);");
                }
            }
        }
    }

    private static void WriteUnmarshalArrayMember(SourceWriter writer, IFieldSymbol member, IArrayTypeSymbol arrayType)
    {
        writer.AppendLine($"if (host.{member.Name} != null && !isDevicePtr && native.{member.Name} != IntPtr.Zero)");
        using (writer.Scope())
        {
            WriteArrayElementSize(writer, "host.", member.Name, arrayType);

            writer.AppendLine("if (size > 0)");
            using (writer.Scope())
            {
                if (CudaLayoutRules.IsCustomStruct(arrayType.ElementType))
                {
                    writer.Append($"var tempArray = new __Native_");
                    writer.AppendFullyQualifiedCppName(arrayType.ElementType);
                    writer.AppendLine($"[host.{member.Name}.Length];");
                    
                    writer.AppendLine("var handle = System.Runtime.InteropServices.GCHandle.Alloc(tempArray, System.Runtime.InteropServices.GCHandleType.Pinned);");
                    writer.AppendLine("try");
                    using (writer.Scope())
                    {
                        writer.AppendLine(
                            $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(handle.AddrOfPinnedObject(), native.{member.Name}, size));");
                    }
                    writer.AppendLine("finally");
                    using (writer.Scope())
                    {
                        writer.AppendLine("handle.Free();");
                    }
                    
                    writer.AppendLine($"for (int i = 0; i < host.{member.Name}.Length; i++)");
                    using (writer.Scope())
                    {
                        writer.Append("Unmarshal_");
                        writer.AppendFullyQualifiedCppName(arrayType.ElementType);
                        writer.AppendLine($"(ref host.{member.Name}[i], ref tempArray[i], false);");
                    }
                }
                else
                {
                    writer.AppendLine(
                        $"var handle = System.Runtime.InteropServices.GCHandle.Alloc(host.{member.Name}, System.Runtime.InteropServices.GCHandleType.Pinned);");
                    writer.AppendLine("try");
                    using (writer.Scope())
                    {
                        writer.AppendLine(
                            $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(handle.AddrOfPinnedObject(), native.{member.Name}, size));");
                    }
                    writer.AppendLine("finally");
                    using (writer.Scope())
                    {
                        writer.AppendLine("handle.Free();");
                    }
                }
            }
        }
    }

    private static void WriteArrayElementSize(SourceWriter writer, string prefix, string name,
        IArrayTypeSymbol arrayType)
    {
        if (CudaLayoutRules.IsCustomStruct(arrayType.ElementType))
        {
            writer.Append($"int size = {prefix}{name}.Length * sizeof(__Native_");
            writer.AppendFullyQualifiedCppName(arrayType.ElementType);
            writer.AppendLine(");");
        }
        else
        {
            writer.Append($"int size = {prefix}{name}.Length * sizeof(");
            writer.AppendFullyQualifiedCsharpName(arrayType.ElementType);
            writer.AppendLine(");");
        }
    }
}
