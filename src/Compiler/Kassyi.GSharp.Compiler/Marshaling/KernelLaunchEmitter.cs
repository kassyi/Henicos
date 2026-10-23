namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits P/Invoke wrapper logic for launching CUDA kernels, including memory allocation, marshaling, and stream synchronization.</summary>
internal static class KernelLaunchEmitter
{
    /// <summary>The device copy of the auto result buffer in the generated launcher.</summary>
    private const string DeviceAutoResult = "d_" + GSharpInternalNames.ReturnValue.Buffer;

    /// <summary>Emits local variable declarations for device pointers and tracking lists.</summary>
    internal static void WriteVariablesInitialization(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        foreach (var p in method.Parameters)
        {
            if (isDevicePtr)
            {
                continue;
            }

            var shape = KernelParameterShape.Classify(p);
            if (shape.IsCustomStruct)
            {
                writer.AppendLine(
                    $"System.Collections.Generic.List<IntPtr> {p.Name}_allocatedPtrs =" +
                    " new System.Collections.Generic.List<IntPtr>();");
            }

            if (shape.IsArray || shape.IsByRef)
            {
                writer.AppendLine($"IntPtr d_{p.Name} = IntPtr.Zero;");
            }
        }

        if (method.ReturnType.SpecialType != SpecialType.System_Void)
        {
            writer.AppendLine($"IntPtr {DeviceAutoResult} = IntPtr.Zero;");
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
            WriteParameterMarshal(writer, KernelParameterShape.Classify(p), paramIndex++, isDevicePtr);
        }

        if (method.ReturnType.SpecialType != SpecialType.System_Void)
        {
            writer.AppendLine("// Marshal auto result array");
            writer.Append($"int {GSharpInternalNames.ReturnValue.BufferSize} = {GSharpInternalNames.ReturnValue.Buffer}.Length * sizeof(");
            writer.AppendFullyQualifiedCsharpName(method.ReturnType);
            writer.AppendLine(");");

            writer.AppendLine($"if ({GSharpInternalNames.ReturnValue.BufferSize} > 0)");
            using (writer.Scope())
            {
                writer.AppendLine(
                    "CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(" +
                    $"out {DeviceAutoResult}, (IntPtr){GSharpInternalNames.ReturnValue.BufferSize}, hStream));");
                writer.AppendLine("kernelParams[" + paramIndex + $"] = &{DeviceAutoResult};");
            }
        }

        writer.AppendLine();

        WriteLaunchKernelCall(writer, method);

        foreach (var p in method.Parameters)
        {
            WriteParameterUnmarshal(writer, KernelParameterShape.Classify(p), isDevicePtr);
        }

        if (method.ReturnType.SpecialType == SpecialType.System_Void)
        {
            return;
        }

        writer.AppendLine($"if ({GSharpInternalNames.ReturnValue.Buffer} != null && {DeviceAutoResult} != IntPtr.Zero)");
        using (writer.Scope())
        {
            writer.Append($"int size = {GSharpInternalNames.ReturnValue.Buffer}.Length * sizeof(");
            writer.AppendFullyQualifiedCsharpName(method.ReturnType);
            writer.AppendLine(");");

            writer.AppendLine("if (size > 0)");
            using (writer.Scope())
            {
                writer.AppendLine(
                    "var handle = System.Runtime.InteropServices.GCHandle.Alloc(" +
                    $"{GSharpInternalNames.ReturnValue.Buffer}, System.Runtime.InteropServices.GCHandleType.Pinned);");
                writer.AppendLine("try");
                using (writer.Scope())
                {
                    writer.AppendLine(
                        "CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2(" +
                        $"handle.AddrOfPinnedObject(), {DeviceAutoResult}, (IntPtr)size, hStream));");
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
            WriteParameterFree(writer, KernelParameterShape.Classify(p), isDevicePtr);
        }

        if (method.ReturnType.SpecialType == SpecialType.System_Void)
        {
            return;
        }

        writer.AppendLine($"if ({DeviceAutoResult} != IntPtr.Zero)");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"CudaException.ThrowIfError(CudaDriverApi.cuMemFreeAsync({DeviceAutoResult}, hStream));");
        }
    }

    // [WHY] The IntPtr wrapper receives device memory that the caller owns, so it forwards it without copying
    private static bool IsPassedThrough(in KernelParameterShape shape, bool isDevicePtr) =>
        isDevicePtr && (shape.IsArray || shape.IsCustomStruct || shape.IsByRef);

    private static void WriteParameterMarshal(SourceWriter writer, in KernelParameterShape shape, int paramIndex,
        bool isDevicePtr)
    {
        var p = shape.Parameter;
        if (IsPassedThrough(shape, isDevicePtr))
        {
            var kind = shape.Kind switch
            {
                KernelParameterKind.Array => "array",
                KernelParameterKind.CustomStruct => "struct",
                _ => "scalar"
            };
            writer.AppendLine($"// Marshal device {kind}: {shape.Identifier}");
            if (shape.IsArray)
            {
                writer.AppendLine($"IntPtr* proxy_{p.Name} = stackalloc IntPtr[2];");
                writer.AppendLine($"proxy_{p.Name}[0] = {shape.Identifier};");
                writer.AppendLine($"((int*)(proxy_{p.Name} + 1))[0] = 0; // Length is unknown for raw IntPtr");
                writer.AppendLine($"kernelParams[{paramIndex}] = proxy_{p.Name};");
            }
            else
            {
                writer.AppendLine($"kernelParams[{paramIndex}] = &{shape.Identifier};");
            }

            return;
        }

        switch (shape.Kind)
        {
            case KernelParameterKind.CustomStruct:
                WriteCustomStructMarshal(writer, shape, paramIndex, isDevicePtr);
                break;
            case KernelParameterKind.Array:
                WriteArrayMarshal(writer, shape, paramIndex);
                break;
            case KernelParameterKind.RefScalar:
                WriteRefOrOutScalarMarshal(writer, p, paramIndex);
                break;
            default:
                WriteScalarMarshal(writer, p, paramIndex);
                break;
        }
    }

    private static void WriteCustomStructMarshal(SourceWriter writer, in KernelParameterShape shape, int paramIndex,
        bool isDevicePtr)
    {
        var p = shape.Parameter;
        var marshaledType = shape.MarshaledType;
        writer.AppendAotStructPath(marshaledType);
        writer.AppendLine($" native_{p.Name} = default;");
        writer.AppendQualifiedMarshalMethodName(marshaledType);
        using (writer.Parentheses())
        {
            writer.Append(
                $"ref {p.Name.ToCSharpIdentifier()}, ref native_{p.Name}, {(isDevicePtr ? "true" : "false")}, {(isDevicePtr ? "null" : $"{p.Name}_allocatedPtrs")}, hStream");
        }

        writer.AppendLine(";");

        if (p.RefKind is RefKind.Ref or RefKind.Out)
        {
            writer.AppendLine($"// Marshal ref/out struct: {p.Name.ToCSharpIdentifier()}");
            writer.Append($"int {p.Name}_size = sizeof(");
            writer.AppendAotStructPath(marshaledType);
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
            writer.AppendLine($"// Marshal scalar struct: {p.Name.ToCSharpIdentifier()}");
            writer.AppendLine($"kernelParams[{paramIndex}] = &native_{p.Name};");
        }
    }

    private static void WriteArrayMarshal(SourceWriter writer, in KernelParameterShape shape, int paramIndex)
    {
        var p = shape.Parameter;
        var elementType = GetElementType(shape);
        GetArrayDirection(p, out var hasIn, out var hasOut);

        var isOutOnly = hasOut && !hasIn;
        var lenExpr = shape.LengthExpression;

        writer.AppendLine($"// Marshal array: {p.Name.ToCSharpIdentifier()}");
        writer.Append($"int {p.Name}_size = {lenExpr} * sizeof(");
        writer.AppendFullyQualifiedCsharpName(elementType);
        writer.AppendLine(");");

        writer.AppendLine($"IntPtr* proxy_{p.Name} = stackalloc IntPtr[2];");
        writer.AppendLine($"proxy_{p.Name}[0] = IntPtr.Zero;");
        writer.AppendLine($"((int*)(proxy_{p.Name} + 1))[0] = {lenExpr};");

        writer.AppendLine($"if ({p.Name}_size > 0)");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out d_{p.Name}, (IntPtr){p.Name}_size, hStream));");
            writer.AppendLine($"proxy_{p.Name}[0] = d_{p.Name};");

            if (isOutOnly)
            {
                writer.AppendLine($"// Skip HtoD copy for [Out] array: {p.Name.ToCSharpIdentifier()}");
            }
            else
            {
                writer.Append("fixed (");
                writer.AppendFullyQualifiedCsharpName(elementType);
                writer.AppendLine($"* p_{p.Name} = {shape.SpanExpression})");
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
        writer.AppendLine($"// Marshal ref/out scalar: {p.Name.ToCSharpIdentifier()}");
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
            writer.AppendLine($"* p_{p.Name}_temp = &{p.Name.ToCSharpIdentifier()})");
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
        writer.AppendLine($"// Marshal scalar: {p.Name.ToCSharpIdentifier()}");
        writer.AppendLine($"kernelParams[{paramIndex}] = &{p.Name.ToCSharpIdentifier()};");
    }

    private static void WriteLaunchKernelCall(SourceWriter writer, IMethodSymbol method)
    {
        var isCooperative = method.IsCooperativeKernel();

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

    private static void WriteParameterUnmarshal(SourceWriter writer, in KernelParameterShape shape, bool isDevicePtr)
    {
        if (IsPassedThrough(shape, isDevicePtr))
        {
            return;
        }

        switch (shape.Kind)
        {
            case KernelParameterKind.CustomStruct:
                WriteCustomStructUnmarshal(writer, shape, isDevicePtr);
                break;
            case KernelParameterKind.Array:
                WriteArrayUnmarshal(writer, shape, isDevicePtr);
                break;
            case KernelParameterKind.RefScalar:
                WriteRefOrOutScalarUnmarshal(writer, shape.Parameter, isDevicePtr);
                break;
        }
    }

    private static void WriteCustomStructUnmarshal(SourceWriter writer, in KernelParameterShape shape, bool isDevicePtr)
    {
        var p = shape.Parameter;
        if (isDevicePtr && shape.IsByRef)
        {
            return;
        }

        if (shape.IsByRef)
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

        writer.AppendQualifiedUnmarshalMethodName(shape.MarshaledType);
        using (writer.Parentheses())
        {
            writer.Append($"ref {p.Name.ToCSharpIdentifier()}, ref native_{p.Name}, {(isDevicePtr ? "true" : "false")}, hStream");
        }

        writer.AppendLine(";");
    }

    private static void WriteArrayUnmarshal(SourceWriter writer, in KernelParameterShape shape, bool isDevicePtr)
    {
        if (isDevicePtr)
        {
            return;
        }

        var p = shape.Parameter;
        GetArrayDirection(p, out var hasIn, out var hasOut);

        var isInOnly = hasIn && !hasOut;
        if (isInOnly || shape.IsReadOnlyContainer)
        {
            writer.AppendLine($"// Skip DtoH copy for [In] array: {shape.Identifier}");
            return;
        }

        writer.AppendLine($"// Copy back array: {shape.Identifier}");
        writer.AppendLine($"if ({p.Name}_size > 0 && d_{p.Name} != IntPtr.Zero)");
        using (writer.Scope())
        {
            writer.Append("fixed (");
            writer.AppendFullyQualifiedCsharpName(GetElementType(shape));
            writer.AppendLine($"* p_{p.Name} = {shape.SpanExpression})");
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
            writer.AppendLine($"{p.Name.ToCSharpIdentifier()} = default;");
        }

        writer.AppendLine($"// Fast zero-allocation copy from device for scalar: {p.Name.ToCSharpIdentifier()}");
        writer.Append("fixed (");
        writer.AppendFullyQualifiedCsharpName(p.Type);
        writer.AppendLine($"* ptr_{p.Name} = &{p.Name.ToCSharpIdentifier()})");
        using (writer.Scope())
        {
            writer.AppendLine(
                "CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2(" +
                $"(IntPtr)ptr_{p.Name}, d_{p.Name}, (IntPtr){p.Name}_size, hStream));");
            writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
        }
    }

    private static void WriteParameterFree(SourceWriter writer, in KernelParameterShape shape, bool isDevicePtr)
    {
        if (isDevicePtr)
        {
            return;
        }

        var p = shape.Parameter;
        if (shape.IsCustomStruct)
        {
            writer.AppendLine(
                $"foreach (var ptr in {p.Name}_allocatedPtrs) CudaDriverApi.cuMemFreeAsync(ptr, hStream);");
        }

        if (shape.IsArray || shape.IsByRef)
        {
            writer.AppendLine($"if (d_{p.Name} != IntPtr.Zero) CudaDriverApi.cuMemFreeAsync(d_{p.Name}, hStream);");
        }
    }

    private static ITypeSymbol GetElementType(in KernelParameterShape shape) =>
        shape.ElementType ??
        throw new InvalidOperationException($"Parameter '{shape.Identifier}' is not an array-like type.");

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
