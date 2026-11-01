namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits the CPU host launch members of a kernel wrapper class: delegate, the copies of <c>[In]</c> arrays
/// (<see cref="CpuArrayCopyEmitter"/>), managed fallback (<see cref="ManagedFallbackEmitter"/>) and native call.
/// </summary>
internal static class CpuHostLaunchEmitter
{
    private const string ReferenceEqualsMethod = ArraySharingPlan.ReferenceEqualsMethod;
    private const string ArchSuffix = GSharpInternalNames.Launcher.ArchSuffix;
    private const string DllName = GSharpInternalNames.Launcher.DllName;
    private const string DllPath = GSharpInternalNames.Launcher.DllPath;
    private const string FunctionHandle = GSharpInternalNames.Launcher.FunctionHandle;
    private const string PinnedHandle = GSharpInternalNames.Launcher.PinnedHandle;

    /// <summary>Emits the managed source, the delegate declaration and the launch wrapper of one kernel.</summary>
    public static void WriteKernelMembers(SourceWriter writer, in KernelEmission kernel, string assemblyName)
    {
        writer.AppendLine(kernel.ManagedCSharpSource);
        WrapperEmitter.WriteLayoutCheckedField(writer, kernel.Host.CpuLayoutGuards, kernel.MemberKey);
        WriteCpuDelegateDeclaration(writer, kernel);
        WriteLaunchMethodCpu(writer, kernel, assemblyName);
    }

    private static void WriteLaunchMethodCpu(SourceWriter writer, in KernelEmission kernel, string assemblyName)
    {
        writer.AppendLine("#pragma warning disable CS8500");
        WrapperEmitter.WriteLaunchMethodSignature(writer, kernel, false);
        using (writer.Scope())
        {
            WrapperEmitter.WriteOutParameterDefaults(writer, kernel.Host);
            WrapperEmitter.WriteArrayArgumentChecks(writer, kernel.Host);
            AutoResultEmitter.WriteAllocation(writer, kernel.Host);
            var sharing = ArraySharingPlan.ForCpu(kernel.Host);
            CpuArrayCopyEmitter.WriteKernelCopies(writer, sharing);
            WriteDllPath(writer, assemblyName);
            ManagedFallbackEmitter.WriteManagedExecutionFallback(writer, kernel.Host);

            // [WHY] Only the native launcher reads the host bytes as the C++ layout; the managed fallback uses the values
            WrapperEmitter.WriteLayoutGuards(writer, kernel.Host.CpuLayoutGuards, kernel.MemberKey);
            WriteCpuDelegateGetOrCompile(writer, kernel);
            WriteCpuFixedBlocksAndInvoke(writer, kernel.Host, sharing);
        }

        writer.AppendLine("#pragma warning restore CS8500");
    }

    private static void WriteCpuDelegateDeclaration(SourceWriter writer, in KernelEmission kernel)
    {
        // Generate Delegate
        writer.AppendLine(
            "[global::System.Runtime.InteropServices.UnmanagedFunctionPointer(global::System.Runtime.InteropServices.CallingConvention.Cdecl, CharSet = global::System.Runtime.InteropServices.CharSet.Unicode)]");
        writer.Append("private unsafe delegate void ");
        writer.Append(KernelHostNames.GetDelegateTypeName(kernel));
        using (writer.Parentheses())
        {
            writer.Append(
                "uint gridDimX, uint gridDimY, uint gridDimZ, uint blockDimX, uint blockDimY, uint blockDimZ, uint sharedMemBytes");
            foreach (var p in kernel.Host.Parameters)
            {
                writer.Append(", ");

                if (p.IsArray)
                {
                    writer.Append($"global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy {p.Identifier}");
                }
                else
                {
                    // [WHY] A user struct by value goes as a pointer to the host value, so that the runtime marshaller
                    // never converts it (bool, char, the x64 size rules); the native launcher copies it (#511). A struct
                    // whose layouts differ goes as a pointer to its native shadow struct.
                    var typeName = p.IsCpuConvertedStruct && p.AotStructPath is { } shadow ? shadow : p.TypeName;
                    writer.Append($"{typeName}{(p.IsByRef || p.IsCpuValueStruct ? "*" : "")} {p.Identifier}");
                }
            }

            AutoResultEmitter.WriteCpuDelegateParameter(writer, kernel.Host);
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    // var __gsharp_dll_path = <the native library of the assembly for this architecture>;
    private static void WriteDllPath(SourceWriter writer, string assemblyName)
    {
        var safeAssemblyName = AotHostNames.ToSafeIdentifier(assemblyName);
        writer.AppendLine(
            $"var {ArchSuffix} = global::System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==" +
            $" global::System.Runtime.InteropServices.Architecture.Arm64 ? \"_arm64\" : \"_x64\";");
        writer.AppendLine(
            $"var {DllName} = $\"Kassyi.GSharp.Kernels_{safeAssemblyName}{{{ArchSuffix}}}.dll\";");
        writer.AppendLine(
            $"var {DllPath} = global::System.IO.Path.Combine(global::System.AppContext.BaseDirectory, {DllName});");
    }

    private static void WriteCpuDelegateGetOrCompile(SourceWriter writer, in KernelEmission kernel)
    {
        var exportName = kernel.Host.LaunchExportName;

        writer.AppendLine(
            $"var {FunctionHandle} = global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.LoadAotKernel<{KernelHostNames.GetDelegateTypeName(kernel)}>(\"{exportName}\", {DllPath});");
    }

    // <declarations>; try { <marshaling>; <fixed blocks and call>; <unmarshaling> } finally { <release> }
    // [WHY] Marshal_ pins the array fields of a converted struct or element and records each handle; this one finally
    // block frees them and returns the rented shadows after the call and the copy back, also when the marshaling, the
    // native call or its error check throws.
    private static void WriteCpuFixedBlocksAndInvoke(SourceWriter writer, in KernelHostModel kernel,
        in ArraySharingPlan sharing)
    {
        var parameters = kernel.Parameters;
        var hasConverted = false;
        for (var i = 0; i < parameters.Length; i++)
        {
            hasConverted |= WriteConvertedDeclaration(writer, sharing, i);
        }

        hasConverted |= AutoResultEmitter.WriteCpuConvertedDeclaration(writer, kernel);

        if (!hasConverted)
        {
            WriteFixedBlocksAndInvoke(writer, kernel);
            AutoResultEmitter.WriteReturn(writer, kernel);
            return;
        }

        writer.AppendLine("try");
        using (writer.Scope())
        {
            // [WHY] The same order as the CUDA launcher: an [Out] array that shares a shadow comes after the holders
            // that convert their elements into it (#721)
            for (var i = 0; i < parameters.Length; i++)
            {
                if (!sharing.IsMarshaledLast(i))
                {
                    WriteConvertedMarshal(writer, sharing, i);
                }
            }

            for (var i = 0; i < parameters.Length; i++)
            {
                if (sharing.IsMarshaledLast(i))
                {
                    WriteConvertedMarshal(writer, sharing, i);
                }
            }

            AutoResultEmitter.WriteCpuConvertedMarshal(writer, kernel);
            WriteFixedBlocksAndInvoke(writer, kernel);
            foreach (var shape in kernel.Parameters)
            {
                WriteConvertedUnmarshal(writer, shape);
            }

            AutoResultEmitter.WriteCpuConvertedUnmarshal(writer, kernel);
        }

        writer.AppendLine("finally");
        using (writer.Scope())
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                WriteConvertedRelease(writer, sharing, i);
            }

            AutoResultEmitter.WriteCpuConvertedRelease(writer, kernel);
        }

        AutoResultEmitter.WriteReturn(writer, kernel);
    }

    // <shadow> __gsharp_native_<ord> = default; var __gsharp_allocated_ptrs_<ord> = new List<IntPtr>();                  (converted struct)
    // byte[]? __gsharp_shadow_<ord> = null; var __gsharp_allocated_ptrs_<ord> = ...; int __gsharp_size_<ord> = checked(...); (converted array)
    // byte[]? __gsharp_shared_shadow_<ord> = null;                         (a converted T[] that an earlier holder may share)
    // Returns whether the parameter is converted. The size is computed before the try block, when nothing is allocated.
    private static bool WriteConvertedDeclaration(SourceWriter writer, in ArraySharingPlan sharing, int index)
    {
        var shape = sharing.Parameters[index];
        writer.AppendLineIf(sharing.HasShadowSource(index), $"byte[]? {KernelHostNames.GetSharedShadowLocal(shape)} = null;");
        switch (shape)
        {
            case { IsCpuConvertedStruct: true, AotStructPath: { } shadow }:
                writer.AppendLine($"{shadow} {KernelHostNames.GetNativeLocal(shape)} = default;");
                writer.AppendLine($"var {KernelHostNames.GetAllocatedPointersLocal(shape)} = new global::System.Collections.Generic.List<global::System.IntPtr>();");
                return true;
            case { IsCpuConvertedArray: true, Cpu.ConvertedElementAotStructPath: { } element }:
                writer.AppendLine($"byte[]? {KernelHostNames.GetShadowLocal(shape)} = null;");
                writer.AppendLine($"var {KernelHostNames.GetAllocatedPointersLocal(shape)} = new global::System.Collections.Generic.List<global::System.IntPtr>();");
                writer.AppendLine($"int {KernelHostNames.GetSizeLocal(shape)} = checked({shape.LengthExpression} * sizeof({element}));");
                return true;
            default:
                return false;
        }
    }

    // Marshal_<S>(ref <name>, ref __gsharp_native_<ord>, false, __gsharp_allocated_ptrs_<ord>, hStream);                (converted struct)
    // __gsharp_shadow_<ord> = ArrayPool<byte>.Shared.Rent(__gsharp_size_<ord>); <spans>;                                (converted array)
    // for (...) Marshal_<E>(ref __gsharp_host_<ord>[i], ref __gsharp_native_<ord>[i], false, __gsharp_allocated_ptrs_<ord>, hStream); ([In] / in-out)
    // __gsharp_native_<ord>.Clear();                                       ([Out] of an element without array fields)
    // [WHY] The elements of an array differ in layout, so they are converted into a rented shadow array of the device
    // layout, which the kernel reads in place (the CUDA launcher copies the same shadow to the device). An [Out] element
    // with array fields is still marshaled: the kernel writes into its pinned arrays. A rented array holds the bytes of
    // an earlier use, so an [Out] shadow is cleared rather than left to the kernel.
    // A converted T[] that holds the array of a holder marshaled before it takes that shadow and fills nothing
    // (WriteSharedShadowLookup).
    private static void WriteConvertedMarshal(SourceWriter writer, in ArraySharingPlan sharing, int index)
    {
        var shape = sharing.Parameters[index];
        switch (shape)
        {
            case { IsCpuConvertedStruct: true, MarshalMethodName: { } marshal }:
                writer.AppendLine(
                    $"{marshal}(ref {shape.Identifier}, ref {KernelHostNames.GetNativeLocal(shape)}, false, {KernelHostNames.GetAllocatedPointersLocal(shape)}, hStream);");
                break;
            case { IsCpuConvertedArray: true } when sharing.HasShadowSource(index):
                var sharedShadow = KernelHostNames.GetSharedShadowLocal(shape);
                WriteSharedShadowLookup(writer, sharing, index);
                writer.AppendLine(
                    $"{KernelHostNames.GetShadowLocal(shape)} = {sharedShadow} ?? global::System.Buffers.ArrayPool<byte>.Shared.Rent({KernelHostNames.GetSizeLocal(shape)});");
                ConvertedArrayEmitter.WriteShadowAndHostSpans(writer, shape, shape.Cpu);
                writer.AppendLine($"if ({sharedShadow} == null)");
                using (writer.Scope())
                {
                    WriteConvertedElements(writer, sharing, index);
                }

                break;
            case { IsCpuConvertedArray: true }:
                writer.AppendLine($"{KernelHostNames.GetShadowLocal(shape)} = global::System.Buffers.ArrayPool<byte>.Shared.Rent({KernelHostNames.GetSizeLocal(shape)});");
                ConvertedArrayEmitter.WriteShadowAndHostSpans(writer, shape, shape.Cpu);
                WriteConvertedElements(writer, sharing, index);
                break;
        }
    }

    // An [Out] array of elements without array fields is cleared instead of converted (ArraySharingPlan.CopiesIn)
    private static void WriteConvertedElements(SourceWriter writer, in ArraySharingPlan sharing, int index)
    {
        var shape = sharing.Parameters[index];
        if (!sharing.CopiesIn(index))
        {
            writer.AppendLine($"{KernelHostNames.GetNativeLocal(shape)}.Clear();");
        }
        else
        {
            ConvertedArrayEmitter.WriteElementMarshalLoop(writer, shape, shape.Cpu, KernelHostNames.GetAllocatedPointersLocal(shape));
        }
    }

    // __gsharp_shared_shadow_<ord> = ReferenceEquals(<name>, <earlier>) ? __gsharp_shadow_<earlier> : ... : null;
    // [WHY] Converted arguments that hold one array share one shadow, which the native kernel reads and writes in place,
    // as the CUDA launcher gives them one device region (#721). The first holder converts the elements into it; an [Out]
    // holder comes last, so that it never clears what an earlier holder converted. Every holder that copies back reads
    // the same shadow, so the host elements get the same values.
    private static void WriteSharedShadowLookup(SourceWriter writer, in ArraySharingPlan sharing, int index)
    {
        var shape = sharing.Parameters[index];
        writer.Append($"{KernelHostNames.GetSharedShadowLocal(shape)} = ");
        for (var k = 0; k < sharing.Parameters.Length; k++)
        {
            if (sharing.IsShadowSource(index, k))
            {
                var source = sharing.Parameters[k];
                writer.Append(
                    $"{ReferenceEqualsMethod}({shape.Identifier}, {source.Identifier}) ? {KernelHostNames.GetShadowLocal(source)} : ");
            }
        }

        writer.AppendLine("null;");
    }

    // Unmarshal_<S>(ref <name>, ref __gsharp_native_<ord>, false, hStream);                                     (converted struct)
    // for (...) Unmarshal_<E>(ref __gsharp_host_<ord>[i], ref __gsharp_native_<ord>[i], false, hStream);  (converted array, copied back)
    // The array goes through the spans that the marshaling declared in the same try block.
    // [WHY] A struct by value is unmarshaled as well: its own fields go back only to Launch's copy, but an array field
    // whose elements are converted shares the caller's array, whose elements must get the kernel's writes
    private static void WriteConvertedUnmarshal(SourceWriter writer, in KernelParameterShape shape)
    {
        switch (shape)
        {
            case { IsCpuConvertedStruct: true, UnmarshalMethodName: { } unmarshal }:
                writer.AppendLine($"{unmarshal}(ref {shape.Identifier}, ref {KernelHostNames.GetNativeLocal(shape)}, false, hStream);");
                break;
            case { IsCpuConvertedArray: true, CopyOut: true, IsReadOnlyContainer: false }:
                ConvertedArrayEmitter.WriteElementUnmarshalLoop(writer, shape, shape.Cpu);
                break;
        }
    }

    // foreach (var __gsharp_handle in __gsharp_allocated_ptrs_<ord>) GCHandle.FromIntPtr(__gsharp_handle).Free();
    // if (__gsharp_shadow_<ord> != null) ArrayPool<byte>.Shared.Return(__gsharp_shadow_<ord>);                        (converted array)
    // A shared shadow goes back to the pool from its owner only: ... && __gsharp_shared_shadow_<ord> == null
    private static void WriteConvertedRelease(SourceWriter writer, in ArraySharingPlan sharing, int index)
    {
        var shape = sharing.Parameters[index];
        var notShared = sharing.HasShadowSource(index) ? $" && {KernelHostNames.GetSharedShadowLocal(shape)} == null" : "";
        switch (shape)
        {
            case { IsCpuConvertedStruct: true }:
                WriteHandleRelease(writer, shape);
                break;
            case { IsCpuConvertedArray: true }:
                var shadow = KernelHostNames.GetShadowLocal(shape);
                WriteHandleRelease(writer, shape);
                writer.AppendLine(
                    $"if ({shadow} != null{notShared}) global::System.Buffers.ArrayPool<byte>.Shared.Return({shadow});");
                break;
        }
    }

    private static void WriteHandleRelease(SourceWriter writer, in KernelParameterShape shape) =>
        writer.AppendLine(
            $"foreach (var {PinnedHandle} in {KernelHostNames.GetAllocatedPointersLocal(shape)}) global::System.Runtime.InteropServices.GCHandle.FromIntPtr({PinnedHandle}).Free();");

    private static void WriteFixedBlocksAndInvoke(SourceWriter writer, in KernelHostModel kernel)
    {
        var hasFixed = false;

        foreach (var shape in kernel.Parameters)
        {
            var pinned = KernelHostNames.GetPinnedLocal(shape);
            if (shape.IsCpuConvertedArray)
            {
                writer.AppendLine($"fixed (byte* {pinned} = {KernelHostNames.GetShadowLocal(shape)})");
                hasFixed = true;
            }
            else if (shape is { IsArray: true, ElementTypeName: { } elementType })
            {
                writer.AppendLine($"fixed ({elementType}* {pinned} = {shape.SpanExpression})");
                hasFixed = true;
            }
            else if (shape is { IsByRef: true, IsCpuConvertedStruct: false })
            {
                writer.AppendLine($"fixed ({shape.TypeName}* {pinned} = &{shape.Identifier})");
                hasFixed = true;
            }
        }

        hasFixed |= AutoResultEmitter.WriteCpuFixed(writer, kernel);

        if (hasFixed)
        {
            using (writer.Scope())
            {
                WriteDelegateInvocation(writer, kernel);
            }
        }
        else
        {
            WriteDelegateInvocation(writer, kernel);
        }
    }

    private static void WriteDelegateInvocation(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.Append(FunctionHandle);
        using (writer.Parentheses())
        {
            // The native launcher places the dynamic shared memory declarations in sharedMemBytes (#544)
            writer.Append("gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ, sharedMemBytes");

            foreach (var shape in kernel.Parameters)
            {
                writer.Append(", ");
                WriteDelegateArgument(writer, shape);
            }

            if (kernel.ReturnsValue)
            {
                writer.Append(", ");
                writer.Append(AutoResultEmitter.PinnedAutoResult);
            }
        }

        writer.AppendLine(";");
        writer.AppendLine($"global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.ThrowIfNativeError({DllPath});");
    }

    // [WHY] A switch statement, not a switch expression: each form is written straight into the writer, so no argument
    // string is built (transpiler-emission.md section 1)
    private static void WriteDelegateArgument(SourceWriter writer, in KernelParameterShape shape)
    {
        var pinned = KernelHostNames.GetPinnedLocal(shape);
        switch (shape)
        {
            case { IsArray: true, IsNullableReference: true }:
                // A null optional array reads as an empty one on the device (GSharpArray::flags, #706)
                writer.Append(
                    $"new global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy {{ Data = (global::System.IntPtr){pinned}, Length = {shape.LengthExpression}, ");
                writer.Append(
                    $"Flags = {shape.Identifier} is null ? global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy.NullReadsAsEmpty : 0 }}");
                break;
            case { IsArray: true }:
                writer.Append(
                    $"new global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy {{ Data = (global::System.IntPtr){pinned}, Length = {shape.LengthExpression} }}");
                break;
            case { IsCpuConvertedStruct: true }:
                writer.Append($"&{KernelHostNames.GetNativeLocal(shape)}");
                break;
            case { IsByRef: true }:
                writer.Append(pinned);
                break;
            case { IsCpuValueStruct: true }:
                // The parameter of Launch is a value parameter, a fixed variable whose address needs no pinning
                writer.Append($"&{shape.Identifier}");
                break;
            default:
                writer.Append(shape.Identifier);
                break;
        }
    }
}
