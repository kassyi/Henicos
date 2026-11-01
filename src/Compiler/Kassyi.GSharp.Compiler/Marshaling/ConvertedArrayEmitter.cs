namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits the element-by-element conversion of an array-like parameter whose element lays out differently on the device,
/// for the CUDA launcher (<see cref="KernelLaunchEmitter"/>) and the CPU launcher (<see cref="CpuHostLaunchEmitter"/>).
/// </summary>
/// <remarks>
/// Both launchers convert the elements into a shadow array of the device layout, rented from <c>ArrayPool</c>. They differ
/// only in the <see cref="ParameterTransfer"/> (<see cref="KernelParameterShape.Cuda"/> or
/// <see cref="KernelParameterShape.Cpu"/>) and in what they do with the shadow: the CUDA launcher copies it to the
/// device, the CPU launcher passes it to the native kernel.
/// </remarks>
internal static class ConvertedArrayEmitter
{
    // for (int __gsharp_index_<ord> = 0; __gsharp_index_<ord> < __gsharp_native_<ord>.Length; __gsharp_index_<ord>++)
    private static void WriteElementLoopHeader(SourceWriter writer, in KernelParameterShape shape)
    {
        var index = KernelHostNames.GetElementIndexLocal(shape);
        var native = KernelHostNames.GetNativeLocal(shape);
        writer.AppendLine($"for (int {index} = 0; {index} < {native}.Length; {index}++)");
    }

    // var __gsharp_native_<ord> = MemoryMarshal.Cast<byte, <element shadow>>(__gsharp_shadow_<ord>.AsSpan(0, __gsharp_size_<ord>));
    // Span<<element>> __gsharp_host_<ord> = <argument span>;                     (ReadOnlySpan for a read-only container)
    // __gsharp_native_<ord> spans the rented bytes in the device layout; __gsharp_host_<ord> spans the argument
    internal static void WriteShadowAndHostSpans(SourceWriter writer, in KernelParameterShape shape,
        in ParameterTransfer transfer)
    {
        var native = KernelHostNames.GetNativeLocal(shape);
        var shadow = KernelHostNames.GetShadowLocal(shape);
        var size = KernelHostNames.GetSizeLocal(shape);
        var host = KernelHostNames.GetHostSpanLocal(shape);
        writer.AppendLine(
            $"var {native} = global::System.Runtime.InteropServices.MemoryMarshal" +
            $".Cast<byte, {GetElementShadowPath(shape, transfer)}>({shadow}.AsSpan(0, {size}));");
        var span = shape.IsReadOnlyContainer ? "global::System.ReadOnlySpan" : "global::System.Span";
        var element = shape.ElementTypeName ??
            throw new InvalidOperationException($"Parameter '{shape.Identifier}' is not an array-like type.");
        writer.AppendLine($"{span}<{element}> {host} = {shape.SpanExpression};");
    }

    // for (...) { Marshal_<E>(ref __gsharp_host_<ord>[__gsharp_index_<ord>], ref __gsharp_native_<ord>[__gsharp_index_<ord>], false, <allocations>,
    // hStream); }, through the spans of WriteShadowAndHostSpans. <allocations> records what Marshal_ allocates: the CUDA
    // launcher's DeviceArrayRegions, or the CPU launcher's list of GCHandles of the parameter.
    internal static void WriteElementMarshalLoop(SourceWriter writer, in KernelParameterShape shape,
        in ParameterTransfer transfer, string allocations)
    {
        var index = KernelHostNames.GetElementIndexLocal(shape);
        var host = KernelHostNames.GetHostSpanLocal(shape);
        var native = KernelHostNames.GetNativeLocal(shape);
        WriteElementLoopHeader(writer, shape);
        using (writer.Scope())
        {
            var hostElement = $"{host}[{index}]";
            if (shape.IsReadOnlyContainer)
            {
                // A read-only container gives no ref to its element; Marshal_ only reads its copy
                var elementLocal = KernelHostNames.GetElementLocal(shape);
                writer.AppendLine($"var {elementLocal} = {host}[{index}];");
                hostElement = elementLocal;
            }

            writer.AppendLine(
                $"{transfer.ConvertedElementMarshalMethodName}(ref {hostElement}, " +
                $"ref {native}[{index}], false, {allocations}, hStream);");
        }
    }

    // for (...) { Unmarshal_<E>(ref __gsharp_host_<ord>[__gsharp_index_<ord>], ref __gsharp_native_<ord>[__gsharp_index_<ord>], false, hStream); },
    // through the spans of WriteShadowAndHostSpans
    internal static void WriteElementUnmarshalLoop(SourceWriter writer, in KernelParameterShape shape,
        in ParameterTransfer transfer)
    {
        var index = KernelHostNames.GetElementIndexLocal(shape);
        var host = KernelHostNames.GetHostSpanLocal(shape);
        var native = KernelHostNames.GetNativeLocal(shape);
        WriteElementLoopHeader(writer, shape);
        using (writer.Scope())
        {
            writer.AppendLine(
                $"{transfer.ConvertedElementUnmarshalMethodName}(ref {host}[{index}], " +
                $"ref {native}[{index}], false, hStream);");
        }
    }

    /// <summary>The <c>AotContexts</c> path of the converted element of <paramref name="shape"/>.</summary>
    internal static string GetElementShadowPath(in KernelParameterShape shape, in ParameterTransfer transfer) =>
        transfer.ConvertedElementAotStructPath ??
        throw new InvalidOperationException($"Parameter '{shape.Identifier}' is not a converted array.");
}
