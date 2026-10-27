namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits the CPU host launch members of a kernel wrapper class: delegate, managed fallback and native call.</summary>
internal static class CpuHostLaunchEmitter
{
    private const string PinnedAutoResult = "p_" + GSharpInternalNames.ReturnValue.Buffer;

    // The managed launcher's context slot for the thread currently being stepped.
    private const string LinearContext =
        $"{GSharpInternalNames.Launcher.Contexts}[{GSharpInternalNames.Launcher.LinearThreadIndex}]";

    /// <summary>Emits the managed source, the delegate declaration and the launch wrapper of one kernel.</summary>
    public static void WriteKernelMembers(SourceWriter writer, in KernelEmission kernel, string assemblyName)
    {
        writer.AppendLine(kernel.ManagedCSharpSource);
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
            WrapperEmitter.WriteAutoResultAllocation(writer, kernel.Host);
            WriteManagedExecutionFallback(writer, kernel.Host, assemblyName);

            // [WHY] Only the native launcher reads the host bytes as the C++ layout; the managed fallback uses the values
            WrapperEmitter.WriteLayoutGuards(writer, kernel.Host.CpuLayoutGuards);
            WriteCpuDelegateGetOrCompile(writer, kernel);
            WriteCpuFixedBlocksAndInvoke(writer, kernel.Host);
        }

        writer.AppendLine("#pragma warning restore CS8500");
    }

    private static void WriteCpuDelegateDeclaration(SourceWriter writer, in KernelEmission kernel)
    {
        // Generate Delegate
        writer.AppendLine(
            "[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]");
        writer.Append("private unsafe delegate void ");
        writer.Append(kernel.MemberKey);
        writer.Append("_Delegate");
        using (writer.Parentheses())
        {
            writer.Append(
                "uint gridDimX, uint gridDimY, uint gridDimZ, uint blockDimX, uint blockDimY, uint blockDimZ");
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

            if (kernel.Host.ReturnTypeName is { } returnType)
            {
                writer.Append($", {returnType}* {GSharpInternalNames.ReturnValue.Buffer}");
            }
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    private static void WriteManagedExecutionFallback(SourceWriter writer, in KernelHostModel kernel, string assemblyName)
    {
        var safeAssemblyName = AotHostNames.ToSafeIdentifier(assemblyName);
        var contextName = kernel.ContextName;
        var stateMachineName = kernel.StateMachineName;

        writer.AppendLine(
            "var archSuffix = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==" +
            " System.Runtime.InteropServices.Architecture.Arm64 ? \"_arm64\" : \"_x64\";");
        writer.AppendLine(
            $"var dllName = $\"Kassyi.GSharp.Kernels_{safeAssemblyName}{{archSuffix}}.dll\";");
        writer.AppendLine(
            "var dllPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, dllName);");

        writer.AppendLine(
            "if (global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.ShouldUseManagedExecution(dllPath))");
        using (writer.Scope())
        {
            writer.AppendLine($"int {GSharpInternalNames.Launcher.TotalBlocks} = (int)(gridDimX * gridDimY * gridDimZ);");
            writer.AppendLine($"int {GSharpInternalNames.Launcher.ThreadsPerBlock} = (int)(blockDimX * blockDimY * blockDimZ);");
            writer.AppendLine(
                $"var {GSharpInternalNames.Launcher.Contexts} = new {contextName}[{GSharpInternalNames.Launcher.TotalBlocks} * {GSharpInternalNames.Launcher.ThreadsPerBlock}];");
            writer.AppendLine($"for (int i = 0; i < {GSharpInternalNames.Launcher.Contexts}.Length; i++)");
            using (writer.Scope())
            {
                writer.AppendLine($"{GSharpInternalNames.Launcher.Contexts}[i] = new {contextName}();");
            }

            writer.AppendLine(
                $"global::Kassyi.GSharp.Core.ManagedGpuContext.InitializeGrid({GSharpInternalNames.Launcher.TotalBlocks});");

            writer.AppendLine("bool anyGridActive = true;");
            writer.AppendLine("while (anyGridActive)");
            using (writer.Scope())
            {
                writer.AppendLine("anyGridActive = false;");
                writer.AppendLine(
                    $"for (int {GSharpInternalNames.Launcher.BlockLoopIndex} = 0; {GSharpInternalNames.Launcher.BlockLoopIndex} < {GSharpInternalNames.Launcher.TotalBlocks}; {GSharpInternalNames.Launcher.BlockLoopIndex}++)");
                using (writer.Scope())
                {
                    writer.AppendLine($"int {GSharpInternalNames.Launcher.BlockX} = {GSharpInternalNames.Launcher.BlockLoopIndex} % (int)gridDimX;");
                    writer.AppendLine($"int {GSharpInternalNames.Launcher.BlockY} = ({GSharpInternalNames.Launcher.BlockLoopIndex} / (int)gridDimX) % (int)gridDimY;");
                    writer.AppendLine($"int {GSharpInternalNames.Launcher.BlockZ} = {GSharpInternalNames.Launcher.BlockLoopIndex} / (int)(gridDimX * gridDimY);");

                    writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.IsActive = true;");
                    writer.AppendLine("try");
                    using (writer.Scope())
                    {
                        writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.BlockIdxX = {GSharpInternalNames.Launcher.BlockX};");
                        writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.BlockIdxY = {GSharpInternalNames.Launcher.BlockY};");
                        writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.BlockIdxZ = {GSharpInternalNames.Launcher.BlockZ};");
                        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimX = (int)blockDimX;");
                        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimY = (int)blockDimY;");
                        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimZ = (int)blockDimZ;");
                    writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.GridDimX = (int)gridDimX;");
                    writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.GridDimY = (int)gridDimY;");
                    writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.GridDimZ = (int)gridDimZ;");
                    writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.ResetWarpExchange();");

                    writer.AppendLine("bool anyBlockActive = true;");
                    writer.AppendLine("while (anyBlockActive)");
                    using (writer.Scope())
                    {
                        writer.AppendLine("anyBlockActive = false;");
                        writer.AppendLine("bool gridSyncRequested = false;");
                        writer.AppendLine(
                            $"for (int {GSharpInternalNames.Launcher.ThreadLoopIndex} = 0; {GSharpInternalNames.Launcher.ThreadLoopIndex} < {GSharpInternalNames.Launcher.ThreadsPerBlock}; {GSharpInternalNames.Launcher.ThreadLoopIndex}++)");
                        using (writer.Scope())
                        {
                            writer.AppendLine($"int {GSharpInternalNames.Launcher.ThreadX} = {GSharpInternalNames.Launcher.ThreadLoopIndex} % (int)blockDimX;");
                            writer.AppendLine(
                                $"int {GSharpInternalNames.Launcher.ThreadY} = ({GSharpInternalNames.Launcher.ThreadLoopIndex} / (int)blockDimX) % (int)blockDimY;");
                            writer.AppendLine(
                                $"int {GSharpInternalNames.Launcher.ThreadZ} = {GSharpInternalNames.Launcher.ThreadLoopIndex} / (int)(blockDimX * blockDimY);");
                            writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.ThreadIdxX = {GSharpInternalNames.Launcher.ThreadX};");
                            writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.ThreadIdxY = {GSharpInternalNames.Launcher.ThreadY};");
                            writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.ThreadIdxZ = {GSharpInternalNames.Launcher.ThreadZ};");

                            writer.AppendLine(
                                $"int {GSharpInternalNames.Launcher.LinearThreadIndex} = {GSharpInternalNames.Launcher.BlockLoopIndex} * {GSharpInternalNames.Launcher.ThreadsPerBlock} + {GSharpInternalNames.Launcher.ThreadLoopIndex};");
                            writer.AppendLine($"if ({LinearContext}.{GSharpInternalNames.StateMachine.State} != -1)");
                            using (writer.Scope())
                            {
                                writer.AppendLine("anyGridActive = true;");
                                writer.AppendLine("anyBlockActive = true;");
                                writer.AppendLine($"{LinearContext}.{GSharpInternalNames.StateMachine.YieldedForGridSync} = false;");

                                writer.Append($"{stateMachineName}(");
                                bool isFirst = true;
                                foreach (var p in kernel.Parameters)
                                {
                                    if (!isFirst)
                                    {
                                        writer.Append(", ");
                                    }

                                    isFirst = false;
                                    if (p.RefKind is RefKind.Ref or RefKind.Out)
                                    {
                                        writer.Append("ref ");
                                    }
                                    else if (p.RefKind is RefKind.RefReadOnlyParameter)
                                    {
                                        writer.Append("in ");
                                    }

                                    writer.Append(p.Identifier);
                                }

                                if (!isFirst)
                                {
                                    writer.Append(", ");
                                }

                                writer.Append(LinearContext);
                                if (kernel.ReturnsValue)
                                {
                                    writer.Append($", ref {GSharpInternalNames.ReturnValue.Buffer}[{GSharpInternalNames.Launcher.LinearThreadIndex}]");
                                }

                                writer.AppendLine(");");

                                writer.AppendLine($"if ({LinearContext}.{GSharpInternalNames.StateMachine.YieldedForGridSync})");
                                using (writer.Scope())
                                {
                                    writer.AppendLine("gridSyncRequested = true;");
                                }
                            }
                        }

                        writer.AppendLine("if (gridSyncRequested)");
                        using (writer.Scope())
                        {
                            writer.AppendLine("break;");
                        }
                    }

                    }
                    writer.AppendLine("finally");
                    using (writer.Scope())
                    {
                        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.IsActive = false;");
                    }
                }
            }

            WrapperEmitter.WriteAutoResultReturn(writer, kernel);
            if (!kernel.ReturnsValue)
            {
                writer.AppendLine("return;");
            }
        }
    }

    private static void WriteCpuDelegateGetOrCompile(SourceWriter writer, in KernelEmission kernel)
    {
        var exportName = kernel.Host.LaunchExportName;

        writer.AppendLine(
            $"var hfunc = global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.LoadAotKernel<{kernel.MemberKey}_Delegate>(\"{exportName}\", dllPath);");
    }

    // <shadow> <name>_native = default; var <name>_allocatedPtrs = new List<IntPtr>(); (each converted struct)
    // byte[]? <name>_shadow = null; var <name>_allocatedPtrs = ...; int <name>_size = ...; (each converted array)
    // try { Marshal_<S>(ref <name>, ref <name>_native, false, <name>_allocatedPtrs); <the elements of each array>;
    // <fixed blocks and call>; Unmarshal_<S>(ref <name>, ref <name>_native, false); <the elements back> }
    // finally { foreach (var handle in <name>_allocatedPtrs) GCHandle.FromIntPtr(handle).Free(); ...;
    // ArrayPool<byte>.Shared.Return(<name>_shadow); }
    // [WHY] Marshal_ pins the array fields of a converted struct or element and records each handle; this one finally
    // block frees them and returns the rented shadows after the call and the copy back, also when the marshaling, the
    // native call or its error check throws.
    private static void WriteCpuFixedBlocksAndInvoke(SourceWriter writer, in KernelHostModel kernel)
    {
        var hasConverted = false;
        foreach (var shape in kernel.Parameters)
        {
            if (shape is { IsCpuConvertedStruct: true, AotStructPath: { } shadow })
            {
                writer.AppendLine($"{shadow} {shape.Name}_native = default;");
                writer.AppendLine(
                    $"var {shape.Name}_allocatedPtrs = new System.Collections.Generic.List<IntPtr>();");
                hasConverted = true;
            }
            else if (shape is { IsCpuConvertedArray: true, Cpu.ConvertedElementAotStructPath: { } element })
            {
                writer.AppendLine($"byte[]? {shape.Name}_shadow = null;");
                writer.AppendLine(
                    $"var {shape.Name}_allocatedPtrs = new System.Collections.Generic.List<IntPtr>();");
                writer.AppendLine($"int {shape.Name}_size = checked({shape.LengthExpression} * sizeof({element}));");
                hasConverted = true;
            }
        }

        if (!hasConverted)
        {
            WriteFixedBlocksAndInvoke(writer, kernel);
            WrapperEmitter.WriteAutoResultReturn(writer, kernel);
            return;
        }

        writer.AppendLine("try");
        using (writer.Scope())
        {
            foreach (var shape in kernel.Parameters)
            {
                if (shape is { IsCpuConvertedStruct: true, MarshalMethodName: { } marshal })
                {
                    writer.AppendLine(
                        $"{marshal}(ref {shape.Identifier}, ref {shape.Name}_native, false, {shape.Name}_allocatedPtrs);");
                }
                else if (shape.IsCpuConvertedArray)
                {
                    WriteConvertedArrayMarshal(writer, shape);
                }
            }

            WriteFixedBlocksAndInvoke(writer, kernel);

            // [WHY] A struct by value is unmarshaled as well: its own fields go back only to Launch's copy, but an array
            // field whose elements are converted shares the caller's array, whose elements must get the kernel's writes
            foreach (var shape in kernel.Parameters)
            {
                if (shape is { IsCpuConvertedStruct: true, UnmarshalMethodName: { } unmarshal })
                {
                    writer.AppendLine($"{unmarshal}(ref {shape.Identifier}, ref {shape.Name}_native, false);");
                }
                else if (shape is { IsCpuConvertedArray: true, CopyOut: true, IsReadOnlyContainer: false })
                {
                    WriteConvertedArrayUnmarshal(writer, shape);
                }
            }
        }

        writer.AppendLine("finally");
        using (writer.Scope())
        {
            foreach (var shape in kernel.Parameters)
            {
                if (shape.IsCpuConvertedStruct || shape.IsCpuConvertedArray)
                {
                    writer.AppendLine(
                        $"foreach (var handle in {shape.Name}_allocatedPtrs) System.Runtime.InteropServices.GCHandle.FromIntPtr(handle).Free();");
                }

                if (shape.IsCpuConvertedArray)
                {
                    writer.AppendLine(
                        $"if ({shape.Name}_shadow != null) System.Buffers.ArrayPool<byte>.Shared.Return({shape.Name}_shadow);");
                }
            }
        }

        WrapperEmitter.WriteAutoResultReturn(writer, kernel);
    }

    // <name>_shadow = ArrayPool<byte>.Shared.Rent(<name>_size); <spans>;
    // for (...) Marshal_<E>(ref <name>_host[i], ref <name>_native[i], false, <name>_allocatedPtrs);   ([In] / in-out)
    // <name>_native.Clear();                                       ([Out] of an element without array fields)
    // [WHY] The elements differ in layout, so they are converted into a rented shadow array of the device layout, which
    // the kernel reads in place (the CUDA launcher copies the same shadow to the device). An [Out] element with array
    // fields is still marshaled: the kernel writes into its pinned arrays. A rented array holds the bytes of an earlier
    // use, so an [Out] shadow is cleared rather than left to the kernel.
    private static void WriteConvertedArrayMarshal(SourceWriter writer, in KernelParameterShape shape)
    {
        var name = shape.Name;
        writer.AppendLine($"{name}_shadow = System.Buffers.ArrayPool<byte>.Shared.Rent({name}_size);");
        WriteShadowAndHostSpans(writer, shape);
        if (!shape.CopyIn && !shape.Cpu.ConvertedElementHasArrayFields)
        {
            writer.AppendLine($"{name}_native.Clear();");
            return;
        }

        writer.AppendLine($"for (int {name}_index = 0; {name}_index < {name}_native.Length; {name}_index++)");
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
                $"{shape.Cpu.ConvertedElementMarshalMethodName}(ref {hostElement}, " +
                $"ref {name}_native[{name}_index], false, {name}_allocatedPtrs);");
        }
    }

    // for (...) Unmarshal_<E>(ref <name>_host[i], ref <name>_native[i], false); after the call, through the spans that
    // the marshaling declared in the same try block
    private static void WriteConvertedArrayUnmarshal(SourceWriter writer, in KernelParameterShape shape)
    {
        var name = shape.Name;
        writer.AppendLine($"for (int {name}_index = 0; {name}_index < {name}_native.Length; {name}_index++)");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"{shape.Cpu.ConvertedElementUnmarshalMethodName}(ref {name}_host[{name}_index], " +
                $"ref {name}_native[{name}_index], false);");
        }
    }

    // <name>_native spans the rented bytes in the device layout; <name>_host spans the argument
    private static void WriteShadowAndHostSpans(SourceWriter writer, in KernelParameterShape shape)
    {
        writer.AppendLine(
            $"var {shape.Name}_native = System.Runtime.InteropServices.MemoryMarshal" +
            $".Cast<byte, {shape.Cpu.ConvertedElementAotStructPath}>({shape.Name}_shadow.AsSpan(0, {shape.Name}_size));");
        var span = shape.IsReadOnlyContainer ? "System.ReadOnlySpan" : "System.Span";
        writer.AppendLine($"{span}<{shape.ElementTypeName}> {shape.Name}_host = {shape.SpanExpression};");
    }

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

        if (kernel.ReturnTypeName is { } returnType)
        {
            writer.AppendLine($"fixed ({returnType}* {PinnedAutoResult} = {GSharpInternalNames.ReturnValue.Buffer})");
            hasFixed = true;
        }

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
            writer.Append("gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ");

            foreach (var shape in kernel.Parameters)
            {
                writer.Append(", ");

                if (shape.IsArray)
                {
                    writer.Append(
                        $"new global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy {{ Data = (IntPtr)p_{shape.Name}, Length = {shape.LengthExpression} }}");
                }
                else if (shape.IsCpuConvertedStruct)
                {
                    writer.Append($"&{shape.Name}_native");
                }
                else if (shape.IsByRef)
                {
                    writer.Append($"p_{shape.Name}");
                }
                else if (shape.IsCpuValueStruct)
                {
                    // The parameter of Launch is a value parameter, a fixed variable whose address needs no pinning
                    writer.Append($"&{shape.Identifier}");
                }
                else
                {
                    writer.Append(shape.Identifier);
                }
            }

            if (kernel.ReturnsValue)
            {
                writer.Append(", ");
                writer.Append(PinnedAutoResult);
            }
        }

        writer.AppendLine(";");
        writer.AppendLine("global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.ThrowIfNativeError(dllPath);");
    }
}
