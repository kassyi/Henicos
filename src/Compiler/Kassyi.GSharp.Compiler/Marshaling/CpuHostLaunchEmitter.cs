namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits the CPU host launch members of a kernel wrapper class: delegate, the copies of <c>[In]</c> arrays
/// (<see cref="CpuArrayCopyEmitter"/>), managed fallback (<see cref="ManagedFallbackEmitter"/>) and native call.
/// </summary>
internal static class CpuHostLaunchEmitter
{
    private const string ReferenceEqualsMethod = ArraySharingPlan.ReferenceEqualsMethod;

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
        // [WHY] UnmanagedFunctionPointer defaults to CharSet.Ansi, which converts a char value parameter to 1-byte
        // ANSI instead of the 2-byte UTF-16 char16_t that the native CPU launcher takes (#971).
        var hasCharParameter = kernel.Host.Parameters
            .AsStackEnumerable()
            .Any(static p => !p.IsByRef && p.TypeName == "global::System.Char");

        // Generate Delegate
        writer.Append(
            "[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl");
        writer.AppendIf(hasCharParameter, ", CharSet = System.Runtime.InteropServices.CharSet.Unicode");
        writer.AppendLine(")]");
        writer.Append("private unsafe delegate void ");
        writer.Append(kernel.MemberKey);
        writer.Append("_Delegate");
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

    // var dllPath = <the native library of the assembly for this architecture>;
    private static void WriteDllPath(SourceWriter writer, string assemblyName)
    {
        var safeAssemblyName = AotHostNames.ToSafeIdentifier(assemblyName);
        writer.AppendLine(
            "var archSuffix = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==" +
            " System.Runtime.InteropServices.Architecture.Arm64 ? \"_arm64\" : \"_x64\";");
        writer.AppendLine(
            $"var dllName = $\"Kassyi.GSharp.Kernels_{safeAssemblyName}{{archSuffix}}.dll\";");
        writer.AppendLine(
            "var dllPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, dllName);");
    }

    private static void WriteCpuDelegateGetOrCompile(SourceWriter writer, in KernelEmission kernel)
    {
        var exportName = kernel.Host.LaunchExportName;

        writer.AppendLine(
            $"var hfunc = global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.LoadAotKernel<{kernel.MemberKey}_Delegate>(\"{exportName}\", dllPath);");
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

    // <shadow> <name>_native = default; var <name>_allocatedPtrs = new List<IntPtr>();                  (converted struct)
    // byte[]? <name>_shadow = null; var <name>_allocatedPtrs = ...; int <name>_size = checked(...);      (converted array)
    // byte[]? <name>_sharedShadow = null;                         (a converted T[] that an earlier holder may share)
    // Returns whether the parameter is converted. The size is computed before the try block, when nothing is allocated.
    private static bool WriteConvertedDeclaration(SourceWriter writer, in ArraySharingPlan sharing, int index)
    {
        var shape = sharing.Parameters[index];
        writer.AppendLineIf(sharing.HasShadowSource(index), $"byte[]? {shape.Name}_sharedShadow = null;");
        switch (shape)
        {
            case { IsCpuConvertedStruct: true, AotStructPath: { } shadow }:
                writer.AppendLine($"{shadow} {shape.Name}_native = default;");
                writer.AppendLine($"var {shape.Name}_allocatedPtrs = new System.Collections.Generic.List<IntPtr>();");
                return true;
            case { IsCpuConvertedArray: true, Cpu.ConvertedElementAotStructPath: { } element }:
                writer.AppendLine($"byte[]? {shape.Name}_shadow = null;");
                writer.AppendLine($"var {shape.Name}_allocatedPtrs = new System.Collections.Generic.List<IntPtr>();");
                writer.AppendLine($"int {shape.Name}_size = checked({shape.LengthExpression} * sizeof({element}));");
                return true;
            default:
                return false;
        }
    }

    // Marshal_<S>(ref <name>, ref <name>_native, false, <name>_allocatedPtrs, hStream);                (converted struct)
    // <name>_shadow = ArrayPool<byte>.Shared.Rent(<name>_size); <spans>;                                (converted array)
    // for (...) Marshal_<E>(ref <name>_host[i], ref <name>_native[i], false, <name>_allocatedPtrs, hStream); ([In] / in-out)
    // <name>_native.Clear();                                       ([Out] of an element without array fields)
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
                    $"{marshal}(ref {shape.Identifier}, ref {shape.Name}_native, false, {shape.Name}_allocatedPtrs, hStream);");
                break;
            case { IsCpuConvertedArray: true } when sharing.HasShadowSource(index):
                WriteSharedShadowLookup(writer, sharing, index);
                writer.AppendLine(
                    $"{shape.Name}_shadow = {shape.Name}_sharedShadow ?? System.Buffers.ArrayPool<byte>.Shared.Rent({shape.Name}_size);");
                ConvertedArrayEmitter.WriteShadowAndHostSpans(writer, shape, shape.Cpu);
                writer.AppendLine($"if ({shape.Name}_sharedShadow == null)");
                using (writer.Scope())
                {
                    WriteConvertedElements(writer, sharing, index);
                }

                break;
            case { IsCpuConvertedArray: true }:
                writer.AppendLine($"{shape.Name}_shadow = System.Buffers.ArrayPool<byte>.Shared.Rent({shape.Name}_size);");
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
            writer.AppendLine($"{shape.Name}_native.Clear();");
        }
        else
        {
            ConvertedArrayEmitter.WriteElementMarshalLoop(writer, shape, shape.Cpu, $"{shape.Name}_allocatedPtrs");
        }
    }

    // <name>_sharedShadow = ReferenceEquals(<name>, <earlier>) ? <earlier>_shadow : ... : null;
    // [WHY] Converted arguments that hold one array share one shadow, which the native kernel reads and writes in place,
    // as the CUDA launcher gives them one device region (#721). The first holder converts the elements into it; an [Out]
    // holder comes last, so that it never clears what an earlier holder converted. Every holder that copies back reads
    // the same shadow, so the host elements get the same values.
    private static void WriteSharedShadowLookup(SourceWriter writer, in ArraySharingPlan sharing, int index)
    {
        var shape = sharing.Parameters[index];
        writer.Append($"{shape.Name}_sharedShadow = ");
        for (var k = 0; k < sharing.Parameters.Length; k++)
        {
            if (sharing.IsShadowSource(index, k))
            {
                var source = sharing.Parameters[k];
                writer.Append(
                    $"{ReferenceEqualsMethod}({shape.Identifier}, {source.Identifier}) ? {source.Name}_shadow : ");
            }
        }

        writer.AppendLine("null;");
    }

    // Unmarshal_<S>(ref <name>, ref <name>_native, false, hStream);                                     (converted struct)
    // for (...) Unmarshal_<E>(ref <name>_host[i], ref <name>_native[i], false, hStream);  (converted array, copied back)
    // The array goes through the spans that the marshaling declared in the same try block.
    // [WHY] A struct by value is unmarshaled as well: its own fields go back only to Launch's copy, but an array field
    // whose elements are converted shares the caller's array, whose elements must get the kernel's writes
    private static void WriteConvertedUnmarshal(SourceWriter writer, in KernelParameterShape shape)
    {
        switch (shape)
        {
            case { IsCpuConvertedStruct: true, UnmarshalMethodName: { } unmarshal }:
                writer.AppendLine($"{unmarshal}(ref {shape.Identifier}, ref {shape.Name}_native, false, hStream);");
                break;
            case { IsCpuConvertedArray: true, CopyOut: true, IsReadOnlyContainer: false }:
                ConvertedArrayEmitter.WriteElementUnmarshalLoop(writer, shape, shape.Cpu);
                break;
        }
    }

    // foreach (var handle in <name>_allocatedPtrs) GCHandle.FromIntPtr(handle).Free();
    // if (<name>_shadow != null) ArrayPool<byte>.Shared.Return(<name>_shadow);                        (converted array)
    // A shared shadow goes back to the pool from its owner only: ... && <name>_sharedShadow == null
    private static void WriteConvertedRelease(SourceWriter writer, in ArraySharingPlan sharing, int index)
    {
        var shape = sharing.Parameters[index];
        var notShared = sharing.HasShadowSource(index) ? $" && {shape.Name}_sharedShadow == null" : "";
        switch (shape)
        {
            case { IsCpuConvertedStruct: true }:
                WriteHandleRelease(writer, shape);
                break;
            case { IsCpuConvertedArray: true }:
                WriteHandleRelease(writer, shape);
                writer.AppendLine(
                    $"if ({shape.Name}_shadow != null{notShared}) System.Buffers.ArrayPool<byte>.Shared.Return({shape.Name}_shadow);");
                break;
        }
    }

    private static void WriteHandleRelease(SourceWriter writer, in KernelParameterShape shape) =>
        writer.AppendLine(
            $"foreach (var handle in {shape.Name}_allocatedPtrs) System.Runtime.InteropServices.GCHandle.FromIntPtr(handle).Free();");

    private static void WriteFixedBlocksAndInvoke(SourceWriter writer, in KernelHostModel kernel)
    {
        var hasFixed = false;

        foreach (var shape in kernel.Parameters)
        {
            if (shape.IsCpuConvertedArray)
            {
                writer.AppendLine($"fixed (byte* p_{shape.Name} = {shape.Name}_shadow)");
                hasFixed = true;
            }
            else if (shape is { IsArray: true, ElementTypeName: { } elementType })
            {
                writer.AppendLine($"fixed ({elementType}* p_{shape.Name} = {shape.SpanExpression})");
                hasFixed = true;
            }
            else if (shape is { IsByRef: true, IsCpuConvertedStruct: false })
            {
                writer.AppendLine($"fixed ({shape.TypeName}* p_{shape.Name} = &{shape.Identifier})");
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
        writer.Append("hfunc");
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
        writer.AppendLine("global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.ThrowIfNativeError(dllPath);");
    }

    // [WHY] A switch statement, not a switch expression: each form is written straight into the writer, so no argument
    // string is built (transpiler-emission.md section 1)
    private static void WriteDelegateArgument(SourceWriter writer, in KernelParameterShape shape)
    {
        switch (shape)
        {
            case { IsArray: true, IsNullableReference: true }:
                // A null optional array reads as an empty one on the device (GSharpArray::flags, #706)
                writer.Append(
                    $"new global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy {{ Data = (IntPtr)p_{shape.Name}, Length = {shape.LengthExpression}, ");
                writer.Append(
                    $"Flags = {shape.Identifier} is null ? global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy.NullReadsAsEmpty : 0 }}");
                break;
            case { IsArray: true }:
                writer.Append(
                    $"new global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy {{ Data = (IntPtr)p_{shape.Name}, Length = {shape.LengthExpression} }}");
                break;
            case { IsCpuConvertedStruct: true }:
                writer.Append($"&{shape.Name}_native");
                break;
            case { IsByRef: true }:
                writer.Append($"p_{shape.Name}");
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
