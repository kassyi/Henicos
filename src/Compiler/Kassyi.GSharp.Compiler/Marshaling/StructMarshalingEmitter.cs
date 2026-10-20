namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits custom struct marshal and unmarshal helper methods between managed and native shadow representations.</summary>
internal static class StructMarshalingEmitter
{
    /// <summary>Emits marshal and unmarshal routines for all custom structs used in kernel signatures.</summary>
    internal static void WriteStructMarshaling(SourceWriter writer,
        IEnumerable<(IMethodSymbol Method, string Source, string ManagedCSharpSource)> methods)
    {
        var structTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var (method, _, _) in methods)
        {
            foreach (var parameter in method.Parameters)
            {
                CudaLayoutRules.CollectStructs(parameter.Type, structTypes);
            }
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
        writer.AppendNestedCppName(s);
        using (writer.Parentheses())
        {
            writer.Append("ref ");
            writer.AppendFullyQualifiedCsharpName(s);
            writer.Append(" host, ref ");
            writer.AppendAotStructPath(s);
            writer.Append(
                " native, bool isDevicePtr, System.Collections.Generic.List<IntPtr>? allocatedPtrs, IntPtr hStream = default");
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
        if (member.Type.TryGetArrayLikeElementType(out var elementType))
        {
            WriteMarshalArrayMember(writer, member, elementType);
        }
        else if (CudaLayoutRules.IsCustomStruct(member.Type))
        {
            writer.Append("Marshal_");
            writer.AppendNestedCppName(member.Type);
            using (writer.Parentheses())
            {
                writer.Append($"ref host.{member.Name.ToCSharpIdentifier()}, ref native.{member.Name.ToCSharpIdentifier()}, isDevicePtr, allocatedPtrs, hStream");
            }

            writer.AppendLine(";");
        }
        else
        {
            if (member.Type.IsReferenceType || member.Type.SpecialType != SpecialType.None)
            {
                writer.AppendLine($"native.{member.Name.ToCSharpIdentifier()} = host.{member.Name.ToCSharpIdentifier()};");
            }
            else
            {
                writer.AppendLine($"fixed (byte* pField = native.{member.Name.ToCSharpIdentifier()})");
                using (writer.Scope())
                {
                    writer.Append("*((");
                    writer.AppendFullyQualifiedCsharpName(member.Type);
                    writer.AppendLine($"*)pField) = host.{member.Name.ToCSharpIdentifier()};");
                }
            }
        }
    }

    private static void WriteMarshalArrayMember(SourceWriter writer, IFieldSymbol member, ITypeSymbol elementType)
    {
        writer.AppendLine($"if (host.{member.Name.ToCSharpIdentifier()} != null)");
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
                WriteArrayElementSize(writer, "host.", member.Name.ToCSharpIdentifier(), elementType);

                writer.AppendLine("if (size > 0)");
                using (writer.Scope())
                {
                    writer.AppendLine(
                        $"CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out native.{member.Name.ToCSharpIdentifier()}.Data, (IntPtr)size, hStream));");
                    writer.AppendLine($"allocatedPtrs?.Add(native.{member.Name.ToCSharpIdentifier()}.Data);");

                    if (CudaLayoutRules.IsCustomStruct(elementType))
                    {
                        writer.Append("var tempArray = new ");
                        writer.AppendAotStructPath(elementType);
                        writer.AppendLine($"[host.{member.Name.ToCSharpIdentifier()}.Length];");

                        writer.AppendLine($"for (int i = 0; i < host.{member.Name.ToCSharpIdentifier()}.Length; i++)");
                        using (writer.Scope())
                        {
                            writer.Append("Marshal_");
                            writer.AppendNestedCppName(elementType);
                            writer.AppendLine(
                                $"(ref host.{member.Name.ToCSharpIdentifier()}[i], ref tempArray[i], false, allocatedPtrs, hStream);");
                        }

                        writer.AppendLine(
                            "var handle = System.Runtime.InteropServices.GCHandle.Alloc(tempArray, System.Runtime.InteropServices.GCHandleType.Pinned);");
                        writer.AppendLine("try");
                        using (writer.Scope())
                        {
                            writer.AppendLine(
                                $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(native.{member.Name.ToCSharpIdentifier()}.Data, handle.AddrOfPinnedObject(), (IntPtr)size, hStream));");
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
                            $"var handle = System.Runtime.InteropServices.GCHandle.Alloc(host.{member.Name.ToCSharpIdentifier()}, System.Runtime.InteropServices.GCHandleType.Pinned);");
                        writer.AppendLine("try");
                        using (writer.Scope())
                        {
                            writer.AppendLine(
                                $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(native.{member.Name.ToCSharpIdentifier()}.Data, handle.AddrOfPinnedObject(), (IntPtr)size, hStream));");
                        }

                        writer.AppendLine("finally");
                        using (writer.Scope())
                        {
                            writer.AppendLine("handle.Free();");
                        }
                    }
                }

                writer.AppendLine($"native.{member.Name.ToCSharpIdentifier()}.Length = host.{member.Name.ToCSharpIdentifier()}.Length;");
            }
        }
    }

    private static void WriteUnmarshalMethod(SourceWriter writer, INamedTypeSymbol s)
    {
        writer.Append("public static unsafe void Unmarshal_");
        writer.AppendNestedCppName(s);
        using (writer.Parentheses())
        {
            writer.Append("ref ");
            writer.AppendFullyQualifiedCsharpName(s);
            writer.Append(" host, ref ");
            writer.AppendAotStructPath(s);
            writer.Append(" native, bool isDevicePtr, IntPtr hStream = default");
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
        if (member.Type.TryGetArrayLikeElementType(out var elementType))
        {
            WriteUnmarshalArrayMember(writer, member, elementType);
        }
        else if (CudaLayoutRules.IsCustomStruct(member.Type))
        {
            writer.Append("Unmarshal_");
            writer.AppendNestedCppName(member.Type);
            using (writer.Parentheses())
            {
                writer.Append($"ref host.{member.Name.ToCSharpIdentifier()}, ref native.{member.Name.ToCSharpIdentifier()}, isDevicePtr, hStream");
            }

            writer.AppendLine(";");
        }
        else
        {
            if (member.Type.IsReferenceType || member.Type.SpecialType != SpecialType.None)
            {
                writer.AppendLine($"host.{member.Name.ToCSharpIdentifier()} = native.{member.Name.ToCSharpIdentifier()};");
            }
            else
            {
                writer.AppendLine($"fixed (byte* pField = native.{member.Name.ToCSharpIdentifier()})");
                using (writer.Scope())
                {
                    writer.Append($"host.{member.Name.ToCSharpIdentifier()} = *((");
                    writer.AppendFullyQualifiedCsharpName(member.Type);
                    writer.AppendLine("*)pField);");
                }
            }
        }
    }

    private static void WriteUnmarshalArrayMember(SourceWriter writer, IFieldSymbol member, ITypeSymbol elementType)
    {
        writer.AppendLine(
            $"if (host.{member.Name.ToCSharpIdentifier()} != null && !isDevicePtr && native.{member.Name.ToCSharpIdentifier()}.Data != IntPtr.Zero)");
        using (writer.Scope())
        {
            WriteArrayElementSize(writer, "host.", member.Name.ToCSharpIdentifier(), elementType);

            writer.AppendLine("if (size > 0)");
            using (writer.Scope())
            {
                if (CudaLayoutRules.IsCustomStruct(elementType))
                {
                    writer.Append("var tempArray = new ");
                    writer.AppendAotStructPath(elementType);
                    writer.AppendLine($"[host.{member.Name.ToCSharpIdentifier()}.Length];");

                    writer.AppendLine(
                        "var handle = System.Runtime.InteropServices.GCHandle.Alloc(tempArray, System.Runtime.InteropServices.GCHandleType.Pinned);");
                    writer.AppendLine("try");
                    using (writer.Scope())
                    {
                        writer.AppendLine(
                            $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2(handle.AddrOfPinnedObject(), native.{member.Name.ToCSharpIdentifier()}.Data, (IntPtr)size, hStream));");
                    }

                    writer.AppendLine("finally");
                    using (writer.Scope())
                    {
                        writer.AppendLine("handle.Free();");
                    }

                    writer.AppendLine($"for (int i = 0; i < host.{member.Name.ToCSharpIdentifier()}.Length; i++)");
                    using (writer.Scope())
                    {
                        writer.Append("Unmarshal_");
                        writer.AppendNestedCppName(elementType);
                        writer.AppendLine($"(ref host.{member.Name.ToCSharpIdentifier()}[i], ref tempArray[i], false, hStream);");
                    }
                }
                else
                {
                    writer.AppendLine(
                        $"var handle = System.Runtime.InteropServices.GCHandle.Alloc(host.{member.Name.ToCSharpIdentifier()}, System.Runtime.InteropServices.GCHandleType.Pinned);");
                    writer.AppendLine("try");
                    using (writer.Scope())
                    {
                        writer.AppendLine(
                            $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2(handle.AddrOfPinnedObject(), native.{member.Name.ToCSharpIdentifier()}.Data, (IntPtr)size, hStream));");
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
        ITypeSymbol elementType)
    {
        if (CudaLayoutRules.IsCustomStruct(elementType))
        {
            writer.Append($"int size = {prefix}{name}.Length * sizeof(");
            writer.AppendAotStructPath(elementType);
            writer.AppendLine(");");
        }
        else
        {
            writer.Append($"int size = {prefix}{name}.Length * sizeof(");
            writer.AppendFullyQualifiedCsharpName(elementType);
            writer.AppendLine(");");
        }
    }
}
