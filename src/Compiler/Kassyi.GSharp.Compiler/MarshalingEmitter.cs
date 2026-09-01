using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Compiler;

/// <summary>Emits native P/Invoke marshaling code, pinned struct conversions, and CUDA parameter setup.</summary>
/// <remarks><para>Implements: <b>GEN-34, GEN-36</b></para></remarks>
internal static class MarshalingEmitter
{
    internal static bool IsCustomStruct(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { TypeKind: TypeKind.Struct } named &&
               !named.ContainingNamespace.IsSystemOrNumerics();
    }

    internal static void CollectStructs(ITypeSymbol type, HashSet<INamedTypeSymbol> structs)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } named ||
            named.ContainingNamespace.IsSystemOrNumerics())
        {
            return;
        }

        if (structs.Add(named))
        {
            foreach (var member in named.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
            {
                CollectStructs(member.Type, structs);
            }
        }
    }

    internal static void WriteStructMarshaling(SourceWriter writer, IMethodSymbol method)
    {
        var structTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var parameter in method.Parameters)
        {
            CollectStructs(parameter.Type, structTypes);
        }

        if (structTypes.Count > 0)
        {
            writer.AppendLine(
                "#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type");
            foreach (var s in structTypes)
            {
                WriteNativeStructDeclaration(writer, s);
                WriteMarshalMethod(writer, s);
                WriteUnmarshalMethod(writer, s);
            }

            writer.AppendLine("#pragma warning restore CS8500");
        }
    }

    private static void WriteNativeStructDeclaration(SourceWriter writer, INamedTypeSymbol s)
    {
        writer.AppendLine(
            "[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]");
        writer.Append("public struct __Native_");
        writer.AppendFullyQualifiedCppName(s);
        writer.AppendLine();
        using (writer.Scope())
        {
            foreach (var member in s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
            {
                if (member.Type is IArrayTypeSymbol)
                {
                    writer.Append("public IntPtr ");
                    writer.Append(member.Name);
                    writer.AppendSemiColonLine();
                }
                else if (IsCustomStruct(member.Type))
                {
                    writer.Append("public __Native_");
                    writer.AppendFullyQualifiedCppName(member.Type);
                    writer.AppendSpace();
                    writer.Append(member.Name);
                    writer.AppendSemiColonLine();
                }
                else
                {
                    if (member.Type.IsReferenceType)
                    {
                        writer.Append("#error Unsupported reference type in struct field: ");
                        writer.AppendLine(member.Name);
                    }
                    writer.Append("public ");
                    writer.Append(member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    writer.AppendSpace();
                    writer.Append(member.Name);
                    writer.AppendSemiColonLine();
                }
            }
        }

        writer.AppendLine();
    }

    private static void WriteMarshalMethod(SourceWriter writer, INamedTypeSymbol s)
    {
        writer.Append("public static unsafe __Native_");
        writer.AppendFullyQualifiedCppName(s);
        writer.Append(" Marshal_");
        writer.AppendFullyQualifiedCppName(s);
        writer.Append("(ref ");
        writer.Append(s.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        writer.AppendLine(" host, bool isDevicePtr, System.Collections.Generic.List<IntPtr> allocatedPtrs)");
        using (writer.Scope())
        {
            writer.Append("var native = new __Native_");
            writer.AppendFullyQualifiedCppName(s);
            writer.AppendLine("();");
            foreach (var member in s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
            {
                WriteMarshalMember(writer, member);
            }

            writer.AppendLine("return native;");
        }

        writer.AppendLine();
    }

    private static void WriteMarshalMember(SourceWriter writer, IFieldSymbol member)
    {
        if (member.Type is IArrayTypeSymbol arrayType)
        {
            WriteMarshalArrayMember(writer, member, arrayType);
        }
        else if (IsCustomStruct(member.Type))
        {
            writer.Append("native.");
            writer.Append(member.Name);
            writer.Append(" = Marshal_");
            writer.AppendFullyQualifiedCppName(member.Type);
            writer.Append("(ref host.");
            writer.Append(member.Name);
            writer.AppendLine(", isDevicePtr, allocatedPtrs);");
        }
        else
        {
            writer.Append("native.");
            writer.Append(member.Name);
            writer.Append(" = host.");
            writer.Append(member.Name);
            writer.AppendSemiColonLine();
        }
    }

    private static void WriteMarshalArrayMember(SourceWriter writer, IFieldSymbol member, IArrayTypeSymbol arrayType)
    {
        writer.Append("if (host.");
        writer.Append(member.Name);
        writer.AppendLine(" != null)");
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

                writer.AppendKeyword("if");
                using (writer.Parentheses())
                {
                    writer.Append("size");
                    writer.AppendOperator(">");
                    writer.Append("0");
                }

                writer.AppendLine();
                using (writer.Scope())
                {
                    writer.Append("CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out native.");
                    writer.Append(member.Name);
                    writer.AppendLine(", size));");
                    writer.Append("allocatedPtrs.Add(native.");
                    writer.Append(member.Name);
                    writer.AppendLine(");");
                    writer.Append("var handle = System.Runtime.InteropServices.GCHandle.Alloc(host.");
                    writer.Append(member.Name);
                    writer.AppendLine(", System.Runtime.InteropServices.GCHandleType.Pinned);");
                    writer.AppendLine("try");
                    using (writer.Scope())
                    {
                        writer.Append("CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(native.");
                        writer.Append(member.Name);
                        writer.AppendLine(", handle.AddrOfPinnedObject(), size));");
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

    private static void WriteUnmarshalMethod(SourceWriter writer, INamedTypeSymbol s)
    {
        writer.Append("public static unsafe void Unmarshal_");
        writer.AppendFullyQualifiedCppName(s);
        writer.Append("(ref ");
        writer.Append(s.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        writer.Append(" host, ref __Native_");
        writer.AppendFullyQualifiedCppName(s);
        writer.AppendLine(" native, bool isDevicePtr)");
        using (writer.Scope())
        {
            foreach (var member in s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
            {
                WriteUnmarshalMember(writer, member);
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
        else if (IsCustomStruct(member.Type))
        {
            writer.Append("Unmarshal_");
            writer.AppendFullyQualifiedCppName(member.Type);
            writer.Append("(ref host.");
            writer.Append(member.Name);
            writer.Append(", ref native.");
            writer.Append(member.Name);
            writer.AppendLine(", isDevicePtr);");
        }
        else
        {
            writer.Append("host.");
            writer.Append(member.Name);
            writer.Append(" = native.");
            writer.Append(member.Name);
            writer.AppendSemiColonLine();
        }
    }

    private static void WriteUnmarshalArrayMember(SourceWriter writer, IFieldSymbol member, IArrayTypeSymbol arrayType)
    {
        writer.Append("if (host.");
        writer.Append(member.Name);
        writer.Append(" != null && !isDevicePtr && native.");
        writer.Append(member.Name);
        writer.AppendLine(" != IntPtr.Zero)");
        using (writer.Scope())
        {
            WriteArrayElementSize(writer, "host.", member.Name, arrayType);

            writer.AppendKeyword("if");
            using (writer.Parentheses())
            {
                writer.Append("size");
                writer.AppendOperator(">");
                writer.Append("0");
            }

            writer.AppendLine();
            using (writer.Scope())
            {
                writer.Append("var handle = System.Runtime.InteropServices.GCHandle.Alloc(host.");
                writer.Append(member.Name);
                writer.AppendLine(", System.Runtime.InteropServices.GCHandleType.Pinned);");
                writer.AppendLine("try");
                using (writer.Scope())
                {
                    writer.Append(
                        "CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(handle.AddrOfPinnedObject(), native.");
                    writer.Append(member.Name);
                    writer.AppendLine(", size));");
                }

                writer.AppendLine("finally");
                using (writer.Scope())
                {
                    writer.AppendLine("handle.Free();");
                }
            }
        }
    }

    private static void WriteArrayElementSize(SourceWriter writer, string prefix, string name,
        IArrayTypeSymbol arrayType)
    {
        if (IsCustomStruct(arrayType.ElementType))
        {
            writer.Append("int size = ");
            writer.Append(prefix);
            writer.Append(name);
            writer.Append(".Length * sizeof(__Native_");
            writer.AppendFullyQualifiedCppName(arrayType.ElementType);
            writer.AppendLine(");");
        }
        else
        {
            writer.Append("int size = ");
            writer.Append(prefix);
            writer.Append(name);
            writer.Append(".Length * sizeof(");
            writer.Append(arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            writer.AppendLine(");");
        }
    }

    internal static void WriteVariablesInitialization(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        foreach (var p in method.Parameters.Where(p => !p.HasAttribute(KnownAttributes.JitConstant)))
        {
            if (IsCustomStruct(p.Type))
            {
                writer.Append("System.Collections.Generic.List<IntPtr> ");
                writer.Append(p.Name);
                writer.Append("_allocatedPtrs = new System.Collections.Generic.List<IntPtr>");
                writer.AppendParentheses();

                writer.AppendLine(";");
                if (p.RefKind is RefKind.Ref or RefKind.Out)
                {
                    writer.Append("IntPtr d_");
                    writer.Append(p.Name);
                    writer.AppendLine(" = IntPtr.Zero;");
                }
            }
            else if (!isDevicePtr &&
                     (p.Type is IArrayTypeSymbol || p.RefKind is RefKind.Ref or RefKind.Out))
            {
                writer.Append("IntPtr d_");
                writer.Append(p.Name);
                writer.AppendLine(" = IntPtr.Zero;");
            }
        }
    }

    internal static void WriteMarshalAndLaunchKernel(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        var parameters = method.Parameters.Where(p => !p.HasAttribute(KnownAttributes.JitConstant)).ToList();
        writer.Append("void** kernelParams = stackalloc void*");
        using (writer.Brackets())
        {
            writer.Append(parameters.Count);
        }

        writer.AppendLine(";");

        for (var i = 0; i < parameters.Count; i++)
        {
            WriteParameterMarshal(writer, parameters[i], i, isDevicePtr);
        }

        writer.AppendLine();

        WriteLaunchKernelCall(writer, method);

        foreach (var p in method.Parameters.Where(p => !p.HasAttribute(KnownAttributes.JitConstant)))
        {
            WriteParameterUnmarshal(writer, p, isDevicePtr);
        }
    }

    internal static void WriteCleanup(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        foreach (var p in method.Parameters.Where(p => !p.HasAttribute(KnownAttributes.JitConstant)))
        {
            WriteParameterFree(writer, p, isDevicePtr);
        }
    }

    private static void WriteParameterMarshal(SourceWriter writer, IParameterSymbol p, int paramIndex, bool isDevicePtr)
    {
        if (IsCustomStruct(p.Type))
        {
            WriteCustomStructMarshal(writer, p, paramIndex, isDevicePtr);
        }
        else if (p.Type is IArrayTypeSymbol arrayType)
        {
            WriteArrayMarshal(writer, p, arrayType, paramIndex, isDevicePtr);
        }
        else if (p.RefKind is RefKind.Ref or RefKind.Out)
        {
            WriteRefOrOutScalarMarshal(writer, p, paramIndex);
        }
        else
        {
            WriteScalarMarshal(writer, p, paramIndex);
        }
    }

    private static void WriteCustomStructMarshal(SourceWriter writer, IParameterSymbol p, int paramIndex,
        bool isDevicePtr)
    {
        writer.Append("var native_");
        writer.Append(p.Name);
        writer.Append(" = Marshal_");
        writer.AppendFullyQualifiedCppName(p.Type);
        writer.Append("(ref ");
        writer.Append(p.Name);
        writer.Append(", ");
        writer.Append(isDevicePtr ? "true" : "false");
        writer.Append(", ");
        writer.Append(p.Name);
        writer.AppendLine("_allocatedPtrs);");

        if (p.RefKind is RefKind.Ref or RefKind.Out)
        {
            writer.Append("// Marshal ref/out struct: ");
            writer.AppendLine(p.Name);
            writer.Append("int ");
            writer.Append(p.Name);
            writer.Append("_size = sizeof(__Native_");
            writer.AppendFullyQualifiedCppName(p.Type);
            writer.AppendLine(");");

            writer.Append("CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_");
            writer.Append(p.Name);
            writer.Append(", ");
            writer.Append(p.Name);
            writer.AppendLine("_size));");
            if (p.RefKind == RefKind.Ref)
            {
                writer.AppendLine("// Fast zero-allocation copy to device");
                writer.Append("CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_");
                writer.Append(p.Name);
                writer.Append(", (IntPtr)(&native_");
                writer.Append(p.Name);
                writer.Append("), ");
                writer.Append(p.Name);
                writer.AppendLine("_size));");
            }

            writer.Append("kernelParams");
            using (writer.Brackets())
            {
                writer.Append(paramIndex);
            }
            writer.Append(" = &d_");
            writer.Append(p.Name);
            writer.AppendSemiColonLine();
        }
        else
        {
            writer.Append("// Marshal scalar struct: ");
            writer.AppendLine(p.Name);
            writer.Append("kernelParams");
            using (writer.Brackets())
            {
                writer.Append(paramIndex);
            }
            writer.Append(" = &native_");
            writer.Append(p.Name);
            writer.AppendSemiColonLine();
        }
    }

    private static void WriteArrayMarshal(SourceWriter writer, IParameterSymbol p, IArrayTypeSymbol arrayType,
        int paramIndex, bool isDevicePtr)
    {
        if (isDevicePtr)
        {
            writer.Append("// Marshal device array: ");
            writer.AppendLine(p.Name);
            writer.Append("kernelParams");
            using (writer.Brackets())
            {
                writer.Append(paramIndex);
            }
            writer.Append(" = &");
            writer.Append(p.Name);
            writer.AppendSemiColonLine();
        }
        else
        {
            writer.Append("// Marshal array: ");
            writer.AppendLine(p.Name);
            writer.Append("int ");
            writer.Append(p.Name);
            writer.Append("_size = ");
            writer.Append(p.Name);
            writer.Append(".Length * sizeof(");
            writer.Append(arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            writer.AppendLine(");");
            writer.AppendKeyword("if");
            using (writer.Parentheses())
            {
                writer.Append(p.Name);
                writer.Append("_size");
                writer.AppendOperator(">");
                writer.Append("0");
            }

            writer.AppendLine();
            using (writer.Scope())
            {
                writer.Append("CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_");
                writer.Append(p.Name);
                writer.Append(", ");
                writer.Append(p.Name);
                writer.AppendLine("_size));");

                writer.Append("System.Runtime.InteropServices.GCHandle handle_");
                writer.Append(p.Name);
                writer.Append(" = System.Runtime.InteropServices.GCHandle.Alloc(");
                writer.Append(p.Name);
                writer.AppendLine(", System.Runtime.InteropServices.GCHandleType.Pinned);");

                writer.AppendLine("try");
                using (writer.Scope())
                {
                    writer.Append("IntPtr p_");
                    writer.Append(p.Name);
                    writer.Append(" = handle_");
                    writer.Append(p.Name);
                    writer.AppendLine(".AddrOfPinnedObject();");

                    writer.Append("CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_");
                    writer.Append(p.Name);
                    writer.Append(", p_");
                    writer.Append(p.Name);
                    writer.Append(", ");
                    writer.Append(p.Name);
                    writer.AppendLine("_size));");
                }

                writer.AppendLine("finally");
                using (writer.Scope())
                {
                    writer.Append("handle_");
                    writer.Append(p.Name);
                    writer.AppendLine(".Free();");
                }
            }

            writer.Append("kernelParams");
            using (writer.Brackets())
            {
                writer.Append(paramIndex);
            }
            writer.Append(" = &d_");
            writer.Append(p.Name);
            writer.AppendSemiColonLine();
        }
    }

    private static void WriteRefOrOutScalarMarshal(SourceWriter writer, IParameterSymbol p, int paramIndex)
    {
        writer.Append("// Marshal ref/out scalar: ");
        writer.AppendLine(p.Name);
        writer.Append("int ");
        writer.Append(p.Name);
        writer.Append("_size = sizeof(");
        writer.Append(p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        writer.AppendLine(");");

        writer.Append("CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_");
        writer.Append(p.Name);
        writer.Append(", ");
        writer.Append(p.Name);
        writer.AppendLine("_size));");

        if (p.RefKind == RefKind.Ref)
        {
            writer.AppendLine("// Fast zero-allocation copy to device");
            writer.Append("fixed (");
            writer.Append(p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            writer.Append("* p_");
            writer.Append(p.Name);
            writer.Append("_temp = &");
            writer.Append(p.Name);
            writer.AppendLine(")");
            using (writer.Scope())
            {
                writer.Append("CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_");
                writer.Append(p.Name);
                writer.Append(", (IntPtr)p_");
                writer.Append(p.Name);
                writer.Append("_temp, ");
                writer.Append(p.Name);
                writer.AppendLine("_size));");
            }
        }

        writer.Append("kernelParams");
        using (writer.Brackets())
        {
            writer.Append(paramIndex);
        }
        writer.Append(" = &d_");
        writer.Append(p.Name);
        writer.AppendSemiColonLine();
    }

    private static void WriteScalarMarshal(SourceWriter writer, IParameterSymbol p, int paramIndex)
    {
        writer.Append("// Marshal scalar: ");
        writer.AppendLine(p.Name);
        writer.Append("kernelParams");
        using (writer.Brackets())
        {
            writer.Append(paramIndex);
        }
        writer.Append(" = &");
        writer.Append(p.Name);
        writer.AppendSemiColonLine();
    }

    private static void WriteLaunchKernelCall(SourceWriter writer, IMethodSymbol method)
    {
        var isCooperative = false;
        var kernelAttr = method.GetAttribute(KnownAttributes.GpuKernel);
        if (kernelAttr != null)
        {
            var namedArg = kernelAttr.NamedArguments.FirstOrDefault(na => na.Key == "Cooperative");
            if (namedArg.Value.Value is true)
            {
                isCooperative = true;
            }
        }

        writer.Append("CudaException.ThrowIfError");
        using (writer.Parentheses())
        {
            writer.Append(isCooperative ? "CudaDriverApi.cuLaunchCooperativeKernel" : "CudaDriverApi.cuLaunchKernel");
            using (writer.Parentheses())
            {
                writer.AppendLine();
                writer.AppendLine("hfunc,");
                writer.AppendLine("gridDimX, gridDimY, gridDimZ, // Grid dim");
                writer.AppendLine("blockDimX, blockDimY, blockDimZ, // Block dim");
                writer.AppendLine("sharedMemBytes, // Shared mem");
                writer.AppendLine("IntPtr.Zero, // Stream");
                if (isCooperative)
                {
                    writer.Append("kernelParams");
                }
                else
                {
                    writer.AppendLine("kernelParams,");
                    writer.Append("IntPtr.Zero // Extra");
                }

                writer.AppendLine();
            }
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    private static void WriteParameterUnmarshal(SourceWriter writer, IParameterSymbol p, bool isDevicePtr)
    {
        if (IsCustomStruct(p.Type))
        {
            WriteCustomStructUnmarshal(writer, p, isDevicePtr);
        }
        else if (p.Type is IArrayTypeSymbol)
        {
            WriteArrayUnmarshal(writer, p, isDevicePtr);
        }
        else if (p.RefKind is RefKind.Ref or RefKind.Out)
        {
            WriteRefOrOutScalarUnmarshal(writer, p);
        }
    }

    private static void WriteCustomStructUnmarshal(SourceWriter writer, IParameterSymbol p, bool isDevicePtr)
    {
        if (p.RefKind is RefKind.Ref or RefKind.Out)
        {
            writer.AppendLine("// Fast zero-allocation copy from device");
            writer.Append("CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)(&native_");
            writer.Append(p.Name);
            writer.Append("), d_");
            writer.Append(p.Name);
            writer.Append(", ");
            writer.Append(p.Name);
            writer.AppendLine("_size));");
        }

        writer.Append("Unmarshal_");
        writer.AppendFullyQualifiedCppName(p.Type);
        writer.Append("(ref ");
        writer.Append(p.Name);
        writer.Append(", ref native_");
        writer.Append(p.Name);
        writer.Append(", ");
        writer.Append(isDevicePtr ? "true" : "false");
        writer.AppendLine(");");
    }

    private static void WriteArrayUnmarshal(SourceWriter writer, IParameterSymbol p, bool isDevicePtr)
    {
        if (isDevicePtr)
        {
            return;
        }

        writer.Append("// Copy back array: ");
        writer.AppendLine(p.Name);
        writer.AppendKeyword("if");
        using (writer.Parentheses())
        {
            writer.Append(p.Name);
            writer.Append("_size");
            writer.AppendOperator(">");
            writer.Append("0");
            writer.AppendOperator("&&");
            writer.Append("d_");
            writer.Append(p.Name);
            writer.AppendOperator("!=");
            writer.Append("IntPtr.Zero");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            writer.Append("System.Runtime.InteropServices.GCHandle");
            writer.Append(" handleBack_");
            writer.Append(p.Name);
            writer.AppendOperator("=");
            writer.Append("System.Runtime.InteropServices.GCHandle.Alloc");
            using (writer.Parentheses())
            {
                writer.Append(p.Name);
                writer.AppendComma();
                writer.Append("System.Runtime.InteropServices.GCHandleType.Pinned");
            }

            writer.AppendSemiColonLine();
            writer.AppendLine("try");
            using (writer.Scope())
            {
                writer.Append("IntPtr p_");
                writer.Append(p.Name);
                writer.Append(" = handleBack_");
                writer.Append(p.Name);
                writer.AppendLine(".AddrOfPinnedObject();");

                writer.Append("CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(p_");
                writer.Append(p.Name);
                writer.Append(", d_");
                writer.Append(p.Name);
                writer.Append(", ");
                writer.Append(p.Name);
                writer.AppendLine("_size));");
            }

            writer.AppendLine("finally");
            using (writer.Scope())
            {
                writer.Append("handleBack_");
                writer.Append(p.Name);
                writer.AppendLine(".Free();");
            }
        }
    }

    private static void WriteRefOrOutScalarUnmarshal(SourceWriter writer, IParameterSymbol p)
    {
        if (p.RefKind == RefKind.Out)
        {
            writer.Append(p.Name);
            writer.AppendLine(" = default;");
        }

        writer.Append("// Fast zero-allocation copy from device for scalar: ");
        writer.AppendLine(p.Name);
        writer.Append("fixed (");
        writer.Append(p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        writer.Append("* ptr_");
        writer.Append(p.Name);
        writer.Append(" = &");
        writer.Append(p.Name);
        writer.AppendLine(")");
        using (writer.Scope())
        {
            writer.Append("CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)ptr_");
            writer.Append(p.Name);
            writer.Append(", d_");
            writer.Append(p.Name);
            writer.Append(", ");
            writer.Append(p.Name);
            writer.AppendLine("_size));");
        }
    }

    internal static void WriteParameterFree(SourceWriter writer, IParameterSymbol p, bool isDevicePtr)
    {
        if (IsCustomStruct(p.Type))
        {
            writer.Append("foreach (var ptr in ");
            writer.Append(p.Name);
            writer.AppendLine("_allocatedPtrs) CudaDriverApi.cuMemFree_v2(ptr);");
            if (p.RefKind is RefKind.Ref or RefKind.Out)
            {
                writer.Append("if (d_");
                writer.Append(p.Name);
                writer.Append(" != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_");
                writer.Append(p.Name);
                writer.AppendLine(");");
            }
        }
        else if (p.Type is IArrayTypeSymbol)
        {
            if (!isDevicePtr)
            {
                writer.Append("if (d_");
                writer.Append(p.Name);
                writer.Append(" != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_");
                writer.Append(p.Name);
                writer.AppendLine(");");
            }
        }
        else if (p.RefKind is RefKind.Ref or RefKind.Out)
        {
            writer.Append("if (d_");
            writer.Append(p.Name);
            writer.Append(" != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_");
            writer.Append(p.Name);
            writer.AppendLine(");");
        }
    }
}
