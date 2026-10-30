namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits P/Invoke wrapper logic for launching CUDA kernels, including memory allocation, marshaling, and stream synchronization.</summary>
internal static class KernelLaunchEmitter
{
    /// <summary>The device copy of the auto result buffer in the generated launcher.</summary>
    private const string DeviceAutoResult = "d_" + GSharpInternalNames.ReturnValue.Buffer;

    // The device scratch of the grid strategy of AsParallel() aggregates (KernelHostModel.GridScratchSlotBytes)
    private const string DeviceGridScratch = "d_" + GSharpInternalNames.StackLinq.GridScratch;

    /// <summary>Emits local variable declarations for device pointers and tracking lists.</summary>
    internal static void WriteVariablesInitialization(SourceWriter writer, in KernelHostModel kernel, bool isDevicePtr)
    {
        // One table for the whole launch, so that every array argument, struct argument and converted element finds the
        // arrays the others already copied (#707, #721)
        writer.AppendLineIf(
            UsesDeviceArrayRegions(kernel, isDevicePtr),
            $"var {DeviceArrays} = new {StructMarshalingEmitter.DeviceArrayRegionsType}();"
        );
        foreach (var shape in kernel.Parameters)
        {
            if (isDevicePtr)
            {
                continue;
            }

            if (shape.IsCudaConvertedArray)
            {
                writer.AppendLine($"byte[]? {shape.Name}_shadow = null;");
            }

            if (shape.IsArray || shape.IsByRef)
            {
                writer.AppendLine($"IntPtr d_{shape.Name} = IntPtr.Zero;");
            }
        }

        if (kernel.ReturnsValue)
        {
            writer.AppendLine($"IntPtr {DeviceAutoResult} = IntPtr.Zero;");
        }

        writer.AppendLineIf(kernel.GridScratchSlotBytes > 0, $"IntPtr {DeviceGridScratch} = IntPtr.Zero;");
    }

    /// <summary>Emits argument marshaling, device memory transfers, kernel launch, and result copyback.</summary>
    internal static void WriteMarshalAndLaunchKernel(SourceWriter writer, in KernelHostModel kernel, bool isDevicePtr)
    {
        var kernelParamCount = kernel.Parameters.Length;

        if (kernel.ReturnsValue)
        {
            kernelParamCount++;
        }

        if (kernel.GridScratchSlotBytes > 0)
        {
            kernelParamCount++;
        }

        writer.Append("void** kernelParams = stackalloc void*[");
        writer.Append(kernelParamCount.ToString());
        writer.AppendLine("];");

        // [WHY] A holder that copies its array in comes first, so that an [Out] array argument that shares the region
        // finds it already filled (#721): the region is copied in when any of its holders needs that
        var usesRegions = UsesDeviceArrayRegions(kernel, isDevicePtr);
        for (var i = 0; i < kernel.Parameters.Length; i++)
        {
            if (!IsMarshaledLast(kernel.Parameters[i], usesRegions))
            {
                WriteParameterMarshal(writer, kernel.Parameters[i], i, isDevicePtr, usesRegions);
            }
        }

        for (var i = 0; i < kernel.Parameters.Length; i++)
        {
            if (IsMarshaledLast(kernel.Parameters[i], usesRegions))
            {
                WriteParameterMarshal(writer, kernel.Parameters[i], i, isDevicePtr, usesRegions);
            }
        }

        var paramIndex = kernel.Parameters.Length;

        if (kernel.ReturnTypeName is { } returnType)
        {
            writer.AppendLine("// Marshal auto result array");
            writer.AppendLine(
                $"int {GSharpInternalNames.ReturnValue.BufferSize} = {GSharpInternalNames.ReturnValue.Buffer}.Length * sizeof({returnType});");

            writer.AppendLine($"if ({GSharpInternalNames.ReturnValue.BufferSize} > 0)");
            using (writer.Scope())
            {
                writer.AppendLine(
                    "CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(" +
                    $"out {DeviceAutoResult}, (IntPtr){GSharpInternalNames.ReturnValue.BufferSize}, hStream));");
                writer.AppendLine("kernelParams[" + paramIndex + $"] = &{DeviceAutoResult};");
            }
        }

        if (kernel.GridScratchSlotBytes > 0)
        {
            // One slot per block for its partial result and one for the total, freed after the launch (WriteCleanup)
            writer.AppendLine("// Allocate the scratch of the grid strategy of AsParallel() aggregates");
            writer.AppendLine(
                "CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(" +
                $"out {DeviceGridScratch}, (IntPtr)(((long)gridDimX * gridDimY * gridDimZ + 1) * {kernel.GridScratchSlotBytes}), hStream));");
            writer.AppendLine($"kernelParams[{kernelParamCount - 1}] = &{DeviceGridScratch};");
        }

        writer.AppendLine();

        WriteLaunchKernelCall(writer, kernel.IsCooperative);

        foreach (var shape in kernel.Parameters)
        {
            WriteParameterUnmarshal(writer, shape, isDevicePtr, usesRegions);
        }

        if (kernel.ReturnTypeName is not { } resultType)
        {
            return;
        }

        writer.AppendLine($"if ({GSharpInternalNames.ReturnValue.Buffer} != null && {DeviceAutoResult} != IntPtr.Zero)");
        using (writer.Scope())
        {
            writer.AppendLine($"int size = {GSharpInternalNames.ReturnValue.Buffer}.Length * sizeof({resultType});");

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
    internal static void WriteCleanup(SourceWriter writer, in KernelHostModel kernel, bool isDevicePtr)
    {
        var usesRegions = UsesDeviceArrayRegions(kernel, isDevicePtr);
        foreach (var shape in kernel.Parameters)
        {
            WriteParameterFree(writer, shape, isDevicePtr, usesRegions);
        }

        writer.AppendLineIf(usesRegions, $"{DeviceArrays}.Free(hStream);");

        if (kernel.GridScratchSlotBytes > 0)
        {
            writer.AppendLine($"if ({DeviceGridScratch} != IntPtr.Zero)");
            using (writer.Scope())
            {
                writer.AppendLine($"CudaException.ThrowIfError(CudaDriverApi.cuMemFreeAsync({DeviceGridScratch}, hStream));");
            }
        }

        if (!kernel.ReturnsValue)
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

    // The device regions of the arrays that array arguments, struct arguments and converted elements hold
    // (DeviceArrayRegions)
    private const string DeviceArrays = GSharpInternalNames.Launcher.DeviceArrays;

    // Whether two holders of one array are possible: a struct argument or a converted array that holds arrays (whose
    // Marshal_ calls record the arrays they copy), or two T[] arguments. A launch with a single T[] argument allocates no
    // table; the IntPtr wrapper copies nothing
    private static bool UsesDeviceArrayRegions(in KernelHostModel kernel, bool isDevicePtr) =>
        !isDevicePtr &&
        (kernel.Parameters
             .AsStackEnumerable()
             .Any(static shape => shape.MarshalsCudaArrays) ||
         kernel.Parameters
             .AsStackEnumerable()
             .Count(static shape => shape.IsSharableArray) > 1);

    // The table that a parameter's Marshal_ calls record their arrays in, null when they copy none
    private static string DeviceArraysFor(in KernelParameterShape shape) =>
        shape.MarshalsCudaArrays ? DeviceArrays : "null";

    // [WHY] An array argument is a reference: two arguments, or an argument and a struct field, that hold one array are
    // one object in C#, so the managed-array launcher gives them one device region, found by reference identity in the
    // launch's DeviceArrayRegions (#721), whenever the launch has one (UsesDeviceArrayRegions).
    private static bool IsSharedArrayArgument(in KernelParameterShape shape, bool usesRegions) =>
        usesRegions && shape.IsSharableArray;

    // Whether the launcher copies the array argument to the device: [Out] does not, except for converted elements that
    // hold arrays, whose device pointers Marshal_ writes into the shadow
    private static bool CopiesIn(in KernelParameterShape shape) =>
        shape.CopyIn || (shape.IsCudaConvertedArray && shape.Cuda.ConvertedElementHasArrayFields);

    // An [Out] array argument is marshaled after every other parameter (WriteMarshalAndLaunchKernel)
    private static bool IsMarshaledLast(in KernelParameterShape shape, bool usesRegions) =>
        IsSharedArrayArgument(shape, usesRegions) && !CopiesIn(shape);

    // "<name> is not null && " for an optional array: the size test already excludes null, but the nullable analysis of
    // the generated code does not know it
    private static string NotNullPrefix(in KernelParameterShape shape) =>
        shape.IsNullableReference ? $"{shape.Identifier} is not null && " : "";

    // [WHY] The IntPtr wrapper receives device memory that the caller owns, so it forwards it without copying
    private static bool IsPassedThrough(in KernelParameterShape shape, bool isDevicePtr) =>
        isDevicePtr && (shape.IsArray || shape.IsCustomStruct || shape.IsByRef);

    private static void WriteParameterMarshal(SourceWriter writer, in KernelParameterShape shape, int paramIndex,
        bool isDevicePtr, bool usesRegions)
    {
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
                writer.AppendLine($"IntPtr* proxy_{shape.Name} = stackalloc IntPtr[2];");
                writer.AppendLine($"proxy_{shape.Name}[0] = {shape.Identifier};");
                writer.AppendLine($"((int*)(proxy_{shape.Name} + 1))[0] = 0; // Length is unknown for raw IntPtr");
                writer.AppendLine($"kernelParams[{paramIndex}] = proxy_{shape.Name};");
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
                WriteCustomStructMarshal(writer, shape, paramIndex);
                break;
            case KernelParameterKind.Array:
                WriteArrayMarshal(writer, shape, paramIndex, usesRegions);
                break;
            case KernelParameterKind.RefScalar:
                WriteRefOrOutScalarMarshal(writer, shape, paramIndex);
                break;
            default:
                WriteScalarMarshal(writer, shape, paramIndex);
                break;
        }
    }

    // Only the managed-array launcher gets here: the Ptr launcher passes a struct through (IsPassedThrough)
    private static void WriteCustomStructMarshal(SourceWriter writer, in KernelParameterShape shape, int paramIndex)
    {
        if (shape.IsDirectCudaValueStruct)
        {
            writer.AppendLine($"// Marshal scalar struct: {shape.Identifier}");
            writer.AppendLine($"kernelParams[{paramIndex}] = &{shape.Identifier};");
            return;
        }

        writer.AppendLine($"{shape.AotStructPath} native_{shape.Name} = default;");
        writer.Append(shape.MarshalMethodName);
        using (writer.Parentheses())
        {
            writer.Append(
                $"ref {shape.Identifier}, ref native_{shape.Name}, false, {DeviceArraysFor(shape)}, hStream");
        }

        writer.AppendLine(";");

        if (shape.RefKind is RefKind.Ref or RefKind.Out)
        {
            writer.AppendLine($"// Marshal ref/out struct: {shape.Identifier}");
            writer.AppendLine($"int {shape.Name}_size = sizeof({shape.AotStructPath});");

            writer.AppendLine(
                $"CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out d_{shape.Name}, (IntPtr){shape.Name}_size, hStream));");
            if (shape.RefKind == RefKind.Ref)
            {
                writer.AppendLine("// Fast zero-allocation copy to device");
                writer.AppendLine(
                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(d_{shape.Name}, " +
                    $"(IntPtr)(&native_{shape.Name}), (IntPtr){shape.Name}_size, hStream));");
                writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
            }

            writer.AppendLine($"kernelParams[{paramIndex}] = &d_{shape.Name};");
        }
        else
        {
            writer.AppendLine($"// Marshal scalar struct: {shape.Identifier}");
            writer.AppendLine($"kernelParams[{paramIndex}] = &native_{shape.Name};");
        }
    }

    private static void WriteArrayMarshal(SourceWriter writer, in KernelParameterShape shape, int paramIndex,
        bool usesRegions)
    {
        if (shape.IsCudaConvertedArray)
        {
            WriteConvertedArrayMarshal(writer, shape, paramIndex, usesRegions);
            return;
        }

        var elementType = GetElementTypeName(shape);
        var isOutOnly = !shape.CopyIn;
        var lenExpr = shape.LengthExpression;

        writer.AppendLine($"// Marshal array: {shape.Identifier}");
        writer.AppendLine($"int {shape.Name}_size = {lenExpr} * sizeof({elementType});");

        writer.AppendLine($"IntPtr* proxy_{shape.Name} = stackalloc IntPtr[2];");
        writer.AppendLine($"proxy_{shape.Name}[0] = IntPtr.Zero;");
        writer.AppendLine($"((int*)(proxy_{shape.Name} + 1))[0] = {lenExpr};");

        var shared = IsSharedArrayArgument(shape, usesRegions);
        writer.AppendLine($"if ({(shared ? NotNullPrefix(shape) : "")}{shape.Name}_size > 0)");
        using (writer.Scope())
        {
            // if (!deviceArrays.TryGetRegion(<name>, out d_<name>)) { <allocate>; deviceArrays.Add(...); <copy in> }
            // An array that another holder already copied gets its region and is not copied again
            if (shared)
            {
                writer.AppendLine($"if (!{DeviceArrays}.TryGetRegion({shape.Identifier}, out d_{shape.Name}))");
                using (writer.Scope())
                {
                    WriteArrayAllocationAndCopyIn(writer, shape, elementType, isOutOnly, shared);
                }
            }
            else
            {
                WriteArrayAllocationAndCopyIn(writer, shape, elementType, isOutOnly, shared);
            }

            writer.AppendLine($"proxy_{shape.Name}[0] = d_{shape.Name};");
        }

        writer.AppendLine($"kernelParams[{paramIndex}] = proxy_{shape.Name};");
    }

    private static void WriteArrayAllocationAndCopyIn(SourceWriter writer, in KernelParameterShape shape,
        string elementType, bool isOutOnly, bool shared)
    {
        writer.AppendLine(
            $"CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out d_{shape.Name}, (IntPtr){shape.Name}_size, hStream));");
        writer.AppendLineIf(shared, $"{DeviceArrays}.Add({shape.Identifier}, d_{shape.Name});");

        if (isOutOnly)
        {
            writer.AppendLine($"// Skip HtoD copy for [Out] array: {shape.Identifier}");
            return;
        }

        writer.AppendLine($"fixed ({elementType}* p_{shape.Name} = {shape.SpanExpression})");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(" +
                $"d_{shape.Name}, (IntPtr)p_{shape.Name}, (IntPtr){shape.Name}_size, hStream));");
            writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
        }
    }

    // [WHY] The host and device layouts of the element differ (GSH0080), so the elements are converted into a shadow
    // array of the device layout, rented from ArrayPool, and the shadow is copied in one transfer.
    private static void WriteConvertedArrayMarshal(SourceWriter writer, in KernelParameterShape shape, int paramIndex,
        bool usesRegions)
    {
        var name = shape.Name;
        var lenExpr = shape.LengthExpression;

        writer.AppendLine($"// Marshal array element by element: {shape.Identifier}");
        writer.AppendLine($"int {name}_size = {lenExpr} * sizeof({ConvertedArrayEmitter.GetElementShadowPath(shape, shape.Cuda)});");

        writer.AppendLine($"IntPtr* proxy_{name} = stackalloc IntPtr[2];");
        writer.AppendLine($"proxy_{name}[0] = IntPtr.Zero;");
        writer.AppendLine($"((int*)(proxy_{name} + 1))[0] = {lenExpr};");

        var shared = IsSharedArrayArgument(shape, usesRegions);
        writer.AppendLine($"if ({(shared ? NotNullPrefix(shape) : "")}{name}_size > 0)");
        using (writer.Scope())
        {
            // The same lookup as WriteArrayMarshal: a holder that finds the region neither converts nor copies, and
            // rents a shadow only to copy the region back into (WriteConvertedArrayUnmarshal)
            if (shared)
            {
                writer.AppendLine($"if (!{DeviceArrays}.TryGetRegion({shape.Identifier}, out d_{name}))");
                using (writer.Scope())
                {
                    WriteConvertedArrayAllocationAndCopyIn(writer, shape, shared);
                }

                if (CopiesBack(shape))
                {
                    writer.AppendLine("else");
                    using (writer.Scope())
                    {
                        WriteShadowRent(writer, name);
                    }
                }
            }
            else
            {
                WriteConvertedArrayAllocationAndCopyIn(writer, shape, shared);
            }

            writer.AppendLine($"proxy_{name}[0] = d_{name};");
        }

        writer.AppendLine($"kernelParams[{paramIndex}] = proxy_{name};");
    }

    private static void WriteConvertedArrayAllocationAndCopyIn(SourceWriter writer, in KernelParameterShape shape,
        bool shared)
    {
        var name = shape.Name;
        WriteShadowRent(writer, name);
        writer.AppendLine(
            "CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(" +
            $"out d_{name}, (IntPtr){name}_size, hStream));");
        // Recorded before the elements are marshaled, so that an element that holds the same array finds it
        writer.AppendLineIf(shared, $"{DeviceArrays}.Add({shape.Identifier}, d_{name});");

        // [WHY] An [Out] array still needs its elements' array fields on the device: the kernel writes into
        // them, and Unmarshal_ copies them back through the device pointers that Marshal_ stores
        if (!CopiesIn(shape))
        {
            writer.AppendLine($"// Skip HtoD copy for [Out] array: {shape.Identifier}");
            return;
        }

        ConvertedArrayEmitter.WriteShadowAndHostSpans(writer, shape, shape.Cuda);
        ConvertedArrayEmitter.WriteElementMarshalLoop(writer, shape, shape.Cuda, DeviceArraysFor(shape));
        writer.AppendLine($"fixed (byte* p_{name} = {name}_shadow)");
        using (writer.Scope())
        {
            writer.AppendLine(
                "CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(" +
                $"d_{name}, (IntPtr)p_{name}, (IntPtr){name}_size, hStream));");
            writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
        }
    }

    private static void WriteShadowRent(SourceWriter writer, string name) =>
        writer.AppendLine($"{name}_shadow = System.Buffers.ArrayPool<byte>.Shared.Rent({name}_size);");

    // Whether the launcher copies the array argument back to the host: not [In], not a read-only container
    private static bool CopiesBack(in KernelParameterShape shape) => shape.CopyOut && !shape.IsReadOnlyContainer;

    private static void WriteRefOrOutScalarMarshal(SourceWriter writer, in KernelParameterShape shape, int paramIndex)
    {
        writer.AppendLine($"// Marshal ref/out scalar: {shape.Identifier}");
        writer.AppendLine($"int {shape.Name}_size = sizeof({shape.TypeName});");
        writer.AppendLine(
            $"CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out d_{shape.Name}, (IntPtr){shape.Name}_size, hStream));");

        if (shape.RefKind == RefKind.Ref)
        {
            writer.AppendLine("// Fast zero-allocation copy to device");
            writer.AppendLine($"fixed ({shape.TypeName}* p_{shape.Name}_temp = &{shape.Identifier})");
            using (writer.Scope())
            {
                writer.AppendLine(
                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(d_{shape.Name}," +
                    $" (IntPtr)p_{shape.Name}_temp, (IntPtr){shape.Name}_size, hStream));");
                writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
            }
        }

        writer.AppendLine($"kernelParams[{paramIndex}] = &d_{shape.Name};");
    }

    private static void WriteScalarMarshal(SourceWriter writer, in KernelParameterShape shape, int paramIndex)
    {
        writer.AppendLine($"// Marshal scalar: {shape.Identifier}");
        writer.AppendLine($"kernelParams[{paramIndex}] = &{shape.Identifier};");
    }

    private static void WriteLaunchKernelCall(SourceWriter writer, bool isCooperative)
    {
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

    private static void WriteParameterUnmarshal(SourceWriter writer, in KernelParameterShape shape, bool isDevicePtr,
        bool usesRegions)
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
                WriteArrayUnmarshal(writer, shape, isDevicePtr, usesRegions);
                break;
            case KernelParameterKind.RefScalar:
                WriteRefOrOutScalarUnmarshal(writer, shape, isDevicePtr);
                break;
        }
    }

    private static void WriteCustomStructUnmarshal(SourceWriter writer, in KernelParameterShape shape, bool isDevicePtr)
    {
        if (shape.IsDirectCudaValueStruct)
        {
            return;
        }

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
                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2((IntPtr)(&native_{shape.Name})," +
                    $" d_{shape.Name}, (IntPtr){shape.Name}_size, hStream));");
                writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
            }
        }

        writer.Append(shape.UnmarshalMethodName);
        using (writer.Parentheses())
        {
            writer.Append($"ref {shape.Identifier}, ref native_{shape.Name}, {(isDevicePtr ? "true" : "false")}, hStream");
        }

        writer.AppendLine(";");
    }

    private static void WriteArrayUnmarshal(SourceWriter writer, in KernelParameterShape shape, bool isDevicePtr,
        bool usesRegions)
    {
        if (isDevicePtr)
        {
            return;
        }

        if (!CopiesBack(shape))
        {
            writer.AppendLine($"// Skip DtoH copy for [In] array: {shape.Identifier}");
            return;
        }

        if (shape.IsCudaConvertedArray)
        {
            WriteConvertedArrayUnmarshal(writer, shape, usesRegions);
            return;
        }

        writer.AppendLine($"// Copy back array: {shape.Identifier}");
        writer.AppendLine($"if ({shape.Name}_size > 0 && d_{shape.Name} != IntPtr.Zero{CopyBackClaim(shape, usesRegions)})");
        using (writer.Scope())
        {
            writer.AppendLine($"fixed ({GetElementTypeName(shape)}* p_{shape.Name} = {shape.SpanExpression})");
            using (writer.Scope())
            {
                writer.AppendLine(
                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2((IntPtr)p_{shape.Name}, d_{shape.Name}, (IntPtr){shape.Name}_size, hStream));");
                writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
            }
        }
    }

    private static void WriteConvertedArrayUnmarshal(SourceWriter writer, in KernelParameterShape shape,
        bool usesRegions)
    {
        var name = shape.Name;
        writer.AppendLine($"// Copy back array element by element: {shape.Identifier}");
        writer.AppendLine($"if ({name}_size > 0 && d_{name} != IntPtr.Zero && {name}_shadow != null{CopyBackClaim(shape, usesRegions)})");
        using (writer.Scope())
        {
            writer.AppendLine($"fixed (byte* p_{name} = {name}_shadow)");
            using (writer.Scope())
            {
                writer.AppendLine(
                    "CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2(" +
                    $"(IntPtr)p_{name}, d_{name}, (IntPtr){name}_size, hStream));");
                writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
            }

            ConvertedArrayEmitter.WriteShadowAndHostSpans(writer, shape, shape.Cuda);
            ConvertedArrayEmitter.WriteElementUnmarshalLoop(writer, shape, shape.Cuda);
        }
    }

    // " && [<name> is not null && ]deviceArrays.TryClaimCopyBack(<name>)" for an array argument that may share its region
    // [WHY] The region holds the writes of every holder, so the first holder that copies back copies them all, and the
    // others would write the same bytes again (#721). A holder that never copies back ([In], a read-only container)
    // claims nothing, so a writable holder of the same array still copies it back.
    private static string CopyBackClaim(in KernelParameterShape shape, bool usesRegions) =>
        IsSharedArrayArgument(shape, usesRegions)
            ? $" && {NotNullPrefix(shape)}{DeviceArrays}.TryClaimCopyBack({shape.Identifier})"
            : "";

    private static void WriteRefOrOutScalarUnmarshal(SourceWriter writer, in KernelParameterShape shape, bool isDevicePtr)
    {
        if (isDevicePtr)
        {
            return;
        }

        if (shape.RefKind == RefKind.Out)
        {
            writer.AppendLine($"{shape.Identifier} = default;");
        }

        writer.AppendLine($"// Fast zero-allocation copy from device for scalar: {shape.Identifier}");
        writer.AppendLine($"fixed ({shape.TypeName}* ptr_{shape.Name} = &{shape.Identifier})");
        using (writer.Scope())
        {
            writer.AppendLine(
                "CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2(" +
                $"(IntPtr)ptr_{shape.Name}, d_{shape.Name}, (IntPtr){shape.Name}_size, hStream));");
            writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
        }
    }

    private static void WriteParameterFree(SourceWriter writer, in KernelParameterShape shape, bool isDevicePtr,
        bool usesRegions)
    {
        if (isDevicePtr)
        {
            return;
        }

        if (shape.IsCudaConvertedArray)
        {
            writer.AppendLine(
                $"if ({shape.Name}_shadow != null) System.Buffers.ArrayPool<byte>.Shared.Return({shape.Name}_shadow);");
        }

        // The launch's DeviceArrayRegions frees the region of an array argument that may share it (WriteCleanup)
        if ((shape.IsArray && !IsSharedArrayArgument(shape, usesRegions)) || shape.IsByRef)
        {
            writer.AppendLine($"if (d_{shape.Name} != IntPtr.Zero) CudaDriverApi.cuMemFreeAsync(d_{shape.Name}, hStream);");
        }
    }

    private static string GetElementTypeName(in KernelParameterShape shape) =>
        shape.ElementTypeName ??
        throw new InvalidOperationException($"Parameter '{shape.Identifier}' is not an array-like type.");
}
