namespace Kassyi.GSharp.Compiler;

internal static class MarshalingEmitter
{
    internal static bool IsCustomStruct(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { TypeKind: TypeKind.Struct } named &&
               named.ContainingNamespace.Name != "System";
    }

    internal static void CollectStructs(ITypeSymbol type, HashSet<INamedTypeSymbol> structs)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } named ||
            named.ContainingNamespace.Name == TypeConstants.SystemNamespace)
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

        foreach (var s in structTypes)
        {
            var sName = s.ToDisplayString().Replace("global::", "").Replace(".", "_");
            var nativeName = $"__Native_{sName}";

            WriteNativeStructDeclaration(writer, s, nativeName);
            WriteMarshalMethod(writer, s, sName, nativeName);
            WriteUnmarshalMethod(writer, s, sName, nativeName);
        }
    }

    private static void WriteNativeStructDeclaration(SourceWriter writer, INamedTypeSymbol s, string nativeName)
    {
        writer.AppendLine(
            "[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]");
        writer.AppendLine($"public struct {nativeName}");
        using (writer.Scope())
        {
            foreach (var member in s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
            {
                if (member.Type is IArrayTypeSymbol)
                {
                    writer.AppendLine($"public IntPtr {member.Name};");
                }
                else if (IsCustomStruct(member.Type))
                {
                    var memberTypeName = member.Type.ToDisplayString().Replace("global::", "").Replace(".", "_");
                    writer.AppendLine($"public __Native_{memberTypeName} {member.Name};");
                }
                else
                {
                    writer.AppendLine($"public {member.Type.ToDisplayString()} {member.Name};");
                }
            }
        }

        writer.AppendLine();
    }

    private static void WriteMarshalMethod(SourceWriter writer, INamedTypeSymbol s, string sName, string nativeName)
    {
        writer.AppendLine(
            $"public static {nativeName} Marshal_{sName}(ref {s.ToDisplayString()} host, bool isDevicePtr, System.Collections.Generic.List<IntPtr> allocatedPtrs)");
        using (writer.Scope())
        {
            writer.AppendLine($"var native = new {nativeName}();");
            foreach (var member in s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
            {
                if (member.Type is IArrayTypeSymbol arrayType)
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
                            writer.AppendLine(
                                $"int size = host.{member.Name}.Length * System.Runtime.InteropServices.Marshal.SizeOf(typeof({arrayType.ElementType.ToDisplayString()}));");
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
                                writer.AppendLine(
                                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out native.{member.Name}, size));");
                                writer.AppendLine($"allocatedPtrs.Add(native.{member.Name});");
                                writer.AppendLine(
                                    $"var handle = System.Runtime.InteropServices.GCHandle.Alloc(host.{member.Name}, System.Runtime.InteropServices.GCHandleType.Pinned);");
                                writer.AppendLine("try");
                                using (writer.Scope())
                                {
                                    writer.AppendLine(
                                        $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(native.{member.Name}, handle.AddrOfPinnedObject(), size));");
                                }

                                writer.AppendKeyword("finally");
                                writer.AppendLine();
                                using (writer.Scope())
                                {
                                    writer.AppendLine("handle.Free();");
                                }
                            }
                        }
                    }
                }
                else if (IsCustomStruct(member.Type))
                {
                    var memberTypeName = member.Type.ToDisplayString().Replace("global::", "").Replace(".", "_");
                    writer.AppendLine(
                        $"native.{member.Name} = Marshal_{memberTypeName}(ref host.{member.Name}, isDevicePtr, allocatedPtrs);");
                }
                else
                {
                    writer.AppendLine($"native.{member.Name} = host.{member.Name};");
                }
            }

            writer.AppendLine("return native;");
        }

        writer.AppendLine();
    }

    private static void WriteUnmarshalMethod(SourceWriter writer, INamedTypeSymbol s, string sName, string nativeName)
    {
        writer.AppendLine(
            $"public static void Unmarshal_{sName}(ref {s.ToDisplayString()} host, ref {nativeName} native, bool isDevicePtr)");
        using (writer.Scope())
        {
            foreach (var member in s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
            {
                if (member.Type is IArrayTypeSymbol arrayType)
                {
                    writer.AppendLine(
                        $"if (host.{member.Name} != null && !isDevicePtr && native.{member.Name} != IntPtr.Zero)");
                    using (writer.Scope())
                    {
                        writer.AppendLine(
                            $"int size = host.{member.Name}.Length * System.Runtime.InteropServices.Marshal.SizeOf(typeof({arrayType.ElementType.ToDisplayString()}));");
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
                            writer.AppendLine(
                                $"var handle = System.Runtime.InteropServices.GCHandle.Alloc(host.{member.Name}, System.Runtime.InteropServices.GCHandleType.Pinned);");
                            writer.AppendLine("try");
                            using (writer.Scope())
                            {
                                writer.AppendLine(
                                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(handle.AddrOfPinnedObject(), native.{member.Name}, size));");
                            }

                            writer.AppendKeyword("finally");
                            writer.AppendLine();
                            using (writer.Scope())
                            {
                                writer.AppendLine("handle.Free();");
                            }
                        }
                    }
                }
                else if (IsCustomStruct(member.Type))
                {
                    var memberTypeName = member.Type.ToDisplayString().Replace("global::", "").Replace(".", "_");
                    writer.AppendLine(
                        $"Unmarshal_{memberTypeName}(ref host.{member.Name}, ref native.{member.Name}, isDevicePtr);");
                }
                else
                {
                    writer.AppendLine($"host.{member.Name} = native.{member.Name};");
                }
            }
        }

        writer.AppendLine();
    }

    internal static void WriteVariablesInitialization(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        foreach (var p in method.Parameters.Where(p => !WrapperEmitter.IsJitConstant(p)))
        {
            if (IsCustomStruct(p.Type))
            {
                writer.Append(
                    $"System.Collections.Generic.List<IntPtr> {p.Name}_allocatedPtrs = new System.Collections.Generic.List<IntPtr>");
                writer.AppendParentheses();

                writer.AppendLine(";");
                if (p.RefKind == RefKind.Ref || p.RefKind == RefKind.Out)
                {
                    writer.AppendLine($"IntPtr d_{p.Name} = IntPtr.Zero;");
                }
            }
            else if (!isDevicePtr &&
                     (p.Type is IArrayTypeSymbol || p.RefKind == RefKind.Ref || p.RefKind == RefKind.Out))
            {
                writer.AppendLine($"IntPtr d_{p.Name} = IntPtr.Zero;");
            }
        }
    }

    internal static void WriteMarshalAndLaunchKernel(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        var parameters = method.Parameters.Where(p => !WrapperEmitter.IsJitConstant(p)).ToList();
        writer.AppendLine($"void** kernelParams = stackalloc void*[{parameters.Count}];");

        for (var i = 0; i < parameters.Count; i++)
        {
            WriteParameterMarshal(writer, parameters[i], i, isDevicePtr);
        }

        writer.AppendLine();

        WriteLaunchKernelCall(writer, method);

        foreach (var p in method.Parameters.Where(p => !WrapperEmitter.IsJitConstant(p)))
        {
            WriteParameterUnmarshal(writer, p, isDevicePtr);
        }
    }

    internal static void WriteCleanup(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        foreach (var p in method.Parameters.Where(p => !WrapperEmitter.IsJitConstant(p)))
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
        var pTypeName = p.Type.ToDisplayString().Replace("global::", "").Replace(".", "_");
        writer.AppendLine(
            $"var native_{p.Name} = Marshal_{pTypeName}(ref {p.Name}, {(isDevicePtr ? "true" : "false")}, {p.Name}_allocatedPtrs);");

        if (p.RefKind == RefKind.Ref || p.RefKind == RefKind.Out)
        {
            writer.AppendLine($"""
                // Marshal ref/out struct: {p.Name}
                int {p.Name}_size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(__Native_{pTypeName}));
                CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_{p.Name}, {p.Name}_size));
                """);
            if (p.RefKind == RefKind.Ref)
            {
                writer.AppendLine($"""
                    // Fast zero-allocation copy to device
                    CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_{p.Name}, (IntPtr)(&native_{p.Name}), {p.Name}_size));
                    """);
            }

            writer.AppendLine($"kernelParams[{paramIndex}] = &d_{p.Name};");
        }
        else
        {
            writer.AppendLine($"// Marshal scalar struct: {p.Name}");
            writer.AppendLine($"kernelParams[{paramIndex}] = &native_{p.Name};");
        }
    }

    private static void WriteArrayMarshal(SourceWriter writer, IParameterSymbol p, IArrayTypeSymbol arrayType,
        int paramIndex, bool isDevicePtr)
    {
        if (isDevicePtr)
        {
            writer.AppendLine($"// Marshal device array: {p.Name}");
            writer.AppendLine($"kernelParams[{paramIndex}] = &{p.Name};");
        }
        else
        {
            writer.AppendLine($"// Marshal array: {p.Name}");
            writer.AppendLine(
                $"int {p.Name}_size = {p.Name}.Length * System.Runtime.InteropServices.Marshal.SizeOf(typeof({arrayType.ElementType.ToDisplayString()}));");
            writer.AppendKeyword("if");
            using (writer.Parentheses())
            {
                writer.Append($"{p.Name}_size");
                writer.AppendOperator(">");
                writer.Append("0");
            }
            writer.AppendLine();
            using (writer.Scope())
            {
                writer.AppendLine($"""
                    CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_{p.Name}, {p.Name}_size));
                    System.Runtime.InteropServices.GCHandle handle_{p.Name} = System.Runtime.InteropServices.GCHandle.Alloc({p.Name}, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try
                    """);
                using (writer.Scope())
                {
                    writer.AppendLine($"""
                        IntPtr p_{p.Name} = handle_{p.Name}.AddrOfPinnedObject();
                        CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_{p.Name}, p_{p.Name}, {p.Name}_size));
                        """);
                }

                writer.AppendLine("finally");
                using (writer.Scope())
                {
                    writer.AppendLine($"handle_{p.Name}.Free();");
                }
            }

            writer.AppendLine($"kernelParams[{paramIndex}] = &d_{p.Name};");
        }
    }

    private static void WriteRefOrOutScalarMarshal(SourceWriter writer, IParameterSymbol p, int paramIndex)
    {
        var typeString = p.Type.ToDisplayString();
        writer.AppendLine($"""
            // Marshal ref/out scalar: {p.Name}
            int {p.Name}_size = System.Runtime.InteropServices.Marshal.SizeOf(typeof({typeString}));
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_{p.Name}, {p.Name}_size));
            """);

        if (p.RefKind == RefKind.Ref)
        {
            writer.AppendLine($"""
                // Fast zero-allocation copy to device
                fixed ({typeString}* p_{p.Name}_temp = &{p.Name})
                """);
            using (writer.Scope())
            {
                writer.AppendLine($"""
                    CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_{p.Name}, (IntPtr)p_{p.Name}_temp, {p.Name}_size));
                    """);
            }
        }

        writer.AppendLine($"kernelParams[{paramIndex}] = &d_{p.Name};");
    }

    private static void WriteScalarMarshal(SourceWriter writer, IParameterSymbol p, int paramIndex)
    {
        writer.AppendLine($"// Marshal scalar: {p.Name}");
        writer.AppendLine($"kernelParams[{paramIndex}] = &{p.Name};");
    }

    private static void WriteLaunchKernelCall(SourceWriter writer, IMethodSymbol method)
    {
        var isCooperative = false;
        foreach (var attr in method.GetAttributes())
        {
            if (!KnownAttributes.IsGpuKernel(attr.AttributeClass?.Name))
            {
                continue;
            }

            var namedArg = attr.NamedArguments.FirstOrDefault(na => na.Key == "Cooperative");
            if (namedArg.Value.Value is true)
            {
                isCooperative = true;
            }

            break;
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
                if (isCooperative)
                {
                    writer.AppendLine("IntPtr.Zero, // Stream");
                    writer.Append("kernelParams");
                }
                else
                {
                    writer.AppendLine("IntPtr.Zero, // Stream");
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
        var pTypeName = p.Type.ToDisplayString().Replace("global::", "").Replace(".", "_");
        if (p.RefKind == RefKind.Ref || p.RefKind == RefKind.Out)
        {
            writer.AppendLine($"""
                // Fast zero-allocation copy from device
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)(&native_{p.Name}), d_{p.Name}, {p.Name}_size));
                """);
        }

        writer.AppendLine(
            $"Unmarshal_{pTypeName}(ref {p.Name}, ref native_{p.Name}, {(isDevicePtr ? "true" : "false")});");
    }

    private static void WriteArrayUnmarshal(SourceWriter writer, IParameterSymbol p, bool isDevicePtr)
    {
        if (!isDevicePtr)
        {
            writer.AppendLine($"// Copy back array: {p.Name}");
            writer.AppendKeyword("if");
            using (writer.Parentheses())
            {
                writer.Append($"{p.Name}_size");
                writer.AppendOperator(">");
                writer.Append("0");
                writer.AppendOperator("&&");
                writer.Append($"d_{p.Name}");
                writer.AppendOperator("!=");
                writer.Append("IntPtr.Zero");
            }
            writer.AppendLine();
            using (writer.Scope())
            {
                writer.AppendLine($"""
                    System.Runtime.InteropServices.GCHandle handleBack_{p.Name} = System.Runtime.InteropServices.GCHandle.Alloc({p.Name}, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try
                    """);
                using (writer.Scope())
                {
                    writer.AppendLine($"""
                        IntPtr p_{p.Name} = handleBack_{p.Name}.AddrOfPinnedObject();
                        CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(p_{p.Name}, d_{p.Name}, {p.Name}_size));
                        """);
                }

                writer.AppendLine("finally");
                using (writer.Scope())
                {
                    writer.AppendLine($"handleBack_{p.Name}.Free();");
                }
            }
        }
    }

    private static void WriteRefOrOutScalarUnmarshal(SourceWriter writer, IParameterSymbol p)
    {
        var typeString = p.Type.ToDisplayString();

        if (p.RefKind == RefKind.Out)
        {
            writer.AppendLine($"{p.Name} = default;");
        }

        writer.AppendLine($"""
            // Fast zero-allocation copy from device for scalar: {p.Name}
            fixed ({typeString}* ptr_{p.Name} = &{p.Name})
            """);
        using (writer.Scope())
        {
            writer.AppendLine($"""
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)ptr_{p.Name}, d_{p.Name}, {p.Name}_size));
                """);
        }
    }

    internal static void WriteParameterFree(SourceWriter writer, IParameterSymbol p, bool isDevicePtr)
    {
        if (IsCustomStruct(p.Type))
        {
            writer.AppendLine($"foreach (var ptr in {p.Name}_allocatedPtrs) CudaDriverApi.cuMemFree_v2(ptr);");
            if (p.RefKind == RefKind.Ref || p.RefKind == RefKind.Out)
            {
                writer.AppendLine($"if (d_{p.Name} != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_{p.Name});");
            }
        }
        else if (p.Type is IArrayTypeSymbol)
        {
            if (!isDevicePtr)
            {
                writer.AppendLine($"if (d_{p.Name} != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_{p.Name});");
            }
        }
        else if (p.RefKind == RefKind.Ref || p.RefKind == RefKind.Out)
        {
            writer.AppendLine($"if (d_{p.Name} != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_{p.Name});");
        }
    }
}
