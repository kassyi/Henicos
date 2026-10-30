namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits P/Invoke wrapper logic for launching CUDA kernels, including memory allocation, marshaling, and stream synchronization.</summary>
internal static class KernelLaunchEmitter
{
    /// <summary>The device copy of the auto result buffer in the generated launcher.</summary>
    private const string DeviceAutoResult = "d_" + GSharpInternalNames.ReturnValue.Buffer;

    // The device scratch of the grid strategy of AsParallel() aggregates (KernelHostModel.GridScratchSlotBytes)
    private const string DeviceGridScratch = "d_" + GSharpInternalNames.StackLinq.GridScratch;

    // The device regions of the arrays that array arguments, struct arguments and converted elements hold
    // (DeviceArrayRegions, ArraySharingMode.Table)
    private const string DeviceArrays = GSharpInternalNames.Launcher.DeviceArrays;

    private const string ReferenceEqualsMethod = ArraySharingPlan.ReferenceEqualsMethod;

    /// <summary>Emits local variable declarations for device pointers and tracking lists.</summary>
    internal static void WriteVariablesInitialization(SourceWriter writer, in KernelHostModel kernel,
        in ArraySharingPlan plan, bool isDevicePtr)
    {
        // One table for the whole launch, so that every array argument, struct argument and converted element finds the
        // arrays the others already copied (#707, #721)
        writer.AppendLineIf(plan.UsesTable, $"var {DeviceArrays} = new {StructMarshalingEmitter.DeviceArrayRegionsType}();");
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
    internal static void WriteMarshalAndLaunchKernel(SourceWriter writer, in KernelHostModel kernel,
        in ArraySharingPlan plan, bool isDevicePtr)
    {
        var kernelParamCount = WriteKernelParamsDeclaration(writer, kernel);
        WriteParameterMarshals(writer, plan, isDevicePtr);
        WriteAutoResultMarshal(writer, kernel);
        WriteGridScratchAllocation(writer, kernel, kernelParamCount);
        writer.AppendLine();

        WriteLaunchKernelCall(writer, kernel.IsCooperative);

        for (var i = 0; i < kernel.Parameters.Length; i++)
        {
            WriteParameterUnmarshal(writer, plan, i, isDevicePtr);
        }

        WriteAutoResultCopyBack(writer, kernel);
    }

    // void** kernelParams = stackalloc void*[<parameters>[ + auto result][ + grid scratch]];
    // Returns the number of slots.
    private static int WriteKernelParamsDeclaration(SourceWriter writer, in KernelHostModel kernel)
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
        return kernelParamCount;
    }

    // [WHY] A holder that copies its array in comes first, so that an [Out] array argument that shares the region finds
    // it already filled (#721): the region is copied in when any of its holders needs that. Kernel parameter slots keep
    // the parameter index.
    private static void WriteParameterMarshals(SourceWriter writer, in ArraySharingPlan plan, bool isDevicePtr)
    {
        for (var i = 0; i < plan.Parameters.Length; i++)
        {
            if (!plan.IsMarshaledLast(i))
            {
                WriteParameterMarshal(writer, plan, i, isDevicePtr);
            }
        }

        for (var i = 0; i < plan.Parameters.Length; i++)
        {
            if (plan.IsMarshaledLast(i))
            {
                WriteParameterMarshal(writer, plan, i, isDevicePtr);
            }
        }
    }

    // int __gsharp_auto_result_size = <buffer>.Length * sizeof(<result>);
    // if (<size> > 0) { cuMemAllocAsync(out d___gsharp_auto_result, <size>); kernelParams[<parameters>] = &d_...; }
    private static void WriteAutoResultMarshal(SourceWriter writer, in KernelHostModel kernel)
    {
        if (kernel.ReturnTypeName is not { } returnType)
        {
            return;
        }

        // [WHY] The device writes the returned elements in its own layout, which a converted struct does not share;
        // the size is checked, as in the CPU launcher, since a large grid of a large struct would wrap
        var elementType = kernel.IsCudaConvertedReturn
            ? ConvertedArrayEmitter.GetReturnShadowPath(kernel.CudaReturn)
            : returnType;
        writer.AppendLine("// Marshal auto result array");
        writer.AppendLine(
            $"int {GSharpInternalNames.ReturnValue.BufferSize} = checked({GSharpInternalNames.ReturnValue.Buffer}.Length * sizeof({elementType}));");

        writer.AppendLine($"if ({GSharpInternalNames.ReturnValue.BufferSize} > 0)");
        using (writer.Scope())
        {
            writer.AppendLine(
                "CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(" +
                $"out {DeviceAutoResult}, (IntPtr){GSharpInternalNames.ReturnValue.BufferSize}, hStream));");
            writer.AppendLine("kernelParams[" + kernel.Parameters.Length + $"] = &{DeviceAutoResult};");
        }
    }

    // One slot per block for its partial result and one for the total, freed after the launch (WriteCleanup)
    private static void WriteGridScratchAllocation(SourceWriter writer, in KernelHostModel kernel, int kernelParamCount)
    {
        if (kernel.GridScratchSlotBytes <= 0)
        {
            return;
        }

        writer.AppendLine("// Allocate the scratch of the grid strategy of AsParallel() aggregates");
        writer.AppendLine(
            "CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(" +
            $"out {DeviceGridScratch}, (IntPtr)(((long)gridDimX * gridDimY * gridDimZ + 1) * {kernel.GridScratchSlotBytes}), hStream));");
        writer.AppendLine($"kernelParams[{kernelParamCount - 1}] = &{DeviceGridScratch};");
    }

    // if (<buffer> != null && d___gsharp_auto_result != IntPtr.Zero) { int size = ...; if (size > 0) { <pinned copy back> } }
    private static void WriteAutoResultCopyBack(SourceWriter writer, in KernelHostModel kernel)
    {
        if (kernel.ReturnTypeName is not { } resultType)
        {
            return;
        }

        if (kernel.IsCudaConvertedReturn)
        {
            WriteConvertedAutoResultCopyBack(writer, kernel.CudaReturn);
            return;
        }

        writer.AppendLine($"if ({GSharpInternalNames.ReturnValue.Buffer} != null && {DeviceAutoResult} != IntPtr.Zero)");
        using (writer.Scope())
        {
            writer.AppendLine($"int size = {GSharpInternalNames.ReturnValue.Buffer}.Length * sizeof({resultType});");

            writer.AppendLine("if (size > 0)");
            using (writer.Scope())
            {
                StructMarshalingEmitter.WritePinnedCopy(
                    writer,
                    GSharpInternalNames.ReturnValue.Buffer,
                    DeviceAutoResult,
                    toDevice: false,
                    synchronize: true
                );
            }
        }
    }

    // if (__gsharp_auto_result_size > 0 && d___gsharp_auto_result != IntPtr.Zero) {
    //   var __gsharp_auto_result_shadow = ArrayPool<byte>.Shared.Rent(__gsharp_auto_result_size);
    //   try { fixed (byte* p = shadow) { <copy to host>; <synchronize> } <shadow span>; <Unmarshal_ loop> }
    //   finally { ArrayPool<byte>.Shared.Return(__gsharp_auto_result_shadow); }
    // }
    // [WHY] The device wrote the returned structs in its layout: they are read into a shadow array of that layout and
    // converted element by element, as an [Out] array of such structs is (#704)
    private static void WriteConvertedAutoResultCopyBack(SourceWriter writer, in ParameterTransfer transfer)
    {
        const string Shadow = GSharpInternalNames.ReturnValue.BufferShadow;
        const string Size = GSharpInternalNames.ReturnValue.BufferSize;
        writer.AppendLine("// Copy back the auto result array element by element");
        writer.AppendLine($"if ({Size} > 0 && {DeviceAutoResult} != IntPtr.Zero)");
        using (writer.Scope())
        {
            writer.AppendLine($"var {Shadow} = System.Buffers.ArrayPool<byte>.Shared.Rent({Size});");
            writer.AppendLine("try");
            using (writer.Scope())
            {
                writer.AppendLine($"fixed (byte* p_{Shadow} = {Shadow})");
                using (writer.Scope())
                {
                    writer.AppendLine(
                        "CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2(" +
                        $"(IntPtr)p_{Shadow}, {DeviceAutoResult}, (IntPtr){Size}, hStream));");
                    writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));");
                }

                ConvertedArrayEmitter.WriteReturnShadowSpan(writer, transfer);
                ConvertedArrayEmitter.WriteReturnUnmarshalLoop(writer, transfer);
            }

            writer.AppendLine("finally");
            using (writer.Scope())
            {
                writer.AppendLine($"System.Buffers.ArrayPool<byte>.Shared.Return({Shadow});");
            }
        }
    }

    /// <summary>Emits cleanup code to asynchronously free allocated device memory buffers.</summary>
    internal static void WriteCleanup(SourceWriter writer, in KernelHostModel kernel, in ArraySharingPlan plan,
        bool isDevicePtr)
    {
        for (var i = 0; i < kernel.Parameters.Length; i++)
        {
            WriteParameterFree(writer, plan, i, isDevicePtr);
        }

        writer.AppendLineIf(plan.UsesTable, $"{DeviceArrays}.Free(hStream);");

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

    // The table that a parameter's Marshal_ calls record their arrays in, null when they copy none
    private static string DeviceArraysFor(in KernelParameterShape shape) =>
        shape.MarshalsCudaArrays ? DeviceArrays : "null";

    // "<name> is not null && " for an optional array whose table lookup follows: the size test already excludes null,
    // but the nullable analysis of the generated code does not know it
    private static string NotNullPrefix(in KernelParameterShape shape) =>
        shape.IsNullableReference ? $"{shape.Identifier} is not null && " : "";

    // [WHY] The IntPtr wrapper receives device memory that the caller owns, so it forwards it without copying
    private static bool IsPassedThrough(in KernelParameterShape shape, bool isDevicePtr) =>
        isDevicePtr && (shape.IsArray || shape.IsCustomStruct || shape.IsByRef);

    private static void WriteParameterMarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex,
        bool isDevicePtr)
    {
        var shape = plan.Parameters[paramIndex];
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
                // The caller's pointer may be IntPtr.Zero for any array: a non-optional one then reads as null (#706)
                WriteNullReadsAsEmptyFlag(writer, shape, $"{shape.Identifier} == IntPtr.Zero");
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
                WriteArrayMarshal(writer, plan, paramIndex);
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

    private static void WriteArrayMarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        if (shape.IsCudaConvertedArray)
        {
            WriteConvertedArrayMarshal(writer, plan, paramIndex);
            return;
        }

        var lenExpr = shape.LengthExpression;

        writer.AppendLine($"// Marshal array: {shape.Identifier}");
        writer.AppendLine($"int {shape.Name}_size = {lenExpr} * sizeof({GetElementTypeName(shape)});");

        writer.AppendLine($"IntPtr* proxy_{shape.Name} = stackalloc IntPtr[2];");
        writer.AppendLine($"proxy_{shape.Name}[0] = IntPtr.Zero;");
        writer.AppendLine($"((int*)(proxy_{shape.Name} + 1))[0] = {lenExpr};");
        WriteNullReadsAsEmptyFlag(writer, shape, $"{shape.Identifier} is null");

        WriteSizeTest(writer, plan, paramIndex);
        using (writer.Scope())
        {
            WriteRegion(writer, plan, paramIndex, rentOnReuse: false);
            writer.AppendLine($"proxy_{shape.Name}[0] = d_{shape.Name};");
        }

        writer.AppendLine($"kernelParams[{paramIndex}] = proxy_{shape.Name};");
    }

    // ((int*)(proxy_<name> + 1))[1] = <null test> ? GSharpArrayProxy.NullReadsAsEmpty : 0;          (an optional T[])
    // ((int*)(proxy_<name> + 1))[1] = 0;                                                         (any other array)
    // [WHY] The device reads a null optional array argument as an empty one (#838), also through a local or a device
    // function, while reading any other null array throws (#706); the flags word of GSharpArray tells them apart. It is
    // written for every array because stackalloc is not zeroed under [SkipLocalsInit], and the device copies flags into
    // every local and field that takes the array, so it is always defined.
    private static void WriteNullReadsAsEmptyFlag(SourceWriter writer, in KernelParameterShape shape, string nullTest)
    {
        if (!shape.IsNullableReference)
        {
            writer.AppendLine($"((int*)(proxy_{shape.Name} + 1))[1] = 0;");
            return;
        }

        writer.AppendLine(
            $"((int*)(proxy_{shape.Name} + 1))[1] = {nullTest} ? global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy.NullReadsAsEmpty : 0;");
    }

    // if ([<name> is not null && ]<name>_size > 0), the null test before a table lookup only
    private static void WriteSizeTest(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        var notNull = plan.UsesTable && plan.IsShared(paramIndex) ? NotNullPrefix(shape) : "";
        writer.AppendLine($"if ({notNull}{shape.Name}_size > 0)");
    }

    // Table:    if (!deviceArrays.TryGetRegion(<name>, out d_<name>)) { <own region> } [else { <rent shadow> }]
    // Pairwise: if (ReferenceEquals(<name>, <earlier>)) { d_<name> = d_<earlier>; [<rent shadow>] } else if ...
    //           else { <own region> }  (the shadow only when <earlier> does not copy back)
    // [WHY] An array argument is a reference: two arguments, or an argument and a struct field, that hold one array are
    // one object in C#, so the launcher gives them one device region (#721). An array that a holder before it already
    // copied gets that region and is not copied again; a converted holder that copies back rents a shadow to copy the
    // region back into (WriteConvertedArrayUnmarshal). Arguments alone are compared pairwise, with no allocation; the
    // table is there when a struct or element holds arrays (ArraySharingPlan).
    private static void WriteRegion(SourceWriter writer, in ArraySharingPlan plan, int paramIndex, bool rentOnReuse)
    {
        var shape = plan.Parameters[paramIndex];
        var name = shape.Name;
        if (!plan.IsShared(paramIndex))
        {
            WriteOwnRegion(writer, plan, paramIndex);
            return;
        }

        if (plan.UsesTable)
        {
            writer.AppendLine($"if (!{DeviceArrays}.TryGetRegion({shape.Identifier}, out d_{name}))");
            using (writer.Scope())
            {
                WriteOwnRegion(writer, plan, paramIndex);
            }

            if (rentOnReuse)
            {
                writer.AppendLine("else");
                using (writer.Scope())
                {
                    WriteShadowRent(writer, name);
                }
            }

            return;
        }

        var hasEarlier = false;
        for (var k = 0; k < plan.Parameters.Length; k++)
        {
            if (!plan.IsEarlierHolder(paramIndex, k))
            {
                continue;
            }

            var earlier = plan.Parameters[k];
            writer.AppendLine(
                $"{(hasEarlier ? "else if" : "if")} ({ReferenceEqualsMethod}({shape.Identifier}, {earlier.Identifier}))");
            using (writer.Scope())
            {
                writer.AppendLine($"d_{name} = d_{earlier.Name};");
                // An earlier holder that copies back copies this array back as well (WriteCopyBackClaim)
                if (rentOnReuse && !plan.IsEarlierCopyBack(paramIndex, k))
                {
                    WriteShadowRent(writer, name);
                }
            }

            hasEarlier = true;
        }

        if (!hasEarlier)
        {
            WriteOwnRegion(writer, plan, paramIndex);
            return;
        }

        writer.AppendLine("else");
        using (writer.Scope())
        {
            WriteOwnRegion(writer, plan, paramIndex);
        }
    }

    private static void WriteOwnRegion(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        var recorded = plan.UsesTable && plan.IsShared(paramIndex);
        if (shape.IsCudaConvertedArray)
        {
            WriteConvertedArrayAllocationAndCopyIn(writer, shape, recorded, plan.CopiesIn(paramIndex));
        }
        else
        {
            WriteArrayAllocationAndCopyIn(writer, shape, recorded, plan.CopiesIn(paramIndex));
        }
    }

    private static void WriteArrayAllocationAndCopyIn(SourceWriter writer, in KernelParameterShape shape,
        bool recorded, bool copiesIn)
    {
        writer.AppendLine(
            $"CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out d_{shape.Name}, (IntPtr){shape.Name}_size, hStream));");
        writer.AppendLineIf(recorded, $"{DeviceArrays}.Add({shape.Identifier}, d_{shape.Name});");

        if (!copiesIn)
        {
            writer.AppendLine($"// Skip HtoD copy for [Out] array: {shape.Identifier}");
            return;
        }

        writer.AppendLine($"fixed ({GetElementTypeName(shape)}* p_{shape.Name} = {shape.SpanExpression})");
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
    private static void WriteConvertedArrayMarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        var name = shape.Name;
        var lenExpr = shape.LengthExpression;

        writer.AppendLine($"// Marshal array element by element: {shape.Identifier}");
        writer.AppendLine($"int {name}_size = {lenExpr} * sizeof({ConvertedArrayEmitter.GetElementShadowPath(shape, shape.Cuda)});");

        writer.AppendLine($"IntPtr* proxy_{name} = stackalloc IntPtr[2];");
        writer.AppendLine($"proxy_{name}[0] = IntPtr.Zero;");
        writer.AppendLine($"((int*)(proxy_{name} + 1))[0] = {lenExpr};");
        WriteNullReadsAsEmptyFlag(writer, shape, $"{shape.Identifier} is null");

        WriteSizeTest(writer, plan, paramIndex);
        using (writer.Scope())
        {
            WriteRegion(writer, plan, paramIndex, rentOnReuse: plan.CopiesBack(paramIndex));
            writer.AppendLine($"proxy_{name}[0] = d_{name};");
        }

        writer.AppendLine($"kernelParams[{paramIndex}] = proxy_{name};");
    }

    private static void WriteConvertedArrayAllocationAndCopyIn(SourceWriter writer, in KernelParameterShape shape,
        bool recorded, bool copiesIn)
    {
        var name = shape.Name;
        WriteShadowRent(writer, name);
        writer.AppendLine(
            "CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(" +
            $"out d_{name}, (IntPtr){name}_size, hStream));");
        // Recorded before the elements are marshaled, so that an element that holds the same array finds it
        writer.AppendLineIf(recorded, $"{DeviceArrays}.Add({shape.Identifier}, d_{name});");

        // [WHY] An [Out] array still needs its elements' array fields on the device: the kernel writes into
        // them, and Unmarshal_ copies them back through the device pointers that Marshal_ stores
        if (!copiesIn)
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

    private static void WriteParameterUnmarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex,
        bool isDevicePtr)
    {
        var shape = plan.Parameters[paramIndex];
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
                WriteArrayUnmarshal(writer, plan, paramIndex, isDevicePtr);
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

    private static void WriteArrayUnmarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex,
        bool isDevicePtr)
    {
        if (isDevicePtr)
        {
            return;
        }

        var shape = plan.Parameters[paramIndex];
        if (!plan.CopiesBack(paramIndex))
        {
            writer.AppendLine($"// Skip DtoH copy for [In] array: {shape.Identifier}");
            return;
        }

        if (shape.IsCudaConvertedArray)
        {
            WriteConvertedArrayUnmarshal(writer, plan, paramIndex);
            return;
        }

        writer.AppendLine($"// Copy back array: {shape.Identifier}");
        writer.Append("if ");
        using (writer.Parentheses())
        {
            writer.Append($"{shape.Name}_size > 0 && d_{shape.Name} != IntPtr.Zero");
            WriteCopyBackClaim(writer, plan, paramIndex);
        }

        writer.AppendLine();
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

    private static void WriteConvertedArrayUnmarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        var name = shape.Name;
        writer.AppendLine($"// Copy back array element by element: {shape.Identifier}");
        writer.Append("if ");
        using (writer.Parentheses())
        {
            writer.Append($"{name}_size > 0 && d_{name} != IntPtr.Zero && {name}_shadow != null");
            WriteCopyBackClaim(writer, plan, paramIndex);
        }

        writer.AppendLine();
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

    // Table:    " && [<name> is not null && ]deviceArrays.TryClaimCopyBack(<name>)"
    // Pairwise: " && !ReferenceEquals(<name>, <earlier holder that copies back>)" for each
    // [WHY] The region holds the writes of every holder, so the first holder that copies back copies them all, and the
    // others would write the same bytes again (#721). A holder that never copies back ([In], a read-only container)
    // claims nothing, so a writable holder of the same array still copies it back.
    private static void WriteCopyBackClaim(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        if (!plan.IsShared(paramIndex))
        {
            return;
        }

        if (plan.UsesTable)
        {
            writer.Append($" && {NotNullPrefix(shape)}{DeviceArrays}.TryClaimCopyBack({shape.Identifier})");
            return;
        }

        for (var k = 0; k < plan.Parameters.Length; k++)
        {
            if (plan.IsEarlierCopyBack(paramIndex, k))
            {
                writer.Append($" && !{ReferenceEqualsMethod}({shape.Identifier}, {plan.Parameters[k].Identifier})");
            }
        }
    }

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

    // if (d_<name> != IntPtr.Zero[ && d_<name> != d_<earlier holder>...]) cuMemFreeAsync(d_<name>, hStream);
    // The launch's DeviceArrayRegions frees the region of an array that the table records (WriteCleanup); a pairwise
    // holder that took the region of a holder before it leaves the free to that holder
    private static void WriteParameterFree(SourceWriter writer, in ArraySharingPlan plan, int paramIndex,
        bool isDevicePtr)
    {
        if (isDevicePtr)
        {
            return;
        }

        var shape = plan.Parameters[paramIndex];
        if (shape.IsCudaConvertedArray)
        {
            writer.AppendLine(
                $"if ({shape.Name}_shadow != null) System.Buffers.ArrayPool<byte>.Shared.Return({shape.Name}_shadow);");
        }

        var freesOwnRegion = shape.IsByRef || (shape.IsArray && !(plan.UsesTable && plan.IsShared(paramIndex)));
        if (!freesOwnRegion)
        {
            return;
        }

        writer.Append("if ");
        using (writer.Parentheses())
        {
            writer.Append($"d_{shape.Name} != IntPtr.Zero");
            for (var k = 0; k < plan.Parameters.Length; k++)
            {
                if (plan.IsEarlierHolder(paramIndex, k))
                {
                    writer.Append($" && d_{shape.Name} != d_{plan.Parameters[k].Name}");
                }
            }
        }

        writer.AppendLine($" CudaDriverApi.cuMemFreeAsync(d_{shape.Name}, hStream);");
    }

    private static string GetElementTypeName(in KernelParameterShape shape) =>
        shape.ElementTypeName ??
        throw new InvalidOperationException($"Parameter '{shape.Identifier}' is not an array-like type.");
}
