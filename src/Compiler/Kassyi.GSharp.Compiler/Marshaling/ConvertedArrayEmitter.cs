namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits the element-by-element conversion of an array-like parameter whose element lays out differently on the device,
/// and of the auto result array of a kernel that returns such a struct, for the CUDA launcher (<see cref="KernelLaunchEmitter"/>) and the CPU launcher (<see cref="CpuHostLaunchEmitter"/>).
/// </summary>
/// <remarks>
/// Both launchers convert the elements into a shadow array of the device layout, rented from <c>ArrayPool</c>. They differ
/// only in the <see cref="ParameterTransfer"/> (<see cref="KernelParameterShape.Cuda"/> or
/// <see cref="KernelParameterShape.Cpu"/>) and in what they do with the shadow: the CUDA launcher copies it to the
/// device, the CPU launcher passes it to the native kernel.
/// </remarks>
internal static class ConvertedArrayEmitter
{
    // for (int <name>_index = 0; <name>_index < <name>_native.Length; <name>_index++)
    private static void WriteElementLoopHeader(SourceWriter writer, string name) =>
        writer.AppendLine($"for (int {name}_index = 0; {name}_index < {name}_native.Length; {name}_index++)");

    // var <name>_native = MemoryMarshal.Cast<byte, <element shadow>>(<name>_shadow.AsSpan(0, <name>_size));
    // Span<<element>> <name>_host = <argument span>;                     (ReadOnlySpan for a read-only container)
    // <name>_native spans the rented bytes in the device layout; <name>_host spans the argument
    internal static void WriteShadowAndHostSpans(SourceWriter writer, in KernelParameterShape shape,
        in ParameterTransfer transfer)
    {
        writer.AppendLine(
            $"var {shape.Name}_native = System.Runtime.InteropServices.MemoryMarshal" +
            $".Cast<byte, {GetElementShadowPath(shape, transfer)}>({shape.Name}_shadow.AsSpan(0, {shape.Name}_size));");
        var span = shape.IsReadOnlyContainer ? "System.ReadOnlySpan" : "System.Span";
        var element = shape.ElementTypeName ??
            throw new InvalidOperationException($"Parameter '{shape.Identifier}' is not an array-like type.");
        writer.AppendLine($"{span}<{element}> {shape.Name}_host = {shape.SpanExpression};");
    }

    // for (...) { Marshal_<E>(ref <name>_host[<name>_index], ref <name>_native[<name>_index], false, <name>_allocatedPtrs,
    // hStream); }, through the spans of WriteShadowAndHostSpans
    internal static void WriteElementMarshalLoop(SourceWriter writer, in KernelParameterShape shape,
        in ParameterTransfer transfer)
    {
        var name = shape.Name;
        WriteElementLoopHeader(writer, name);
        using (writer.Scope())
        {
            var hostElement = $"{name}_host[{name}_index]";
            if (shape.IsReadOnlyContainer)
            {
                // A read-only container gives no ref to its element; Marshal_ only reads its copy
                writer.AppendLine($"var {name}_element = {name}_host[{name}_index];");
                hostElement = $"{name}_element";
            }

            writer.AppendLine(
                $"{transfer.ConvertedElementMarshalMethodName}(ref {hostElement}, " +
                $"ref {name}_native[{name}_index], false, {name}_allocatedPtrs, hStream);");
        }
    }

    // for (...) { Unmarshal_<E>(ref <name>_host[<name>_index], ref <name>_native[<name>_index], false, hStream); },
    // through the spans of WriteShadowAndHostSpans
    internal static void WriteElementUnmarshalLoop(SourceWriter writer, in KernelParameterShape shape,
        in ParameterTransfer transfer)
    {
        var name = shape.Name;
        WriteElementLoopHeader(writer, name);
        using (writer.Scope())
        {
            writer.AppendLine(
                $"{transfer.ConvertedElementUnmarshalMethodName}(ref {name}_host[{name}_index], " +
                $"ref {name}_native[{name}_index], false, hStream);");
        }
    }

    // var __gsharp_auto_result_native = MemoryMarshal.Cast<byte, <element shadow>>(
    //     __gsharp_auto_result_shadow.AsSpan(0, __gsharp_auto_result_size));
    // The auto result array of a kernel that returns a converted struct, in the device layout (#704)
    internal static void WriteReturnShadowSpan(SourceWriter writer, in ParameterTransfer transfer) =>
        writer.AppendLine(
            $"var {GSharpInternalNames.ReturnValue.BufferNative} = System.Runtime.InteropServices.MemoryMarshal" +
            $".Cast<byte, {GetReturnShadowPath(transfer)}>({GSharpInternalNames.ReturnValue.BufferShadow}" +
            $".AsSpan(0, {GSharpInternalNames.ReturnValue.BufferSize}));");

    // for (int __gsharp_auto_result_index = 0; ...; __gsharp_auto_result_index++)
    // { Unmarshal_<E>(ref __gsharp_auto_result[i], ref __gsharp_auto_result_native[i], false, hStream); }
    internal static void WriteReturnUnmarshalLoop(SourceWriter writer, in ParameterTransfer transfer)
    {
        const string Index = GSharpInternalNames.ReturnValue.BufferIndex;
        const string Native = GSharpInternalNames.ReturnValue.BufferNative;
        writer.AppendLine($"for (int {Index} = 0; {Index} < {Native}.Length; {Index}++)");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"{transfer.ConvertedElementUnmarshalMethodName}(ref {GSharpInternalNames.ReturnValue.Buffer}[{Index}], " +
                $"ref {Native}[{Index}], false, hStream);");
        }
    }

    /// <summary>The <c>AotContexts</c> path of the converted struct that a kernel returns.</summary>
    internal static string GetReturnShadowPath(in ParameterTransfer transfer) =>
        transfer.ConvertedElementAotStructPath ??
        throw new InvalidOperationException("The kernel does not return a converted struct.");

    /// <summary>The <c>AotContexts</c> path of the converted element of <paramref name="shape"/>.</summary>
    internal static string GetElementShadowPath(in KernelParameterShape shape, in ParameterTransfer transfer) =>
        transfer.ConvertedElementAotStructPath ??
        throw new InvalidOperationException($"Parameter '{shape.Identifier}' is not a converted array.");
}
