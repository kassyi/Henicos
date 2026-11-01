namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits P/Invoke wrapper logic for launching CUDA kernels, including memory allocation, marshaling, and stream synchronization.</summary>
internal static class KernelLaunchEmitter
{
    // The device scratch of the grid strategy of AsParallel() aggregates (KernelHostModel.GridScratchSlotBytes)
    private const string DeviceGridScratch = "d_" + GSharpInternalNames.StackLinq.GridScratch;

    // The device regions of the arrays that array arguments, struct arguments and converted elements hold
    // (DeviceArrayRegions, ArraySharingMode.Table)
    private const string DeviceArrays = GSharpInternalNames.Launcher.DeviceArrays;

    private const string KernelParams = GSharpInternalNames.Launcher.KernelParams;
    private const string FunctionHandle = GSharpInternalNames.Launcher.FunctionHandle;
    private const string CudaDriverApiType = "global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi";
    private const string CudaExceptionType = "global::Kassyi.GSharp.Runtime.Interop.CudaException";
    private const string IntPtrType = "global::System.IntPtr";

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
                writer.AppendLine($"byte[]? {KernelHostNames.GetShadowLocal(shape)} = null;");
            }

            if (shape.IsArray || shape.IsByRef)
            {
                writer.AppendLine($"{IntPtrType} {KernelHostNames.GetDevicePointerLocal(shape)} = {IntPtrType}.Zero;");
            }
        }

        AutoResultEmitter.WriteCudaDeclaration(writer, kernel);

        writer.AppendLineIf(kernel.GridScratchSlotBytes > 0, $"{IntPtrType} {DeviceGridScratch} = {IntPtrType}.Zero;");
    }

    /// <summary>Emits argument marshaling, device memory transfers, kernel launch, and result copyback.</summary>
    internal static void WriteMarshalAndLaunchKernel(SourceWriter writer, in KernelHostModel kernel,
        in ArraySharingPlan plan, bool isDevicePtr)
    {
        var kernelParamCount = WriteKernelParamsDeclaration(writer, kernel);
        WriteParameterMarshals(writer, plan, isDevicePtr);
        AutoResultEmitter.WriteCudaMarshal(writer, kernel, kernel.Parameters.Length);
        WriteGridScratchAllocation(writer, kernel, kernelParamCount);
        writer.AppendLine();

        WriteLaunchKernelCall(writer, kernel.IsCooperative);

        for (var i = 0; i < kernel.Parameters.Length; i++)
        {
            WriteParameterUnmarshal(writer, plan, i, isDevicePtr);
        }

        AutoResultEmitter.WriteCudaCopyBack(writer, kernel);
    }

    // void** __gsharp_kernel_params = stackalloc void*[<parameters>[ + auto result][ + grid scratch]];
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

        writer.Append($"void** {KernelParams} = stackalloc void*[");
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

    // One slot per block for its partial result and one for the total, freed after the launch (WriteCleanup)
    private static void WriteGridScratchAllocation(SourceWriter writer, in KernelHostModel kernel, int kernelParamCount)
    {
        if (kernel.GridScratchSlotBytes <= 0)
        {
            return;
        }

        writer.AppendLine("// Allocate the scratch of the grid strategy of AsParallel() aggregates");
        writer.AppendLine(
            $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemAllocAsync(" +
            $"out {DeviceGridScratch}, ({IntPtrType})(((long)gridDimX * gridDimY * gridDimZ + 1) * {kernel.GridScratchSlotBytes}), hStream));");
        writer.AppendLine($"{KernelParams}[{kernelParamCount - 1}] = &{DeviceGridScratch};");
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
            writer.AppendLine($"if ({DeviceGridScratch} != {IntPtrType}.Zero) {CudaDriverApiType}.cuMemFreeAsync({DeviceGridScratch}, hStream);");
        }

        AutoResultEmitter.WriteCudaFree(writer, kernel);
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
                var proxy = KernelHostNames.GetProxyLocal(shape);
                writer.AppendLine($"{IntPtrType}* {proxy} = stackalloc {IntPtrType}[2];");
                writer.AppendLine($"{proxy}[0] = {shape.Identifier};");
                writer.AppendLine($"((int*)({proxy} + 1))[0] = 0; // Length is unknown for raw IntPtr");
                // The caller's pointer may be IntPtr.Zero for any array: a non-optional one then reads as null (#706)
                WriteNullReadsAsEmptyFlag(writer, shape, $"{shape.Identifier} == {IntPtrType}.Zero");
                writer.AppendLine($"{KernelParams}[{paramIndex}] = {proxy};");
            }
            else
            {
                writer.AppendLine($"{KernelParams}[{paramIndex}] = &{shape.Identifier};");
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
            writer.AppendLine($"kernelParams[{paramIndex}] = &{shape.Identifier};".Replace("kernelParams", KernelParams));
            return;
        }

        var native = KernelHostNames.GetNativeLocal(shape);
        writer.AppendLine($"{shape.AotStructPath} {native} = default;");
        writer.Append(shape.MarshalMethodName);
        using (writer.Parentheses())
        {
            writer.Append(
                $"ref {shape.Identifier}, ref {native}, false, {DeviceArraysFor(shape)}, hStream");
        }

        writer.AppendLine(";");

        if (shape.RefKind is RefKind.Ref or RefKind.Out)
        {
            var size = KernelHostNames.GetSizeLocal(shape);
            var device = KernelHostNames.GetDevicePointerLocal(shape);
            writer.AppendLine($"// Marshal ref/out struct: {shape.Identifier}");
            writer.AppendLine($"int {size} = sizeof({shape.AotStructPath});");

            writer.AppendLine(
                $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemAllocAsync(out {device}, ({IntPtrType}){size}, hStream));");
            if (shape.RefKind == RefKind.Ref)
            {
                writer.AppendLine("// Fast zero-allocation copy to device");
                writer.AppendLine(
                    $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemcpyHtoDAsync_v2({device}, " +
                    $"({IntPtrType})(&{native}), ({IntPtrType}){size}, hStream));");
                writer.AppendLine($"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuStreamSynchronize(hStream));");
            }

            writer.AppendLine($"{KernelParams}[{paramIndex}] = &{device};");
        }
        else
        {
            writer.AppendLine($"// Marshal scalar struct: {shape.Identifier}");
            writer.AppendLine($"{KernelParams}[{paramIndex}] = &{native};");
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
        var size = KernelHostNames.GetSizeLocal(shape);
        var proxy = KernelHostNames.GetProxyLocal(shape);
        var device = KernelHostNames.GetDevicePointerLocal(shape);

        writer.AppendLine($"// Marshal array: {shape.Identifier}");
        writer.AppendLine($"int {size} = checked({lenExpr} * sizeof({GetElementTypeName(shape)}));");

        writer.AppendLine($"{IntPtrType}* {proxy} = stackalloc {IntPtrType}[2];");
        writer.AppendLine($"{proxy}[0] = {IntPtrType}.Zero;");
        writer.AppendLine($"((int*)({proxy} + 1))[0] = {lenExpr};");
        WriteNullReadsAsEmptyFlag(writer, shape, $"{shape.Identifier} is null");

        WriteSizeTest(writer, plan, paramIndex);
        using (writer.Scope())
        {
            WriteRegion(writer, plan, paramIndex, rentOnReuse: false);
            writer.AppendLine($"{proxy}[0] = {device};");
        }

        writer.AppendLine($"{KernelParams}[{paramIndex}] = {proxy};");
    }

    // ((int*)(__gsharp_proxy_<ord> + 1))[1] = <null test> ? GSharpArrayProxy.NullReadsAsEmpty : 0;   (an optional T[])
    // ((int*)(__gsharp_proxy_<ord> + 1))[1] = 0;                                                     (any other array)
    // [WHY] The device reads a null optional array argument as an empty one (#838), also through a local or a device
    // function, while reading any other null array throws (#706); the flags word of GSharpArray tells them apart. It is
    // written for every array because stackalloc is not zeroed under [SkipLocalsInit], and the device copies flags into
    // every local and field that takes the array, so it is always defined.
    private static void WriteNullReadsAsEmptyFlag(SourceWriter writer, in KernelParameterShape shape, string nullTest)
    {
        var proxy = KernelHostNames.GetProxyLocal(shape);
        if (!shape.IsNullableReference)
        {
            writer.AppendLine($"((int*)({proxy} + 1))[1] = 0;");
            return;
        }

        writer.AppendLine(
            $"((int*)({proxy} + 1))[1] = {nullTest} ? global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy.NullReadsAsEmpty : 0;");
    }

    // if ([<name> is not null && ]__gsharp_size_<ord> > 0), the null test before a table lookup only
    private static void WriteSizeTest(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        var notNull = plan.UsesTable && plan.IsShared(paramIndex) ? NotNullPrefix(shape) : "";
        writer.AppendLine($"if ({notNull}{KernelHostNames.GetSizeLocal(shape)} > 0)");
    }

    // Table:    if (!deviceArrays.TryGetRegion(<name>, out __gsharp_d_<ord>)) { <own region> } [else { <rent shadow> }]
    // Pairwise: if (ReferenceEquals(<name>, <earlier>)) { __gsharp_d_<ord> = __gsharp_d_<earlier>; [<rent shadow>] } else if ...
    //           else { <own region> }  (the shadow only when <earlier> does not copy back)
    // [WHY] An array argument is a reference: two arguments, or an argument and a struct field, that hold one array are
    // one object in C#, so the launcher gives them one device region (#721). An array that a holder before it already
    // copied gets that region and is not copied again; a converted holder that copies back rents a shadow to copy the
    // region back into (WriteConvertedArrayUnmarshal). Arguments alone are compared pairwise, with no allocation; the
    // table is there when a struct or element holds arrays (ArraySharingPlan).
    private static void WriteRegion(SourceWriter writer, in ArraySharingPlan plan, int paramIndex, bool rentOnReuse)
    {
        var shape = plan.Parameters[paramIndex];
        var device = KernelHostNames.GetDevicePointerLocal(shape);
        if (!plan.IsShared(paramIndex))
        {
            WriteOwnRegion(writer, plan, paramIndex);
            return;
        }

        if (plan.UsesTable)
        {
            writer.AppendLine($"if (!{DeviceArrays}.TryGetRegion({shape.Identifier}, out {device}))");
            using (writer.Scope())
            {
                WriteOwnRegion(writer, plan, paramIndex);
            }

            if (rentOnReuse)
            {
                writer.AppendLine("else");
                using (writer.Scope())
                {
                    WriteShadowRent(writer, shape);
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
                writer.AppendLine($"{device} = {KernelHostNames.GetDevicePointerLocal(earlier)};");
                // An earlier holder that copies back copies this array back as well (WriteCopyBackClaim)
                if (rentOnReuse && !plan.IsEarlierCopyBack(paramIndex, k))
                {
                    WriteShadowRent(writer, shape);
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
        var device = KernelHostNames.GetDevicePointerLocal(shape);
        var size = KernelHostNames.GetSizeLocal(shape);
        var pinned = KernelHostNames.GetPinnedLocal(shape);
        writer.AppendLine(
            $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemAllocAsync(out {device}, ({IntPtrType}){size}, hStream));");
        writer.AppendLineIf(recorded, $"{DeviceArrays}.Add({shape.Identifier}, {device});");

        if (!copiesIn)
        {
            writer.AppendLine($"// Skip HtoD copy for [Out] array: {shape.Identifier}");
            return;
        }

        writer.AppendLine($"fixed ({GetElementTypeName(shape)}* {pinned} = {shape.SpanExpression})");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemcpyHtoDAsync_v2(" +
                $"{device}, ({IntPtrType}){pinned}, ({IntPtrType}){size}, hStream));");
            writer.AppendLine($"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuStreamSynchronize(hStream));");
        }
    }

    // [WHY] The host and device layouts of the element differ (GSH0080), so the elements are converted into a shadow
    // array of the device layout, rented from ArrayPool, and the shadow is copied in one transfer.
    private static void WriteConvertedArrayMarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        var lenExpr = shape.LengthExpression;
        var size = KernelHostNames.GetSizeLocal(shape);
        var proxy = KernelHostNames.GetProxyLocal(shape);
        var device = KernelHostNames.GetDevicePointerLocal(shape);

        writer.AppendLine($"// Marshal array element by element: {shape.Identifier}");
        writer.AppendLine($"int {size} = checked({lenExpr} * sizeof({ConvertedArrayEmitter.GetElementShadowPath(shape, shape.Cuda)}));");

        writer.AppendLine($"{IntPtrType}* {proxy} = stackalloc {IntPtrType}[2];");
        writer.AppendLine($"{proxy}[0] = {IntPtrType}.Zero;");
        writer.AppendLine($"((int*)({proxy} + 1))[0] = {lenExpr};");
        WriteNullReadsAsEmptyFlag(writer, shape, $"{shape.Identifier} is null");

        WriteSizeTest(writer, plan, paramIndex);
        using (writer.Scope())
        {
            WriteRegion(writer, plan, paramIndex, rentOnReuse: plan.CopiesBack(paramIndex));
            writer.AppendLine($"{proxy}[0] = {device};");
        }

        writer.AppendLine($"{KernelParams}[{paramIndex}] = {proxy};");
    }

    private static void WriteConvertedArrayAllocationAndCopyIn(SourceWriter writer, in KernelParameterShape shape,
        bool recorded, bool copiesIn)
    {
        var device = KernelHostNames.GetDevicePointerLocal(shape);
        var size = KernelHostNames.GetSizeLocal(shape);
        var shadow = KernelHostNames.GetShadowLocal(shape);
        var pinned = KernelHostNames.GetPinnedLocal(shape);
        WriteShadowRent(writer, shape);
        writer.AppendLine(
            $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemAllocAsync(" +
            $"out {device}, ({IntPtrType}){size}, hStream));");
        // Recorded before the elements are marshaled, so that an element that holds the same array finds it
        writer.AppendLineIf(recorded, $"{DeviceArrays}.Add({shape.Identifier}, {device});");

        // [WHY] An [Out] array still needs its elements' array fields on the device: the kernel writes into
        // them, and Unmarshal_ copies them back through the device pointers that Marshal_ stores
        if (!copiesIn)
        {
            writer.AppendLine($"// Skip HtoD copy for [Out] array: {shape.Identifier}");
            return;
        }

        ConvertedArrayEmitter.WriteShadowAndHostSpans(writer, shape, shape.Cuda);
        ConvertedArrayEmitter.WriteElementMarshalLoop(writer, shape, shape.Cuda, DeviceArraysFor(shape));
        writer.AppendLine($"fixed (byte* {pinned} = {shadow})");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemcpyHtoDAsync_v2(" +
                $"{device}, ({IntPtrType}){pinned}, ({IntPtrType}){size}, hStream));");
            writer.AppendLine($"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuStreamSynchronize(hStream));");
        }
    }

    private static void WriteShadowRent(SourceWriter writer, in KernelParameterShape shape) =>
        writer.AppendLine($"{KernelHostNames.GetShadowLocal(shape)} = global::System.Buffers.ArrayPool<byte>.Shared.Rent({KernelHostNames.GetSizeLocal(shape)});");

    private static void WriteRefOrOutScalarMarshal(SourceWriter writer, in KernelParameterShape shape, int paramIndex)
    {
        var size = KernelHostNames.GetSizeLocal(shape);
        var device = KernelHostNames.GetDevicePointerLocal(shape);
        var pinnedTemp = KernelHostNames.GetPinnedTempLocal(shape);
        writer.AppendLine($"// Marshal ref/out scalar: {shape.Identifier}");
        writer.AppendLine($"int {size} = sizeof({shape.TypeName});");
        writer.AppendLine(
            $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemAllocAsync(out {device}, ({IntPtrType}){size}, hStream));");

        if (shape.RefKind == RefKind.Ref)
        {
            writer.AppendLine("// Fast zero-allocation copy to device");
            writer.AppendLine($"fixed ({shape.TypeName}* {pinnedTemp} = &{shape.Identifier})");
            using (writer.Scope())
            {
                writer.AppendLine(
                    $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemcpyHtoDAsync_v2({device}," +
                    $" ({IntPtrType}){pinnedTemp}, ({IntPtrType}){size}, hStream));");
                writer.AppendLine($"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuStreamSynchronize(hStream));");
            }
        }

        writer.AppendLine($"{KernelParams}[{paramIndex}] = &{device};");
    }

    private static void WriteScalarMarshal(SourceWriter writer, in KernelParameterShape shape, int paramIndex)
    {
        writer.AppendLine($"// Marshal scalar: {shape.Identifier}");
        writer.AppendLine($"{KernelParams}[{paramIndex}] = &{shape.Identifier};");
    }

    private static void WriteLaunchKernelCall(SourceWriter writer, bool isCooperative)
    {
        writer.Append($"{CudaExceptionType}.ThrowIfError");
        using (writer.Parentheses())
        {
            writer.Append(isCooperative ? $"{CudaDriverApiType}.cuLaunchCooperativeKernel" : $"{CudaDriverApiType}.cuLaunchKernel");
            using (writer.Parentheses())
            {
                writer.AppendLine();
                writer.AppendLine($"{FunctionHandle},");
                writer.AppendLine("gridDimX, gridDimY, gridDimZ, // Grid dim");
                writer.AppendLine("blockDimX, blockDimY, blockDimZ, // Block dim");
                writer.AppendLine("sharedMemBytes, // Shared mem");
                writer.AppendLine("hStream, // Stream");
                if (isCooperative)
                {
                    writer.Append(KernelParams);
                }
                else
                {
                    writer.AppendLine($"{KernelParams},");
                    writer.Append($"{IntPtrType}.Zero // Extra");
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

        var native = KernelHostNames.GetNativeLocal(shape);
        var device = KernelHostNames.GetDevicePointerLocal(shape);
        var size = KernelHostNames.GetSizeLocal(shape);
        if (shape.IsByRef)
        {
            if (!isDevicePtr)
            {
                writer.AppendLine("// Fast zero-allocation copy from device");
                writer.AppendLine(
                    $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemcpyDtoHAsync_v2(({IntPtrType})(&{native})," +
                    $" {device}, ({IntPtrType}){size}, hStream));");
                writer.AppendLine($"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuStreamSynchronize(hStream));");
            }
        }

        writer.Append(shape.UnmarshalMethodName);
        using (writer.Parentheses())
        {
            writer.Append($"ref {shape.Identifier}, ref {native}, {(isDevicePtr ? "true" : "false")}, hStream");
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

        var size = KernelHostNames.GetSizeLocal(shape);
        var device = KernelHostNames.GetDevicePointerLocal(shape);
        var pinned = KernelHostNames.GetPinnedLocal(shape);
        writer.AppendLine($"// Copy back array: {shape.Identifier}");
        writer.Append("if ");
        using (writer.Parentheses())
        {
            writer.Append($"{size} > 0 && {device} != {IntPtrType}.Zero");
            WriteCopyBackClaim(writer, plan, paramIndex);
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            writer.AppendLine($"fixed ({GetElementTypeName(shape)}* {pinned} = {shape.SpanExpression})");
            using (writer.Scope())
            {
                writer.AppendLine(
                    $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemcpyDtoHAsync_v2(({IntPtrType}){pinned}, {device}, ({IntPtrType}){size}, hStream));");
                writer.AppendLine($"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuStreamSynchronize(hStream));");
            }
        }
    }

    private static void WriteConvertedArrayUnmarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        var size = KernelHostNames.GetSizeLocal(shape);
        var device = KernelHostNames.GetDevicePointerLocal(shape);
        var shadow = KernelHostNames.GetShadowLocal(shape);
        var pinned = KernelHostNames.GetPinnedLocal(shape);
        writer.AppendLine($"// Copy back array element by element: {shape.Identifier}");
        writer.Append("if ");
        using (writer.Parentheses())
        {
            writer.Append($"{size} > 0 && {device} != {IntPtrType}.Zero && {shadow} != null");
            WriteCopyBackClaim(writer, plan, paramIndex);
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            writer.AppendLine($"fixed (byte* {pinned} = {shadow})");
            using (writer.Scope())
            {
                writer.AppendLine(
                    $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemcpyDtoHAsync_v2(" +
                    $"({IntPtrType}){pinned}, {device}, ({IntPtrType}){size}, hStream));");
                writer.AppendLine($"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuStreamSynchronize(hStream));");
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

        var scalarPtr = KernelHostNames.GetScalarPointerLocal(shape);
        var device = KernelHostNames.GetDevicePointerLocal(shape);
        var size = KernelHostNames.GetSizeLocal(shape);
        writer.AppendLine($"// Fast zero-allocation copy from device for scalar: {shape.Identifier}");
        writer.AppendLine($"fixed ({shape.TypeName}* {scalarPtr} = &{shape.Identifier})");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemcpyDtoHAsync_v2(" +
                $"({IntPtrType}){scalarPtr}, {device}, ({IntPtrType}){size}, hStream));");
            writer.AppendLine($"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuStreamSynchronize(hStream));");
        }
    }

    // if (__gsharp_d_<ord> != IntPtr.Zero[ && __gsharp_d_<ord> != __gsharp_d_<earlier holder>...]) cuMemFreeAsync(__gsharp_d_<ord>, hStream);
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
            var shadow = KernelHostNames.GetShadowLocal(shape);
            writer.AppendLine(
                $"if ({shadow} != null) global::System.Buffers.ArrayPool<byte>.Shared.Return({shadow});");
        }

        var freesOwnRegion = shape.IsByRef || (shape.IsArray && !(plan.UsesTable && plan.IsShared(paramIndex)));
        if (!freesOwnRegion)
        {
            return;
        }

        var device = KernelHostNames.GetDevicePointerLocal(shape);
        writer.Append("if ");
        using (writer.Parentheses())
        {
            writer.Append($"{device} != {IntPtrType}.Zero");
            for (var k = 0; k < plan.Parameters.Length; k++)
            {
                if (plan.IsEarlierHolder(paramIndex, k))
                {
                    writer.Append($" && {device} != {KernelHostNames.GetDevicePointerLocal(plan.Parameters[k])}");
                }
            }
        }

        writer.AppendLine($" {CudaDriverApiType}.cuMemFreeAsync({device}, hStream);");
    }

    private static string GetElementTypeName(in KernelParameterShape shape) =>
        shape.ElementTypeName ??
        throw new InvalidOperationException($"Parameter '{shape.Identifier}' is not an array-like type.");
}
