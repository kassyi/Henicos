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

    // The epoch of the launch, passed as the hidden last kernel parameter
    private const string LaunchEpoch = GSharpInternalNames.Launcher.Epoch;
    private const string FunctionHandle = GSharpInternalNames.Launcher.FunctionHandle;
    private const string CudaDriverApiType = HostTypeNames.CudaDriverApi;
    private const string CudaExceptionType = HostTypeNames.CudaException;
    private const string IntPtrType = HostTypeNames.IntPtr;
    private const string ArrayProxyType = HostTypeNames.ArrayProxy;
    private const string LaunchErrorRecordsType = HostTypeNames.LaunchErrorRecords;

    /// <summary>
    /// Whether the launcher reads the device error record of its launch itself: the managed-array launcher after its
    /// copy-back, and every launcher of the checked build after its stream synchronization. Such a launcher owns its
    /// launch epoch until it reads the record or the launch ends (LaunchErrorRecords).
    /// </summary>
    internal static bool ReadsOwnRecord(bool isDevicePtr, bool isCheckedBuild) => isCheckedBuild || !isDevicePtr;

    internal enum CopyDirection
    {
        HostToDevice,
        DeviceToHost,
    }

    private static void WriteSynchronizedCopy(SourceWriter writer, CopyDirection direction, string destination,
        string source, string size, string stream = "hStream")
    {
        var method = direction is CopyDirection.HostToDevice ? "cuMemcpyHtoDAsync_v2" : "cuMemcpyDtoHAsync_v2";
        writer.AppendLine(
            $"{HostTypeNames.CudaException}.ThrowIfError({HostTypeNames.CudaDriverApi}.{method}({destination}, {source}, ({HostTypeNames.IntPtr}){size}, {stream}));");
        writer.AppendLine(
            $"{HostTypeNames.CudaException}.ThrowIfError({HostTypeNames.CudaDriverApi}.cuStreamSynchronize({stream}));");
    }

    private const string ReferenceEqualsMethod = ArraySharingPlan.ReferenceEqualsMethod;

    /// <summary>Emits local variable declarations for device pointers and tracking lists.</summary>
    internal static void WriteVariablesInitialization(SourceWriter writer, in KernelHostModel kernel,
        in ArraySharingPlan plan, bool isDevicePtr, bool readsOwnRecord)
    {
        // One table for the whole launch, so that every array argument, struct argument and converted element finds the
        // arrays the others already copied (#707, #721)
        writer.AppendLineIf(plan.UsesTable,
            $"{StructMarshalingEmitter.DeviceArrayRegionsType} {DeviceArrays} = new();");
        AliasGroupEmitter.WriteDeclaration(writer, plan.Aliases);
        foreach (var shape in kernel.Parameters)
        {
            if (isDevicePtr)
            {
                continue;
            }

            if (shape.IsCudaConvertedArray)
            {
                writer.AppendLine($"byte[]? {KernelHostNames.GetLocal(LauncherLocal.Shadow, shape)} = null;");
            }

            if (shape.IsArray || shape.IsByRef)
            {
                writer.AppendLine(
                    $"{IntPtrType} {KernelHostNames.GetLocal(LauncherLocal.DevicePointer, shape)} = " +
                    $"{IntPtrType}.Zero;");
            }
        }

        AutoResultEmitter.WriteCudaDeclaration(writer, kernel);

        writer.AppendLineIf(kernel.GridScratchSlotBytes > 0, $"{IntPtrType} {DeviceGridScratch} = {IntPtrType}.Zero;");

        // [WHY] Taken before anything is allocated: it throws the unconsumed error of an earlier launch whose record
        // this launch reuses. A kernel that waits at a barrier counting a fixed number of arrivals passes its function,
        // so that a launch captured into a CUDA graph gets an abort word that a node of the graph clears before every
        // replay
        writer.Append($"uint {LaunchEpoch} = {LaunchErrorRecordsType}.NextLaunchEpoch");
        using (writer.Parentheses())
        {
            writer.Append($"hStream, {(readsOwnRecord ? "true" : "false")}");
            writer.AppendIf(kernel.WaitsForArrivals, $", {FunctionHandle}");
        }

        writer.AppendLine(";");
    }

    /// <summary>Emits argument marshaling, device memory transfers, kernel launch, and result copyback.</summary>
    internal static void WriteMarshalAndLaunchKernel(SourceWriter writer, in KernelHostModel kernel,
        in ArraySharingPlan plan, bool isDevicePtr, bool isCheckedBuild = false)
    {
        var kernelParamCount = WriteKernelParamsDeclaration(writer, kernel);
        AliasGroupEmitter.WriteCudaGroups(writer, plan);
        WriteParameterMarshals(writer, plan, isDevicePtr);
        AutoResultEmitter.WriteCudaMarshal(writer, kernel, kernel.Parameters.Length);
        // The epoch is the last slot, the grid scratch the one before it
        WriteGridScratchAllocation(writer, kernel, kernelParamCount - 2);
        writer.AppendLine($"{KernelParams}[{kernelParamCount - 1}] = &{LaunchEpoch};");
        writer.AppendLine();

        WriteLaunchKernelCall(writer, kernel.IsCooperative);
        AliasGroupEmitter.WriteCudaCopyBack(writer, plan);

        for (var i = 0; i < kernel.Parameters.Length; i++)
        {
            WriteParameterUnmarshal(writer, plan, i, isDevicePtr);
        }

        AutoResultEmitter.WriteCudaCopyBack(writer, kernel);
        writer.AppendLineIf(isCheckedBuild,
            $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuStreamSynchronize(hStream));");
        // A failing device thread records its error and exits, and the kernel completes: the launcher reads the record
        // of its own epoch once the stream has synchronized (copy-back or checked build). A launch with neither leaves
        // it to the next synchronization of the stream. A managed-array launcher whose copy-back may copy nothing
        // synchronizes first, so that it finds its record.
        writer.AppendLineIf(!isDevicePtr && !isCheckedBuild && !CopyBackAlwaysSynchronizes(kernel),
            $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuStreamSynchronize(hStream));");
        writer.AppendLineIf(ReadsOwnRecord(isDevicePtr, isCheckedBuild),
            $"{LaunchErrorRecordsType}.ThrowIfLaunchError({LaunchEpoch});");
    }

    // Whether the copy-back of the managed-array launcher synchronizes the stream in every launch: a ref / out scalar
    // or a by-ref struct is copied back in every launch, an array or the auto result only when it is not empty
    private static bool CopyBackAlwaysSynchronizes(in KernelHostModel kernel) =>
        kernel.Parameters
            .AsStackEnumerable()
            .Any(static shape => shape.Kind == KernelParameterKind.RefScalar ||
                (shape.Kind == KernelParameterKind.CustomStruct && shape.IsByRef && !shape.IsDirectCudaValueStruct));

    // void** __gsharp_kernel_params = stackalloc void*[<parameters>[ + auto result][ + grid scratch] + epoch];
    // Returns the number of slots.
    private static int WriteKernelParamsDeclaration(SourceWriter writer, in KernelHostModel kernel)
    {
        var kernelParamCount = kernel.Parameters.Length + 1;

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
    private static void WriteGridScratchAllocation(SourceWriter writer, in KernelHostModel kernel, int slot)
    {
        if (kernel.GridScratchSlotBytes <= 0)
        {
            return;
        }

        writer.AppendLine("// Allocate the scratch of the grid strategy of AsParallel() aggregates");
        writer.AppendLine(
            $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemAllocAsync(" +
            $"out {DeviceGridScratch}, ({IntPtrType})(((long)gridDimX * gridDimY * gridDimZ + 1) * {kernel.GridScratchSlotBytes}), hStream));");
        writer.AppendLine($"{KernelParams}[{slot}] = &{DeviceGridScratch};");
    }

    /// <summary>Emits cleanup code to asynchronously free allocated device memory buffers.</summary>
    internal static void WriteCleanup(SourceWriter writer, in KernelHostModel kernel, in ArraySharingPlan plan,
        bool isDevicePtr, bool readsOwnRecord)
    {
        // A launch that ends before its launcher read the record (an argument copy failed) leaves the record to the
        // next synchronization; after ThrowIfLaunchError this does nothing
        writer.AppendLineIf(readsOwnRecord, $"{LaunchErrorRecordsType}.ReleaseLaunchEpoch({LaunchEpoch});");
        for (var i = 0; i < kernel.Parameters.Length; i++)
        {
            WriteParameterFree(writer, plan, i, isDevicePtr);
        }

        writer.AppendLineIf(plan.UsesTable, $"{DeviceArrays}.Free(hStream);");
        AliasGroupEmitter.WriteCudaFree(writer, plan.Aliases);

        if (kernel.GridScratchSlotBytes > 0)
        {
            writer.AppendLine(
                $"if ({DeviceGridScratch} != {IntPtrType}.Zero) " +
                $"{CudaDriverApiType}.cuMemFreeAsync({DeviceGridScratch}, hStream);");
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
        isDevicePtr && PointerLaunchPlan.Classify(shape) != PointerArgumentKind.Value;

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
                var proxy = KernelHostNames.GetLocal(LauncherLocal.Proxy, shape);
                var value = PointerLaunchPlan.GetProxyExpression(shape);
                WriteProxyDeclaration(writer, shape);
                // Data == 0 is a null array, which WritePointerArrayArgumentChecks has checked to be 0 long
                writer.AppendLine($"{proxy}[0] = {value}.Data;");
                writer.AppendLine($"((int*)({proxy} + 1))[0] = {value}.Length;");
                WriteReservedWord(writer, proxy);
                writer.AppendLine($"{KernelParams}[{paramIndex}] = {GetArgumentPointer(shape)};");
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
                WriteCustomStructMarshal(writer, plan, paramIndex);
                break;
            case KernelParameterKind.Array:
                WriteArrayMarshal(writer, plan, paramIndex);
                break;
            case KernelParameterKind.RefScalar:
                WriteRefOrOutScalarMarshal(writer, plan, paramIndex);
                break;
            default:
                WriteScalarMarshal(writer, shape, paramIndex);
                break;
        }
    }

    // Only the managed-array launcher gets here: the Ptr launcher passes a struct through (IsPassedThrough)
    private static void WriteCustomStructMarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        if (shape.IsDirectCudaValueStruct)
        {
            writer.AppendLine($"// Marshal scalar struct: {shape.Identifier}");
            writer.AppendLine($"{KernelParams}[{paramIndex}] = &{shape.Identifier};");
            return;
        }

        var native = KernelHostNames.GetLocal(LauncherLocal.Native, shape);
        writer.AppendLine($"{shape.AotStructPath} {native} = default;");
        writer.Append(shape.MarshalMethodName);
        using (writer.Parentheses())
        {
            writer.Append(
                $"ref {shape.Identifier}, ref {native}, {DeviceArraysFor(shape)}, hStream");
        }

        writer.AppendLine(";");

        if (shape.RefKind is RefKind.Ref or RefKind.Out)
        {
            var size = KernelHostNames.GetLocal(LauncherLocal.Size, shape);
            var device = KernelHostNames.GetLocal(LauncherLocal.DevicePointer, shape);
            writer.AppendLine($"// Marshal ref/out struct: {shape.Identifier}");
            writer.AppendLine($"int {size} = sizeof({shape.AotStructPath});");
            using (WriteGroupMemberBranch(writer, plan, paramIndex))
            {
                WriteDeviceAllocation(writer, device, size);
                if (shape.RefKind == RefKind.Ref)
                {
                    writer.AppendLine("// Fast zero-allocation copy to device");
                    var source = $"({IntPtrType})(&{native})";
                    WriteSynchronizedCopy(writer, CopyDirection.HostToDevice, device, source, size);
                }
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
        var size = KernelHostNames.GetLocal(LauncherLocal.Size, shape);
        var proxy = KernelHostNames.GetLocal(LauncherLocal.Proxy, shape);
        var device = KernelHostNames.GetLocal(LauncherLocal.DevicePointer, shape);

        writer.AppendLine($"// Marshal array: {shape.Identifier}");
        var element = ConvertedArrayEmitter.GetElementTypeName(shape);
        writer.AppendLine($"int {size} = checked({lenExpr} * sizeof({element}));");

        WriteProxyDeclaration(writer, shape);
        WriteArrayReference(writer, shape, proxy);
        writer.AppendLine($"((int*)({proxy} + 1))[0] = {lenExpr};");
        WriteReservedWord(writer, proxy);

        WriteSizeTest(writer, plan, paramIndex);
        using (writer.Scope())
        {
            using (WriteGroupMemberBranch(writer, plan, paramIndex))
            {
                WriteRegion(writer, plan, paramIndex, rentOnReuse: false);
            }

            writer.AppendLine($"{proxy}[0] = {device};");
        }

        writer.AppendLine($"{KernelParams}[{paramIndex}] = {GetArgumentPointer(shape)};");
    }

    // if (__gsharp_aliases?.IsMember(<k>) == true) { __gsharp_d_<ord> = __gsharp_aliases.GetDevicePointer(<k>); }
    // else { <the caller's own region> }   (returns the else scope; an empty scope when the parameter is no member)
    // [WHY] A group of overlapping arguments has one region, which the launcher filled before the marshaling
    // (AliasGroupEmitter.WriteCudaGroups); a member takes its address in it instead of a copy of its own (#857)
    private static SourceWriterScope WriteGroupMemberBranch(SourceWriter writer, in ArraySharingPlan plan,
        int paramIndex)
    {
        if (AliasGroupEmitter.MemberTest(plan.Aliases, paramIndex) is not { } member)
        {
            return default;
        }

        var device = KernelHostNames.GetLocal(LauncherLocal.DevicePointer, plan.Parameters[paramIndex]);
        writer.AppendLine($"if ({member})");
        using (writer.Scope())
        {
            writer.AppendLine($"{device} = {AliasGroupEmitter.DevicePointer(plan.Aliases, paramIndex)};");
        }

        writer.AppendLine("else");
        return writer.Scope();
    }

    // CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out <device>, (IntPtr)<size>, hStream));
    private static void WriteDeviceAllocation(SourceWriter writer, string device, string size) =>
        writer.AppendLine(
            $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemAllocAsync(" +
            $"out {device}, ({IntPtrType}){size}, hStream));");

    // " && !(__gsharp_aliases?.IsMember(<k>) == true)": a member's group copies it back and frees its region
    private static string NotGroupMember(in ArraySharingPlan plan, int paramIndex) =>
        AliasGroupEmitter.MemberTest(plan.Aliases, paramIndex) is { } member ? $" && !({member})" : "";

    // An array-like argument:
    // IntPtr* __gsharp_proxy_<ord> = stackalloc IntPtr[2];
    // A Nullable<array-like> (Memory<T>?):
    // IntPtr* __gsharp_nullable_<ord> = stackalloc IntPtr[3];
    // __gsharp_nullable_<ord>[0] = <name>.HasValue ? 1 : 0;
    // IntPtr* __gsharp_proxy_<ord> = __gsharp_nullable_<ord> + 1;
    // [WHY] The device takes a Memory<T>? as System_Nullable<GSharpArray<T>>, HasValue in its first byte and the array
    // at byte 8, as it takes every other Nullable<T>; the value of a default Memory<T> is a null array that has a value
    private static void WriteProxyDeclaration(SourceWriter writer, in KernelParameterShape shape)
    {
        var proxy = KernelHostNames.GetLocal(LauncherLocal.Proxy, shape);
        if (!shape.IsNullableValue)
        {
            writer.AppendLine($"{IntPtrType}* {proxy} = stackalloc {IntPtrType}[2];");
            return;
        }

        var nullable = KernelHostNames.GetLocal(LauncherLocal.NullableProxy, shape);
        writer.AppendLine($"{IntPtrType}* {nullable} = stackalloc {IntPtrType}[3];");
        writer.AppendLine($"{nullable}[0] = ({IntPtrType})({shape.Identifier}.HasValue ? 1 : 0);");
        writer.AppendLine($"{IntPtrType}* {proxy} = {nullable} + 1;");
    }

    // The address of the kernel argument: the proxy, or the Nullable that holds it (WriteProxyDeclaration)
    private static string GetArgumentPointer(in KernelParameterShape shape) =>
        KernelHostNames.GetLocal(shape.IsNullableValue ? LauncherLocal.NullableProxy : LauncherLocal.Proxy, shape);

    // __gsharp_proxy_<ord>[0] = <null test> ? IntPtr.Zero : (IntPtr)GSharpArrayProxy.EmptyArrayData;
    // [WHY] A null array (or a span-like value over none) is a null pointer, and an empty array a non-null one that is
    // never dereferenced, so that the device tells them apart as C# does; a copied array replaces it with its region
    private static void WriteArrayReference(SourceWriter writer, in KernelParameterShape shape, string proxy)
    {
        writer.Append($"{proxy}[0] = {shape.NullTestExpression} ? ");
        writer.AppendLine($"{IntPtrType}.Zero : ({IntPtrType}){ArrayProxyType}.EmptyArrayData;");
    }

    // ((int*)(__gsharp_proxy_<ord> + 1))[1] = 0;
    // [WHY] The 4 bytes after the length (GSharpArray::flags) are reserved; stackalloc is not zeroed under
    // [SkipLocalsInit], and the device copies them into every local and field that takes the array
    private static void WriteReservedWord(SourceWriter writer, string proxy) =>
        writer.AppendLine($"((int*)({proxy} + 1))[1] = 0;");

    // if ([<name> is not null && ]__gsharp_size_<ord> > 0), the null test before a table lookup only
    private static void WriteSizeTest(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        var notNull = plan.UsesTable && plan.IsShared(paramIndex) ? NotNullPrefix(shape) : "";
        writer.AppendLine($"if ({notNull}{KernelHostNames.GetLocal(LauncherLocal.Size, shape)} > 0)");
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
        var device = KernelHostNames.GetLocal(LauncherLocal.DevicePointer, shape);
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
                writer.AppendLine($"{device} = {KernelHostNames.GetLocal(LauncherLocal.DevicePointer, earlier)};");
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
        var device = KernelHostNames.GetLocal(LauncherLocal.DevicePointer, shape);
        var size = KernelHostNames.GetLocal(LauncherLocal.Size, shape);
        var pinned = KernelHostNames.GetLocal(LauncherLocal.Pinned, shape);
        writer.AppendLine(
            $"{CudaExceptionType}.ThrowIfError({CudaDriverApiType}.cuMemAllocAsync(out {device}, ({IntPtrType}){size}, hStream));");
        writer.AppendLineIf(recorded, $"{DeviceArrays}.Add({shape.Identifier}, {device});");

        if (!copiesIn)
        {
            writer.AppendLine($"// Skip HtoD copy for [Out] array: {shape.Identifier}");
            return;
        }

        var element = ConvertedArrayEmitter.GetElementTypeName(shape);
        writer.AppendLine($"fixed ({element}* {pinned} = {shape.SpanExpression})");
        using (writer.Scope())
        {
            WriteSynchronizedCopy(writer, CopyDirection.HostToDevice, device, $"({IntPtrType}){pinned}", size);
        }
    }

    // [WHY] The host and device layouts of the element differ (GSH0080), so the elements are converted into a shadow
    // array of the device layout, rented from ArrayPool, and the shadow is copied in one transfer.
    private static void WriteConvertedArrayMarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        var lenExpr = shape.LengthExpression;
        var size = KernelHostNames.GetLocal(LauncherLocal.Size, shape);
        var proxy = KernelHostNames.GetLocal(LauncherLocal.Proxy, shape);
        var device = KernelHostNames.GetLocal(LauncherLocal.DevicePointer, shape);

        writer.AppendLine($"// Marshal array element by element: {shape.Identifier}");
        writer.AppendLine($"int {size} = checked({lenExpr} * sizeof({ConvertedArrayEmitter.GetElementShadowPath(shape, shape.Cuda)}));");

        WriteProxyDeclaration(writer, shape);
        WriteArrayReference(writer, shape, proxy);
        writer.AppendLine($"((int*)({proxy} + 1))[0] = {lenExpr};");
        WriteReservedWord(writer, proxy);

        WriteSizeTest(writer, plan, paramIndex);
        using (writer.Scope())
        {
            WriteRegion(writer, plan, paramIndex, rentOnReuse: plan.CopiesBack(paramIndex));
            writer.AppendLine($"{proxy}[0] = {device};");
        }

        writer.AppendLine($"{KernelParams}[{paramIndex}] = {GetArgumentPointer(shape)};");
    }

    private static void WriteConvertedArrayAllocationAndCopyIn(SourceWriter writer, in KernelParameterShape shape,
        bool recorded, bool copiesIn)
    {
        var device = KernelHostNames.GetLocal(LauncherLocal.DevicePointer, shape);
        var size = KernelHostNames.GetLocal(LauncherLocal.Size, shape);
        var shadow = KernelHostNames.GetLocal(LauncherLocal.Shadow, shape);
        var pinned = KernelHostNames.GetLocal(LauncherLocal.Pinned, shape);
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
            WriteSynchronizedCopy(writer, CopyDirection.HostToDevice, device, $"({IntPtrType}){pinned}", size);
        }
    }

    private static void WriteShadowRent(SourceWriter writer, in KernelParameterShape shape) =>
        writer.AppendLine($"{KernelHostNames.GetLocal(LauncherLocal.Shadow, shape)} = {HostTypeNames.ArrayPoolShared}.Rent({KernelHostNames.GetLocal(LauncherLocal.Size, shape)});");

    private static void WriteRefOrOutScalarMarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        var size = KernelHostNames.GetLocal(LauncherLocal.Size, shape);
        var device = KernelHostNames.GetLocal(LauncherLocal.DevicePointer, shape);
        var pinnedTemp = KernelHostNames.GetLocal(LauncherLocal.PinnedTemp, shape);
        writer.AppendLine($"// Marshal ref/out scalar: {shape.Identifier}");
        writer.AppendLine($"int {size} = sizeof({shape.TypeName});");
        using (WriteGroupMemberBranch(writer, plan, paramIndex))
        {
            WriteDeviceAllocation(writer, device, size);

            if (shape.RefKind == RefKind.Ref)
            {
                writer.AppendLine("// Fast zero-allocation copy to device");
                writer.AppendLine($"fixed ({shape.TypeName}* {pinnedTemp} = &{shape.Identifier})");
                using (writer.Scope())
                {
                    var source = $"({IntPtrType}){pinnedTemp}";
                    WriteSynchronizedCopy(writer, CopyDirection.HostToDevice, device, source, size);
                }
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

        // Only the managed-array launcher gets past here: the Ptr launcher passes every kind below through
        switch (shape.Kind)
        {
            case KernelParameterKind.CustomStruct:
                WriteCustomStructUnmarshal(writer, plan, paramIndex);
                break;
            case KernelParameterKind.Array:
                WriteArrayUnmarshal(writer, plan, paramIndex);
                break;
            case KernelParameterKind.RefScalar:
                WriteRefOrOutScalarUnmarshal(writer, plan, paramIndex);
                break;
        }
    }

    private static void WriteCustomStructUnmarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        if (shape.IsDirectCudaValueStruct)
        {
            return;
        }

        var native = KernelHostNames.GetLocal(LauncherLocal.Native, shape);
        var device = KernelHostNames.GetLocal(LauncherLocal.DevicePointer, shape);
        var size = KernelHostNames.GetLocal(LauncherLocal.Size, shape);
        using (WriteNotGroupMemberScope(writer, plan, paramIndex))
        {
            if (shape.IsByRef)
            {
                writer.AppendLine("// Fast zero-allocation copy from device");
                WriteSynchronizedCopy(writer, CopyDirection.DeviceToHost, $"({IntPtrType})(&{native})", device, size);
            }

            writer.Append(shape.UnmarshalMethodName);
            using (writer.Parentheses())
            {
                writer.Append($"ref {shape.Identifier}, ref {native}, hStream");
            }

            writer.AppendLine(";");
        }
    }

    // if (!(__gsharp_aliases?.IsMember(<k>) == true)) { <copy back> }
    // (an empty scope when the parameter is no member)
    // [WHY] The group copied the member back with the other members (AliasGroupEmitter.WriteCudaCopyBack)
    private static SourceWriterScope WriteNotGroupMemberScope(SourceWriter writer, in ArraySharingPlan plan,
        int paramIndex)
    {
        if (AliasGroupEmitter.MemberTest(plan.Aliases, paramIndex) is not { } member)
        {
            return default;
        }

        writer.AppendLine($"if (!({member}))");
        return writer.Scope();
    }

    private static void WriteArrayUnmarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
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

        var size = KernelHostNames.GetLocal(LauncherLocal.Size, shape);
        var device = KernelHostNames.GetLocal(LauncherLocal.DevicePointer, shape);
        var pinned = KernelHostNames.GetLocal(LauncherLocal.Pinned, shape);
        writer.AppendLine($"// Copy back array: {shape.Identifier}");
        writer.Append("if ");
        using (writer.Parentheses())
        {
            writer.Append($"{size} > 0 && {device} != {IntPtrType}.Zero{NotGroupMember(plan, paramIndex)}");
            WriteCopyBackClaim(writer, plan, paramIndex);
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            var element = ConvertedArrayEmitter.GetElementTypeName(shape);
            writer.AppendLine($"fixed ({element}* {pinned} = {shape.SpanExpression})");
            using (writer.Scope())
            {
                WriteSynchronizedCopy(writer, CopyDirection.DeviceToHost, $"({IntPtrType}){pinned}", device, size);
            }
        }
    }

    private static void WriteConvertedArrayUnmarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        var size = KernelHostNames.GetLocal(LauncherLocal.Size, shape);
        var device = KernelHostNames.GetLocal(LauncherLocal.DevicePointer, shape);
        var shadow = KernelHostNames.GetLocal(LauncherLocal.Shadow, shape);
        var pinned = KernelHostNames.GetLocal(LauncherLocal.Pinned, shape);
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
                WriteSynchronizedCopy(writer, CopyDirection.DeviceToHost, $"({IntPtrType}){pinned}", device, size);
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

    private static void WriteRefOrOutScalarUnmarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        using var notMember = WriteNotGroupMemberScope(writer, plan, paramIndex);
        if (shape.RefKind == RefKind.Out)
        {
            writer.AppendLine($"{shape.Identifier} = default;");
        }

        var scalarPtr = KernelHostNames.GetLocal(LauncherLocal.ScalarPointer, shape);
        var device = KernelHostNames.GetLocal(LauncherLocal.DevicePointer, shape);
        var size = KernelHostNames.GetLocal(LauncherLocal.Size, shape);
        writer.AppendLine($"// Fast zero-allocation copy from device for scalar: {shape.Identifier}");
        writer.AppendLine($"fixed ({shape.TypeName}* {scalarPtr} = &{shape.Identifier})");
        using (writer.Scope())
        {
            WriteSynchronizedCopy(writer, CopyDirection.DeviceToHost, $"({IntPtrType}){scalarPtr}", device, size);
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
            var shadow = KernelHostNames.GetLocal(LauncherLocal.Shadow, shape);
            writer.AppendLine(
                $"if ({shadow} != null) {HostTypeNames.ArrayPoolShared}.Return({shadow});");
        }

        var freesOwnRegion = shape.IsByRef || (shape.IsArray && !(plan.UsesTable && plan.IsShared(paramIndex)));
        if (!freesOwnRegion)
        {
            return;
        }

        var device = KernelHostNames.GetLocal(LauncherLocal.DevicePointer, shape);
        writer.Append("if ");
        using (writer.Parentheses())
        {
            writer.Append($"{device} != {IntPtrType}.Zero{NotGroupMember(plan, paramIndex)}");
            for (var k = 0; k < plan.Parameters.Length; k++)
            {
                if (plan.IsEarlierHolder(paramIndex, k))
                {
                    writer.Append($" && {device} != {KernelHostNames.GetLocal(LauncherLocal.DevicePointer, plan.Parameters[k])}");
                }
            }
        }

        writer.AppendLine($" {CudaDriverApiType}.cuMemFreeAsync({device}, hStream);");
    }
}
