
namespace Kassyi.GSharp.Compiler;

/// <summary>Emits native P/Invoke marshaling code, pinned struct conversions, and CUDA parameter setup.</summary>
/// <remarks><para>Implements: <b>GEN-34, GEN-36</b></para></remarks>
internal static class MarshalingEmitter
{
    internal static bool IsCustomStruct(ITypeSymbol type)
    {
        // Exclude Kassyi.GSharp.Core vector types to bypass the CLR Explicit nesting bug.
        // Nesting explicit structs can silently corrupt data (zeroed fields) due to runtime memory mapping issues.
        // Instead, we flatten them into fixed byte arrays and use pointer casts for direct binary copying.
        return type is INamedTypeSymbol { TypeKind: TypeKind.Struct } named &&
               !named.ContainingNamespace.IsSystemOrNumerics() &&
               !named.ContainingNamespace.IsCoreNamespace();
    }

    internal static void CollectStructs(ITypeSymbol type, HashSet<INamedTypeSymbol> structs)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } named ||
            named.ContainingNamespace.IsSystemOrNumerics() ||
            named.ContainingNamespace.IsCoreNamespace())
        {
            return;
        }

        if (!structs.Add(named))
        {
            return;
        }

        foreach (var member in named.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false } field)
            {
                CollectStructs(field.Type, structs);
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

    private static (int Size, int Alignment)? GetPredefinedLayout(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
        {
            return (8, 8); // Device pointer size and alignment in 64-bit CUDA
        }

        var primitive = type.SpecialType switch
        {
            SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => (1, 1),
            SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16 => (2, 2),
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => (4, 4),
            SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double => (8, 8),
            _ => default((int, int)?),
        };

        if (primitive.HasValue)
        {
            return primitive;
        }

        if (type.ContainingNamespace.IsCoreNamespace())
        {
            return type.Name switch
            {
                "Float2" or "Int2" or "UInt2" => (8, 8),
                "Float3" or "Int3" or "UInt3" => (12, 4),
                "Float4" or "Int4" or "UInt4" => (16, 16),
                "Double2" or "Long2" or "ULong2" => (16, 16),
                "Double3" or "Long3" or "ULong3" => (24, 8),
                "Double4" or "Long4" or "ULong4" => (32, 16),
                _ => null,
            };
        }

        return null;
    }

    private static int AlignUp(int offset, int alignment)
    {
        var remainder = offset % alignment;
        return remainder == 0 ? offset : offset + (alignment - remainder);
    }

    private static (int Size, int Alignment) GetCudaLayout(ITypeSymbol type)
    {
        if (GetPredefinedLayout(type) is { } predefined)
        {
            return predefined;
        }

        // Fallback for custom structs
        if (type is not INamedTypeSymbol namedStruct)
        {
            return (4, 4);
        }

        var size = 0;
        var maxAlign = 1;

        foreach (var member in namedStruct.GetMembers())
        {
            if (member is not IFieldSymbol { IsStatic: false } structField)
            {
                continue;
            }

            var (fieldSize, fieldAlign) = GetCudaLayout(structField.Type);
            size = AlignUp(size, fieldAlign) + fieldSize;
            maxAlign = Math.Max(maxAlign, fieldAlign);
        }

        size = AlignUp(size, maxAlign);
        return (Math.Max(size, 1), maxAlign);
    }

    private static void WriteNativeStructDeclaration(SourceWriter writer, INamedTypeSymbol s)
    {
        var (totalSize, _) = GetCudaLayout(s);

        writer.AppendLine(
            $"[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = {totalSize})]");
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

                var (fieldSize, fieldAlign) = GetCudaLayout(field.Type);
                currentOffset = AlignUp(currentOffset, fieldAlign);

                writer.AppendLine(
                    $"public const string DebugName_{field.Name} = \"{field.Type.GetFullyQualifiedName()}\";");
                writer.AppendLine($"[System.Runtime.InteropServices.FieldOffset({currentOffset})]");

                if (field.Type is IArrayTypeSymbol)
                {
                    writer.AppendLine($"public IntPtr {field.Name};");
                }
                else if (IsCustomStruct(field.Type))
                {
                    writer.Append("public __Native_");
                    writer.AppendFullyQualifiedCppName(field.Type);
                    writer.AppendLine($" {field.Name};");
                }
                else
                {
                    if (field.Type.IsReferenceType)
                    {
                        writer.AppendLine($"#error Unsupported reference type in struct field: {field.Name}");
                    }

                    if (field.Type.SpecialType != SpecialType.None)
                    {
                        writer.Append("public ");
                        writer.AppendFullyQualifiedCsharpName(field.Type);
                        writer.AppendLine($" {field.Name};");
                    }
                    else
                    {
                        writer.AppendLine($"public fixed byte {field.Name}[{fieldSize}];");
                    }
                }

                currentOffset += fieldSize;
            }
        }

        writer.AppendLine();
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
        else if (IsCustomStruct(member.Type))
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
        else if (IsCustomStruct(member.Type))
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

    private static void WriteArrayElementSize(SourceWriter writer, string prefix, string name,
        IArrayTypeSymbol arrayType)
    {
        if (IsCustomStruct(arrayType.ElementType))
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

    internal static void WriteVariablesInitialization(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        foreach (var p in method.Parameters)
        {
            if (IsCustomStruct(p.Type))
            {
                if (!isDevicePtr)
                {
                    writer.AppendLine(
                        $"System.Collections.Generic.List<IntPtr> {p.Name}_allocatedPtrs = new System.Collections.Generic.List<IntPtr>();");
                }

                if (p.RefKind is not (RefKind.Ref or RefKind.Out))
                {
                    continue;
                }

                if (!isDevicePtr)
                {
                    writer.AppendLine($"IntPtr d_{p.Name} = IntPtr.Zero;");
                }
            }
            else if ((!isDevicePtr && p.Type is IArrayTypeSymbol) ||
                     (!isDevicePtr && p.RefKind is RefKind.Ref or RefKind.Out))
            {
                writer.AppendLine($"IntPtr d_{p.Name} = IntPtr.Zero;");
            }
        }

        if (method.ReturnType.SpecialType != SpecialType.System_Void)
        {
            writer.AppendLine("IntPtr d___gsharp_auto_result = IntPtr.Zero;");
        }
    }

    internal static void WriteMarshalAndLaunchKernel(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        var nonJitParamCount = method.Parameters.Length;

        if (method.ReturnType.SpecialType != SpecialType.System_Void)
        {
            nonJitParamCount++;
        }

        writer.Append("void** kernelParams = stackalloc void*[");
        writer.Append(nonJitParamCount.ToString());
        writer.AppendLine("];");

        var paramIndex = 0;
        foreach (var p in method.Parameters)
        {
            WriteParameterMarshal(writer, p, paramIndex++, isDevicePtr);
        }

        if (method.ReturnType.SpecialType != SpecialType.System_Void)
        {
            writer.AppendLine("// Marshal auto result array");
            writer.Append("int __gsharp_auto_result_size = __gsharp_auto_result.Length * sizeof(");
            writer.AppendFullyQualifiedCsharpName(method.ReturnType);
            writer.AppendLine(");");

            writer.AppendLine("if (__gsharp_auto_result_size > 0)");
            using (writer.Scope())
            {
                writer.AppendLine(
                    "CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d___gsharp_auto_result, __gsharp_auto_result_size));");
                writer.AppendLine("kernelParams[" + paramIndex + "] = &d___gsharp_auto_result;");
            }
        }

        writer.AppendLine();

        WriteLaunchKernelCall(writer, method);

        foreach (var p in method.Parameters)
        {
            WriteParameterUnmarshal(writer, p, isDevicePtr);
        }

        if (method.ReturnType.SpecialType == SpecialType.System_Void)
        {
            return;
        }

        writer.AppendLine("if (__gsharp_auto_result != null && d___gsharp_auto_result != IntPtr.Zero)");
        using (writer.Scope())
        {
            writer.Append("int size = __gsharp_auto_result.Length * sizeof(");
            writer.AppendFullyQualifiedCsharpName(method.ReturnType);
            writer.AppendLine(");");

            writer.AppendLine("if (size > 0)");
            using (writer.Scope())
            {
                writer.AppendLine(
                    "var handle = System.Runtime.InteropServices.GCHandle.Alloc(__gsharp_auto_result, System.Runtime.InteropServices.GCHandleType.Pinned);");
                writer.AppendLine("try");
                using (writer.Scope())
                {
                    writer.AppendLine(
                        "CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(handle.AddrOfPinnedObject(), d___gsharp_auto_result, size));");
                }

                writer.AppendLine("finally");
                using (writer.Scope())
                {
                    writer.AppendLine("handle.Free();");
                }
            }
        }
    }

    internal static void WriteCleanup(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        foreach (var p in method.Parameters)
        {
            WriteParameterFree(writer, p, isDevicePtr);
        }

        if (method.ReturnType.SpecialType == SpecialType.System_Void)
        {
            return;
        }

        writer.AppendLine("if (d___gsharp_auto_result != IntPtr.Zero)");
        using (writer.Scope())
        {
            writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuMemFree_v2(d___gsharp_auto_result));");
        }
    }

    private static void WriteParameterMarshal(SourceWriter writer, IParameterSymbol p, int paramIndex, bool isDevicePtr)
    {
        if (isDevicePtr && (p.Type is IArrayTypeSymbol || p.RefKind is RefKind.Ref or RefKind.Out))
        {
            var kind = p.Type is IArrayTypeSymbol ? "array" : IsCustomStruct(p.Type) ? "struct" : "scalar";
            writer.AppendLine($"// Marshal device {kind}: {p.Name}");
            writer.AppendLine($"kernelParams[{paramIndex}] = &{p.Name};");
            return;
        }

        if (IsCustomStruct(p.Type))
        {
            WriteCustomStructMarshal(writer, p, paramIndex, isDevicePtr);
        }
        else if (p.Type is IArrayTypeSymbol arrayType)
        {
            WriteArrayMarshal(writer, p, arrayType, paramIndex);
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
        writer.Append($"__Native_");
        writer.AppendFullyQualifiedCppName(p.Type);
        writer.AppendLine($" native_{p.Name} = default;");
        writer.Append($"Marshal_");
        writer.AppendFullyQualifiedCppName(p.Type);
        using (writer.Parentheses())
        {
            writer.Append(
                $"ref {p.Name}, ref native_{p.Name}, {(isDevicePtr ? "true" : "false")}, {(isDevicePtr ? "null" : $"{p.Name}_allocatedPtrs")}");
        }

        writer.AppendLine(";");

        if (p.RefKind is RefKind.Ref or RefKind.Out)
        {
            writer.AppendLine($"// Marshal ref/out struct: {p.Name}");
            writer.Append($"int {p.Name}_size = sizeof(__Native_");
            writer.AppendFullyQualifiedCppName(p.Type);
            writer.AppendLine(");");

            writer.AppendLine(
                $"CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_{p.Name}, {p.Name}_size));");
            if (p.RefKind == RefKind.Ref)
            {
                writer.AppendLine("// Fast zero-allocation copy to device");
                writer.AppendLine(
                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_{p.Name}, (IntPtr)(&native_{p.Name}), {p.Name}_size));");
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
        int paramIndex)
    {
        GetArrayDirection(p, out var hasIn, out var hasOut);

        var isOutOnly = hasOut && !hasIn;

        writer.AppendLine($"// Marshal array: {p.Name}");
        writer.Append($"int {p.Name}_size = {p.Name}.Length * sizeof(");
        writer.AppendFullyQualifiedCsharpName(arrayType.ElementType);
        writer.AppendLine(");");

        writer.AppendLine($"if ({p.Name}_size > 0)");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_{p.Name}, {p.Name}_size));");

            if (isOutOnly)
            {
                writer.AppendLine($"// Skip HtoD copy for [Out] array: {p.Name}");
            }
            else
            {
                writer.AppendLine(
                    $"System.Runtime.InteropServices.GCHandle handle_{p.Name} = System.Runtime.InteropServices.GCHandle.Alloc({p.Name}, System.Runtime.InteropServices.GCHandleType.Pinned);");

                writer.AppendLine("try");
                using (writer.Scope())
                {
                    writer.AppendLine($"IntPtr p_{p.Name} = handle_{p.Name}.AddrOfPinnedObject();");
                    writer.AppendLine(
                        $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_{p.Name}, p_{p.Name}, {p.Name}_size));");
                }

                writer.AppendLine("finally");
                using (writer.Scope())
                {
                    writer.AppendLine($"handle_{p.Name}.Free();");
                }
            }
        }

        writer.AppendLine($"kernelParams[{paramIndex}] = &d_{p.Name};");
    }

    private static void WriteRefOrOutScalarMarshal(SourceWriter writer, IParameterSymbol p, int paramIndex)
    {
        writer.AppendLine($"// Marshal ref/out scalar: {p.Name}");
        writer.Append($"int {p.Name}_size = sizeof(");
        writer.AppendFullyQualifiedCsharpName(p.Type);
        writer.AppendLine(");");
        writer.AppendLine(
            $"CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_{p.Name}, {p.Name}_size));");

        if (p.RefKind == RefKind.Ref)
        {
            writer.AppendLine("// Fast zero-allocation copy to device");
            writer.Append("fixed (");
            writer.AppendFullyQualifiedCsharpName(p.Type);
            writer.AppendLine($"* p_{p.Name}_temp = &{p.Name})");
            using (writer.Scope())
            {
                writer.AppendLine(
                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_{p.Name}, (IntPtr)p_{p.Name}_temp, {p.Name}_size));");
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
        var kernelAttr = method.GetAttribute(KnownAttributes.GpuKernel);
        if (kernelAttr != null)
        {
            foreach (var namedArg in kernelAttr.NamedArguments)
            {
                if (namedArg is { Key: "Cooperative", Value.Value: true })
                {
                    isCooperative = true;
                    break;
                }
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
            WriteRefOrOutScalarUnmarshal(writer, p, isDevicePtr);
        }
    }

    private static void WriteCustomStructUnmarshal(SourceWriter writer, IParameterSymbol p, bool isDevicePtr)
    {
        if (isDevicePtr && (p.RefKind is RefKind.Ref or RefKind.Out))
        {
            return;
        }

        if (p.RefKind is RefKind.Ref or RefKind.Out)
        {
            if (!isDevicePtr)
            {
                writer.AppendLine("// Fast zero-allocation copy from device");
                writer.AppendLine(
                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)(&native_{p.Name}), d_{p.Name}, {p.Name}_size));");
            }
        }

        writer.Append("Unmarshal_");
        writer.AppendFullyQualifiedCppName(p.Type);
        using (writer.Parentheses())
        {
            writer.Append($"ref {p.Name}, ref native_{p.Name}, {(isDevicePtr ? "true" : "false")}");
        }

        writer.AppendLine(";");
    }

    private static void WriteArrayUnmarshal(SourceWriter writer, IParameterSymbol p, bool isDevicePtr)
    {
        if (isDevicePtr)
        {
            return;
        }

        GetArrayDirection(p, out var hasIn, out var hasOut);

        var isInOnly = hasIn && !hasOut;

        if (isInOnly)
        {
            writer.AppendLine($"// Skip DtoH copy for [In] array: {p.Name}");
            return;
        }

        writer.AppendLine($"// Copy back array: {p.Name}");
        writer.AppendLine($"if ({p.Name}_size > 0 && d_{p.Name} != IntPtr.Zero)");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"System.Runtime.InteropServices.GCHandle handleBack_{p.Name} = System.Runtime.InteropServices.GCHandle.Alloc({p.Name}, System.Runtime.InteropServices.GCHandleType.Pinned);");
            writer.AppendLine("try");
            using (writer.Scope())
            {
                writer.AppendLine($"IntPtr p_{p.Name} = handleBack_{p.Name}.AddrOfPinnedObject();");
                writer.AppendLine(
                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(p_{p.Name}, d_{p.Name}, {p.Name}_size));");
            }

            writer.AppendLine("finally");
            using (writer.Scope())
            {
                writer.AppendLine($"handleBack_{p.Name}.Free();");
            }
        }
    }

    private static void WriteRefOrOutScalarUnmarshal(SourceWriter writer, IParameterSymbol p, bool isDevicePtr)
    {
        if (isDevicePtr)
        {
            return;
        }

        if (p.RefKind == RefKind.Out)
        {
            writer.AppendLine($"{p.Name} = default;");
        }

        writer.AppendLine($"// Fast zero-allocation copy from device for scalar: {p.Name}");
        writer.Append("fixed (");
        writer.AppendFullyQualifiedCsharpName(p.Type);
        writer.AppendLine($"* ptr_{p.Name} = &{p.Name})");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)ptr_{p.Name}, d_{p.Name}, {p.Name}_size));");
        }
    }

    internal static void WriteParameterFree(SourceWriter writer, IParameterSymbol p, bool isDevicePtr)
    {
        if (isDevicePtr)
        {
            return;
        }

        if (IsCustomStruct(p.Type))
        {
            writer.AppendLine($"foreach (var ptr in {p.Name}_allocatedPtrs) CudaDriverApi.cuMemFree_v2(ptr);");
        }

        if (p.Type is IArrayTypeSymbol || p.RefKind is RefKind.Ref or RefKind.Out)
        {
            writer.AppendLine($"if (d_{p.Name} != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_{p.Name});");
        }
    }

    private static void GetArrayDirection(IParameterSymbol p, out bool hasIn, out bool hasOut)
    {
        hasIn = false;
        hasOut = false;
        foreach (var attr in p.GetAttributes())
        {
            var cls = attr.AttributeClass;

            if (cls?.Name is not ("InAttribute" or "OutAttribute") ||
                cls.ContainingNamespace?.GetFullNamespaceName() != "System.Runtime.InteropServices")
            {
                continue;
            }

            switch (cls.Name)
            {
                case "InAttribute":
                    hasIn = true;
                    break;
                case "OutAttribute":
                    hasOut = true;
                    break;
            }
        }
    }
}
