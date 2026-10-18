namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits P/Invoke wrapper logic for launching CUDA kernels, including memory allocation, marshaling, and stream synchronization.</summary>
internal static class KernelLaunchEmitter
{
    /// <summary>Emits local variable declarations for device pointers and tracking lists.</summary>
    internal static void WriteVariablesInitialization(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        foreach (var p in method.Parameters)
        {
            if (CudaLayoutRules.IsCustomStruct(p.Type))
            {
                if (!isDevicePtr)
                {
                    writer.AppendLine(
                        $"System.Collections.Generic.List<IntPtr> {p.Name}_allocatedPtrs =" +
                        " new System.Collections.Generic.List<IntPtr>();");
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
            else if ((!isDevicePtr && IsArrayLikeType(p.Type, out _)) ||
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

    /// <summary>Emits argument marshaling, device memory transfers, kernel launch, and result copyback.</summary>
    internal static void WriteMarshalAndLaunchKernel(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        var kernelParamCount = method.Parameters.Length;

        if (method.ReturnType.SpecialType != SpecialType.System_Void)
        {
            kernelParamCount++;
        }

        writer.Append("void** kernelParams = stackalloc void*[");
        writer.Append(kernelParamCount.ToString());
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
                    "CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(" +
                    "out d___gsharp_auto_result, (IntPtr)__gsharp_auto_result_size, hStream));");
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
                    "var handle = System.Runtime.InteropServices.GCHandle.Alloc(" +
                    "__gsharp_auto_result, System.Runtime.InteropServices.GCHandleType.Pinned);");
                writer.AppendLine("try");
                using (writer.Scope())
                {
                    writer.AppendLine(
                        "CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2(" +
                        "handle.AddrOfPinnedObject(), d___gsharp_auto_result, (IntPtr)size, hStream));");
                    writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
                }

                writer.AppendLine("finally");
                using (writer.Scope())
                {
                    writer.AppendLine("handle.Free();");
                }
            }
        }
    }

    /// <summary>Emits cleanup code to asynchronously free allocated device memory buffers.</summary>
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
            writer.AppendLine(
                "CudaException.ThrowIfError(CudaDriverApi.cuMemFreeAsync(d___gsharp_auto_result, hStream));");
        }
    }

    internal static bool IsArrayLikeType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        if (type is INamedTypeSymbol { IsGenericType: true } genericType &&
            genericType.Name is TypeConstants.SpanType or TypeConstants.ReadOnlySpanType or "Memory" or "ReadOnlyMemory" or "ArraySegment" or "ImmutableArray" &&
            (genericType.ContainingNamespace.IsSystemNamespace() ||
             genericType.ContainingNamespace is
             {
                 Name: "Immutable",
                 ContainingNamespace:
                 {
                     Name: "Collections",
                     ContainingNamespace: { Name: TypeConstants.SystemNamespace, ContainingNamespace.IsGlobalNamespace: true }
                 }
             }))
        {
            elementType = genericType.TypeArguments[0];
            return true;
        }

        elementType = null!;
        return false;
    }

    private static string GetSpanExpression(IParameterSymbol p)
    {
        if (p.Type is INamedTypeSymbol { Name: "Memory" or "ReadOnlyMemory" })
        {
            return $"{p.Name}.Span";
        }
        else if (p.Type is INamedTypeSymbol { Name: "ArraySegment" or "ImmutableArray" })
        {
            return $"{p.Name}.AsSpan()";
        }

        return p.Name;
    }

    private static void WriteParameterMarshal(SourceWriter writer, IParameterSymbol p, int paramIndex, bool isDevicePtr)
    {
        if (isDevicePtr && (IsArrayLikeType(p.Type, out _) || CudaLayoutRules.IsCustomStruct(p.Type) ||
                            p.RefKind is RefKind.Ref or RefKind.Out))
        {
            var kind = IsArrayLikeType(p.Type, out _) ? "array" :
                CudaLayoutRules.IsCustomStruct(p.Type) ? "struct" : "scalar";
            writer.AppendLine($"// Marshal device {kind}: {p.Name}");
            if (kind == "array")
            {
                writer.AppendLine($"IntPtr* proxy_{p.Name} = stackalloc IntPtr[2];");
                writer.AppendLine($"proxy_{p.Name}[0] = {p.Name};");
                writer.AppendLine($"((int*)(proxy_{p.Name} + 1))[0] = 0; // Length is unknown for raw IntPtr");
                writer.AppendLine($"kernelParams[{paramIndex}] = proxy_{p.Name};");
            }
            else
            {
                writer.AppendLine($"kernelParams[{paramIndex}] = &{p.Name};");
            }

            return;
        }

        if (CudaLayoutRules.IsCustomStruct(p.Type))
        {
            WriteCustomStructMarshal(writer, p, paramIndex, isDevicePtr);
        }
        else if (IsArrayLikeType(p.Type, out var elementType))
        {
            WriteArrayMarshal(writer, p, elementType, paramIndex);
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
        writer.AppendAotStructPath(p.Type);
        writer.AppendLine($" native_{p.Name} = default;");
        writer.Append("AotContexts.Marshal_");
        writer.AppendNestedCppName(p.Type);
        using (writer.Parentheses())
        {
            writer.Append(
                $"ref {p.Name}, ref native_{p.Name}, {(isDevicePtr ? "true" : "false")}, {(isDevicePtr ? "null" : $"{p.Name}_allocatedPtrs")}, hStream");
        }

        writer.AppendLine(";");

        if (p.RefKind is RefKind.Ref or RefKind.Out)
        {
            writer.AppendLine($"// Marshal ref/out struct: {p.Name}");
            writer.Append($"int {p.Name}_size = sizeof(");
            writer.AppendAotStructPath(p.Type);
            writer.AppendLine(");");

            writer.AppendLine(
                $"CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out d_{p.Name}, (IntPtr){p.Name}_size, hStream));");
            if (p.RefKind == RefKind.Ref)
            {
                writer.AppendLine("// Fast zero-allocation copy to device");
                writer.AppendLine(
                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(d_{p.Name}, " +
                    $"(IntPtr)(&native_{p.Name}), (IntPtr){p.Name}_size, hStream));");
                writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
            }

            writer.AppendLine($"kernelParams[{paramIndex}] = &d_{p.Name};");
        }
        else
        {
            writer.AppendLine($"// Marshal scalar struct: {p.Name}");
            writer.AppendLine($"kernelParams[{paramIndex}] = &native_{p.Name};");
        }
    }

    private static void WriteArrayMarshal(SourceWriter writer, IParameterSymbol p, ITypeSymbol elementType,
        int paramIndex)
    {
        GetArrayDirection(p, out var hasIn, out var hasOut);

        var isOutOnly = hasOut && !hasIn;

        writer.AppendLine($"// Marshal array: {p.Name}");
        writer.Append($"int {p.Name}_size = {p.Name}.Length * sizeof(");
        writer.AppendFullyQualifiedCsharpName(elementType);
        writer.AppendLine(");");

        writer.AppendLine($"IntPtr* proxy_{p.Name} = stackalloc IntPtr[2];");
        writer.AppendLine($"proxy_{p.Name}[0] = IntPtr.Zero;");
        writer.AppendLine($"((int*)(proxy_{p.Name} + 1))[0] = {p.Name}.Length;");

        writer.AppendLine($"if ({p.Name}_size > 0)");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out d_{p.Name}, (IntPtr){p.Name}_size, hStream));");
            writer.AppendLine($"proxy_{p.Name}[0] = d_{p.Name};");

            if (isOutOnly)
            {
                writer.AppendLine($"// Skip HtoD copy for [Out] array: {p.Name}");
            }
            else
            {
                var spanExpr = GetSpanExpression(p);
                writer.Append("fixed (");
                writer.AppendFullyQualifiedCsharpName(elementType);
                writer.AppendLine($"* p_{p.Name} = {spanExpr})");
                using (writer.Scope())
                {
                    writer.AppendLine(
                        $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(" +
                        $"d_{p.Name}, (IntPtr)p_{p.Name}, (IntPtr){p.Name}_size, hStream));");
                    writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
                }
            }
        }

        writer.AppendLine($"kernelParams[{paramIndex}] = proxy_{p.Name};");
    }

    private static void WriteRefOrOutScalarMarshal(SourceWriter writer, IParameterSymbol p, int paramIndex)
    {
        writer.AppendLine($"// Marshal ref/out scalar: {p.Name}");
        writer.Append($"int {p.Name}_size = sizeof(");
        writer.AppendFullyQualifiedCsharpName(p.Type);
        writer.AppendLine(");");
        writer.AppendLine(
            $"CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out d_{p.Name}, (IntPtr){p.Name}_size, hStream));");

        if (p.RefKind == RefKind.Ref)
        {
            writer.AppendLine("// Fast zero-allocation copy to device");
            writer.Append("fixed (");
            writer.AppendFullyQualifiedCsharpName(p.Type);
            writer.AppendLine($"* p_{p.Name}_temp = &{p.Name})");
            using (writer.Scope())
            {
                writer.AppendLine(
                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(d_{p.Name}," +
                    $" (IntPtr)p_{p.Name}_temp, (IntPtr){p.Name}_size, hStream));");
                writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
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
                writer.AppendLine("hStream, // Stream");
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

    public static void WriteParameterUnmarshal(SourceWriter writer, IParameterSymbol p, bool isDevicePtr)
    {
        if (isDevicePtr && (IsArrayLikeType(p.Type, out _) || CudaLayoutRules.IsCustomStruct(p.Type) ||
                            p.RefKind is RefKind.Ref or RefKind.Out))
        {
            return;
        }

        if (CudaLayoutRules.IsCustomStruct(p.Type))
        {
            WriteCustomStructUnmarshal(writer, p, isDevicePtr);
        }
        else if (IsArrayLikeType(p.Type, out _))
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
                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2((IntPtr)(&native_{p.Name})," +
                    $" d_{p.Name}, (IntPtr){p.Name}_size, hStream));");
                writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
            }
        }

        writer.Append("AotContexts.Unmarshal_");
        writer.AppendNestedCppName(p.Type);
        using (writer.Parentheses())
        {
            writer.Append($"ref {p.Name}, ref native_{p.Name}, {(isDevicePtr ? "true" : "false")}, hStream");
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
            var spanExpr = GetSpanExpression(p);
            writer.Append("fixed (");
            IsArrayLikeType(p.Type, out var elementType);
            writer.AppendFullyQualifiedCsharpName(elementType);
            writer.AppendLine($"* p_{p.Name} = {spanExpr})");
            using (writer.Scope())
            {
                writer.AppendLine(
                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2((IntPtr)p_{p.Name}, d_{p.Name}, (IntPtr){p.Name}_size, hStream));");
                writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
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
                "CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2(" +
                $"(IntPtr)ptr_{p.Name}, d_{p.Name}, (IntPtr){p.Name}_size, hStream));");
            writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
        }
    }

    private static void WriteParameterFree(SourceWriter writer, IParameterSymbol p, bool isDevicePtr)
    {
        if (isDevicePtr)
        {
            return;
        }

        if (CudaLayoutRules.IsCustomStruct(p.Type))
        {
            writer.AppendLine(
                $"foreach (var ptr in {p.Name}_allocatedPtrs) CudaDriverApi.cuMemFreeAsync(ptr, hStream);");
        }

        if (p.Type is IArrayTypeSymbol || p.RefKind is RefKind.Ref or RefKind.Out)
        {
            writer.AppendLine($"if (d_{p.Name} != IntPtr.Zero) CudaDriverApi.cuMemFreeAsync(d_{p.Name}, hStream);");
        }
    }

    private static void GetArrayDirection(IParameterSymbol p, out bool hasIn, out bool hasOut)
    {
        hasIn = false;
        hasOut = false;
        foreach (var attr in p.GetAttributes())
        {
            var cls = attr.AttributeClass;
            if (cls?.ContainingNamespace.IsSystemRuntimeInteropServices() != true)
            {
                continue;
            }

            if (attr.IsAttribute(KnownAttributes.In))
            {
                hasIn = true;
            }
            else if (attr.IsAttribute(KnownAttributes.Out))
            {
                hasOut = true;
            }
        }
    }
}
