namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits the auto result array of a value-returning kernel, one element per thread of the grid, for the CUDA launcher
/// (<see cref="KernelLaunchEmitter"/>) and the CPU launcher (<see cref="CpuHostLaunchEmitter"/>): the host array, the
/// device buffer or native shadow, the copy back with the conversion of a returned struct whose layouts differ, the
/// release and the <c>return</c>. Each launcher calls it at its own step.
/// </summary>
/// <remarks>
/// A returned struct whose host and device layouts differ (<see cref="KernelHostModel.IsCudaConvertedReturn"/>,
/// <see cref="KernelHostModel.IsCpuConvertedReturn"/>) is written by the kernel in the device layout, into a shadow
/// array of that layout, and converted element by element into the host array, as an <c>[Out]</c> array of such structs
/// is (#704).
/// </remarks>
internal static class AutoResultEmitter
{
    private const string Buffer = GSharpInternalNames.ReturnValue.Buffer;
    private const string Size = GSharpInternalNames.ReturnValue.BufferSize;
    private const string Shadow = GSharpInternalNames.ReturnValue.BufferShadow;
    private const string Native = GSharpInternalNames.ReturnValue.BufferNative;
    private const string TotalThreads = "__gsharp_total_threads";

    /// <summary>The device copy of the auto result buffer in the generated CUDA launcher.</summary>
    private const string DeviceAutoResult = "d_" + Buffer;

    /// <summary>The pinned auto result (or its shadow) that the CPU launcher passes to the native kernel.</summary>
    internal const string PinnedAutoResult = "p_" + Buffer;

    // if (<a launch dimension is 0>) throw ...; uint __gsharp_total_threads; checked { ... }
    // var __gsharp_auto_result = new <result>[__gsharp_total_threads];
    /// <summary>Emits the host array of the auto result, one element per thread of the launch.</summary>
    internal static void WriteAllocation(SourceWriter writer, in KernelHostModel kernel)
    {
        if (kernel.ReturnTypeName is not { } returnType)
        {
            return;
        }

        writer.AppendLine(
            "if (gridDimX == 0 || gridDimY == 0 || gridDimZ == 0 || blockDimX == 0 || blockDimY == 0 || blockDimZ == 0)" +
            " throw new global::System.ArgumentOutOfRangeException(\"Dimensions\", \"Launch dimensions must be greater than 0.\");");
        writer.AppendLine(
            $"uint {TotalThreads}; checked {{ {TotalThreads} = (gridDimX * gridDimY * gridDimZ) * (blockDimX * blockDimY * blockDimZ); }}");
        writer.AppendLine($"var {Buffer} = new {returnType}[{TotalThreads}];");
    }

    /// <summary>Emits <c>return __gsharp_auto_result;</c> for a value-returning kernel.</summary>
    internal static void WriteReturn(SourceWriter writer, in KernelHostModel kernel) =>
        writer.AppendLineIf(kernel.ReturnsValue, $"return {Buffer};");

    /// <summary>CUDA: emits <c>IntPtr d___gsharp_auto_result = IntPtr.Zero;</c> for a value-returning kernel.</summary>
    internal static void WriteCudaDeclaration(SourceWriter writer, in KernelHostModel kernel) =>
        writer.AppendLineIf(kernel.ReturnsValue, $"IntPtr {DeviceAutoResult} = IntPtr.Zero;");

    // int __gsharp_auto_result_size = checked(__gsharp_auto_result.Length * sizeof(<result or shadow>));
    // if (<size> > 0) { cuMemAllocAsync(out d___gsharp_auto_result, <size>); kernelParams[<slot>] = &d_...; }
    // [WHY] The device writes the returned elements in its own layout, which a converted struct does not share; the size
    // is checked, as in the CPU launcher, since a large grid of a large struct would wrap
    /// <summary>CUDA: emits the device buffer of the auto result and its kernel parameter slot.</summary>
    internal static void WriteCudaMarshal(SourceWriter writer, in KernelHostModel kernel, int slot)
    {
        if (kernel.ReturnTypeName is not { } returnType)
        {
            return;
        }

        var elementType = kernel.IsCudaConvertedReturn ? GetShadowPath(kernel.CudaReturn) : returnType;
        writer.AppendLine("// Marshal auto result array");
        writer.AppendLine($"int {Size} = checked({Buffer}.Length * sizeof({elementType}));");

        writer.AppendLine($"if ({Size} > 0)");
        using (writer.Scope())
        {
            writer.AppendLine(
                "CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(" +
                $"out {DeviceAutoResult}, (IntPtr){Size}, hStream));");
            writer.AppendLine($"kernelParams[{slot}] = &{DeviceAutoResult};");
        }
    }

    /// <summary>
    /// CUDA: emits the copy back of the auto result, in one pinned transfer, or through a shadow array for a converted
    /// struct.
    /// </summary>
    internal static void WriteCudaCopyBack(SourceWriter writer, in KernelHostModel kernel)
    {
        if (!kernel.ReturnsValue)
        {
            return;
        }

        if (kernel.IsCudaConvertedReturn)
        {
            WriteCudaConvertedCopyBack(writer, kernel.CudaReturn);
            return;
        }

        // if (__gsharp_auto_result_size > 0 && d___gsharp_auto_result != IntPtr.Zero) { <pinned copy back> }
        // The size is the one checked at the allocation (WriteCudaMarshal)
        writer.AppendLine($"if ({Size} > 0 && {DeviceAutoResult} != IntPtr.Zero)");
        using (writer.Scope())
        {
            StructMarshalingEmitter.WritePinnedCopy(
                writer,
                Buffer,
                DeviceAutoResult,
                toDevice: false,
                synchronize: true,
                size: Size
            );
        }
    }

    // if (__gsharp_auto_result_size > 0 && d___gsharp_auto_result != IntPtr.Zero) {
    //   var __gsharp_auto_result_shadow = ArrayPool<byte>.Shared.Rent(__gsharp_auto_result_size);
    //   try { fixed (byte* p = shadow) { <copy to host>; <synchronize> } <shadow span>; <Unmarshal_ loop> }
    //   finally { ArrayPool<byte>.Shared.Return(__gsharp_auto_result_shadow); }
    // }
    // [WHY] The device wrote the returned structs in its layout: they are read into a shadow array of that layout and
    // converted element by element, as an [Out] array of such structs is (#704)
    private static void WriteCudaConvertedCopyBack(SourceWriter writer, in ParameterTransfer transfer)
    {
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

                WriteShadowSpan(writer, transfer);
                WriteUnmarshalLoop(writer, transfer);
            }

            writer.AppendLine("finally");
            using (writer.Scope())
            {
                writer.AppendLine($"System.Buffers.ArrayPool<byte>.Shared.Return({Shadow});");
            }
        }
    }

    /// <summary>CUDA: emits the asynchronous free of the device buffer of the auto result.</summary>
    internal static void WriteCudaFree(SourceWriter writer, in KernelHostModel kernel)
    {
        if (!kernel.ReturnsValue)
        {
            return;
        }

        writer.AppendLine($"if ({DeviceAutoResult} != IntPtr.Zero)");
        using (writer.Scope())
        {
            writer.AppendLine($"CudaException.ThrowIfError(CudaDriverApi.cuMemFreeAsync({DeviceAutoResult}, hStream));");
        }
    }

    // , <result or shadow>* __gsharp_auto_result
    // A returned struct whose layouts differ is written into its native shadow array (#704)
    /// <summary>CPU: emits the auto result parameter of the native launcher's delegate.</summary>
    internal static void WriteCpuDelegateParameter(SourceWriter writer, in KernelHostModel kernel)
    {
        if (kernel.ReturnTypeName is not { } returnType)
        {
            return;
        }

        var elementType = kernel.IsCpuConvertedReturn ? GetShadowPath(kernel.CpuReturn) : returnType;
        writer.Append($", {elementType}* {Buffer}");
    }

    // byte[]? __gsharp_auto_result_shadow = null;
    // int __gsharp_auto_result_size = checked(__gsharp_auto_result.Length * sizeof(<shadow>));
    /// <summary>
    /// CPU: emits the declarations of the shadow of a converted returned struct, before the <c>try</c> block; returns
    /// whether the kernel returns one.
    /// </summary>
    internal static bool WriteCpuConvertedDeclaration(SourceWriter writer, in KernelHostModel kernel)
    {
        if (!kernel.IsCpuConvertedReturn)
        {
            return false;
        }

        writer.AppendLine($"byte[]? {Shadow} = null;");
        writer.AppendLine($"int {Size} = checked({Buffer}.Length * sizeof({GetShadowPath(kernel.CpuReturn)}));");
        return true;
    }

    // __gsharp_auto_result_shadow = ArrayPool<byte>.Shared.Rent(__gsharp_auto_result_size); <shadow span>;
    // __gsharp_auto_result_native.Clear();
    // [WHY] The native kernel writes the returned structs in the device layout, into a rented shadow array that is
    // converted back element by element after the call (#704). A rented array holds the bytes of an earlier use.
    /// <summary>CPU: emits the rented shadow of a converted returned struct, inside the <c>try</c> block.</summary>
    internal static void WriteCpuConvertedMarshal(SourceWriter writer, in KernelHostModel kernel)
    {
        if (!kernel.IsCpuConvertedReturn)
        {
            return;
        }

        writer.AppendLine($"{Shadow} = System.Buffers.ArrayPool<byte>.Shared.Rent({Size});");
        WriteShadowSpan(writer, kernel.CpuReturn);
        writer.AppendLine($"{Native}.Clear();");
    }

    // fixed (<result>* p___gsharp_auto_result = __gsharp_auto_result), or of the shadow span for a converted struct
    /// <summary>CPU: emits the <c>fixed</c> statement that pins the auto result; returns whether it wrote one.</summary>
    internal static bool WriteCpuFixed(SourceWriter writer, in KernelHostModel kernel)
    {
        if (kernel.IsCpuConvertedReturn)
        {
            writer.AppendLine($"fixed ({GetShadowPath(kernel.CpuReturn)}* {PinnedAutoResult} = {Native})");
            return true;
        }

        if (kernel.ReturnTypeName is not { } returnType)
        {
            return false;
        }

        writer.AppendLine($"fixed ({returnType}* {PinnedAutoResult} = {Buffer})");
        return true;
    }

    /// <summary>CPU: emits the conversion of a returned struct from its shadow, after the native call.</summary>
    internal static void WriteCpuConvertedUnmarshal(SourceWriter writer, in KernelHostModel kernel)
    {
        if (kernel.IsCpuConvertedReturn)
        {
            WriteUnmarshalLoop(writer, kernel.CpuReturn);
        }
    }

    /// <summary>CPU: emits the return of the rented shadow of a converted returned struct, in the <c>finally</c> block.</summary>
    internal static void WriteCpuConvertedRelease(SourceWriter writer, in KernelHostModel kernel) =>
        writer.AppendLineIf(kernel.IsCpuConvertedReturn,
            $"if ({Shadow} != null) System.Buffers.ArrayPool<byte>.Shared.Return({Shadow});");

    // var __gsharp_auto_result_native = MemoryMarshal.Cast<byte, <element shadow>>(
    //     __gsharp_auto_result_shadow.AsSpan(0, __gsharp_auto_result_size));
    // The auto result array of a kernel that returns a converted struct, in the device layout (#704)
    private static void WriteShadowSpan(SourceWriter writer, in ParameterTransfer transfer) =>
        writer.AppendLine(
            $"var {Native} = System.Runtime.InteropServices.MemoryMarshal" +
            $".Cast<byte, {GetShadowPath(transfer)}>({Shadow}.AsSpan(0, {Size}));");

    // for (int __gsharp_auto_result_index = 0; ...; __gsharp_auto_result_index++)
    // { Unmarshal_<E>(ref __gsharp_auto_result[i], ref __gsharp_auto_result_native[i], false, hStream); }
    private static void WriteUnmarshalLoop(SourceWriter writer, in ParameterTransfer transfer)
    {
        const string Index = GSharpInternalNames.ReturnValue.BufferIndex;
        writer.AppendLine($"for (int {Index} = 0; {Index} < {Native}.Length; {Index}++)");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"{transfer.ConvertedElementUnmarshalMethodName}(ref {Buffer}[{Index}], ref {Native}[{Index}], false, hStream);");
        }
    }

    // The AotContexts path of the converted struct that a kernel returns
    private static string GetShadowPath(in ParameterTransfer transfer) =>
        transfer.ConvertedElementAotStructPath ??
        throw new InvalidOperationException("The kernel does not return a converted struct.");
}
