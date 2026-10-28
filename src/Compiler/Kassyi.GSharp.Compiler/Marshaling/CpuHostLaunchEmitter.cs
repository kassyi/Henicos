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
            WrapperEmitter.WriteAutoResultAllocation(writer, kernel.Host);
            WriteKernelCopies(writer, kernel.Host);
            WriteManagedExecutionFallback(writer, kernel.Host, assemblyName);

            // [WHY] Only the native launcher reads the host bytes as the C++ layout; the managed fallback uses the values
            WrapperEmitter.WriteLayoutGuards(writer, kernel.Host.CpuLayoutGuards, kernel.MemberKey);
            WriteCpuDelegateGetOrCompile(writer, kernel);
            WriteCpuFixedBlocksAndInvoke(writer, kernel.Host);
        }

        writer.AppendLine("#pragma warning restore CS8500");
    }

    // if (<name> != null) {                                                       (an optional argument only)
    //   <name> = (<array type>)<name>.Clone();                         (<span>.ToArray() for the other containers)
    // or, for elements that hold arrays:
    //   var <name>_copy = <the same copy>; for (...) CopyArrays_<E>(ref <name>_copy[<name>_copyIndex]);
    //   <name> = <name>_copy;                                          (ImmutableArray.Create(<name>_copy))
    // }
    // [WHY] The device gets a copy of an [In] array and never sends it back, and never sends back a read-only container
    // either, so on the CPU the kernel works on a copy as well (#798; the GPU result is the one to match). A read-only
    // container is copied only when its elements hold arrays, the one thing a kernel can write through it. The managed
    // fallback and the native call both take the copy.
    private static void WriteKernelCopies(SourceWriter writer, in KernelHostModel kernel)
    {
        foreach (var shape in kernel.Parameters)
        {
            var copyArrays = shape.Cpu.ConvertedElementCopyArraysMethodName;
            var needsCopy = shape.IsArray && (shape.IsReadOnlyContainer ? copyArrays is not null : !shape.CopyOut);
            if (!needsCopy)
            {
                continue;
            }

            if (shape.IsOptional)
            {
                writer.AppendLine(shape.IsNullableValue ? $"if ({shape.Identifier}.HasValue)" : $"if ({shape.Identifier} != null)");
                using (writer.Scope())
                {
                    WriteKernelCopy(writer, shape, copyArrays);
                }
            }
            else
            {
                WriteKernelCopy(writer, shape, copyArrays);
            }
        }
    }

    private static void WriteKernelCopy(SourceWriter writer, in KernelParameterShape shape, string? copyArrays)
    {
        // A read-only container (ImmutableArray among them) is copied only for elements that hold arrays
        if (copyArrays is null)
        {
            writer.Append($"{shape.Identifier} = ");
            WriteElementsCopy(writer, shape);
            writer.AppendLine(";");
            return;
        }

        var name = shape.Name;
        writer.Append($"var {name}_copy = ");
        WriteElementsCopy(writer, shape);
        writer.AppendLine(";");
        writer.AppendLine($"for (int {name}_copyIndex = 0; {name}_copyIndex < {name}_copy.Length; {name}_copyIndex++)");
        using (writer.Scope())
        {
            writer.AppendLine($"{copyArrays}(ref {name}_copy[{name}_copyIndex]);");
        }

        writer.AppendLine(shape.Container == ArrayContainerKind.ImmutableArray
            ? $"{shape.Identifier} = global::System.Collections.Immutable.ImmutableArray.Create({name}_copy);"
            : $"{shape.Identifier} = {name}_copy;");
    }

    // (<array type>)<name>.Clone(), or <span>.ToArray() for the other containers: a new array of the elements
    private static void WriteElementsCopy(SourceWriter writer, in KernelParameterShape shape) =>
        writer.Append(shape.Container == ArrayContainerKind.Array
            ? $"({shape.TypeName}){shape.Identifier}.Clone()"
            : $"{shape.SpanExpression}.ToArray()");

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
                                // The state machines of the kernel and of the device functions it calls request a
                                // grid sync through one thread-static flag (#665)
                                writer.AppendLine($"{ManagedExecutionRewriter.ManagedGridSyncRequested} = false;");

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

                                writer.AppendLine($"if ({ManagedExecutionRewriter.ManagedGridSyncRequested})");
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

    // <declarations>; try { <marshaling>; <fixed blocks and call>; <unmarshaling> } finally { <release> }
    // [WHY] Marshal_ pins the array fields of a converted struct or element and records each handle; this one finally
    // block frees them and returns the rented shadows after the call and the copy back, also when the marshaling, the
    // native call or its error check throws.
    private static void WriteCpuFixedBlocksAndInvoke(SourceWriter writer, in KernelHostModel kernel)
    {
        var hasConverted = false;
        foreach (var shape in kernel.Parameters)
        {
            hasConverted |= WriteConvertedDeclaration(writer, shape);
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
                WriteConvertedMarshal(writer, shape);
            }

            WriteFixedBlocksAndInvoke(writer, kernel);
            foreach (var shape in kernel.Parameters)
            {
                WriteConvertedUnmarshal(writer, shape);
            }
        }

        writer.AppendLine("finally");
        using (writer.Scope())
        {
            foreach (var shape in kernel.Parameters)
            {
                WriteConvertedRelease(writer, shape);
            }
        }

        WrapperEmitter.WriteAutoResultReturn(writer, kernel);
    }

    // <shadow> <name>_native = default; var <name>_allocatedPtrs = new List<IntPtr>();                  (converted struct)
    // byte[]? <name>_shadow = null; var <name>_allocatedPtrs = ...; int <name>_size = checked(...);      (converted array)
    // Returns whether the parameter is converted. The size is computed before the try block, when nothing is allocated.
    private static bool WriteConvertedDeclaration(SourceWriter writer, in KernelParameterShape shape)
    {
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
    private static void WriteConvertedMarshal(SourceWriter writer, in KernelParameterShape shape)
    {
        switch (shape)
        {
            case { IsCpuConvertedStruct: true, MarshalMethodName: { } marshal }:
                writer.AppendLine(
                    $"{marshal}(ref {shape.Identifier}, ref {shape.Name}_native, false, {shape.Name}_allocatedPtrs, hStream);");
                break;
            case { IsCpuConvertedArray: true }:
                writer.AppendLine($"{shape.Name}_shadow = System.Buffers.ArrayPool<byte>.Shared.Rent({shape.Name}_size);");
                ConvertedArrayEmitter.WriteShadowAndHostSpans(writer, shape, shape.Cpu);
                if (!shape.CopyIn && !shape.Cpu.ConvertedElementHasArrayFields)
                {
                    writer.AppendLine($"{shape.Name}_native.Clear();");
                }
                else
                {
                    ConvertedArrayEmitter.WriteElementMarshalLoop(writer, shape, shape.Cpu);
                }

                break;
        }
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
    private static void WriteConvertedRelease(SourceWriter writer, in KernelParameterShape shape)
    {
        switch (shape)
        {
            case { IsCpuConvertedStruct: true }:
                WriteHandleRelease(writer, shape);
                break;
            case { IsCpuConvertedArray: true }:
                WriteHandleRelease(writer, shape);
                writer.AppendLine(
                    $"if ({shape.Name}_shadow != null) System.Buffers.ArrayPool<byte>.Shared.Return({shape.Name}_shadow);");
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
                WriteDelegateArgument(writer, shape);
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

    // [WHY] A switch statement, not a switch expression: each form is written straight into the writer, so no argument
    // string is built (transpiler-emission.md section 1)
    private static void WriteDelegateArgument(SourceWriter writer, in KernelParameterShape shape)
    {
        switch (shape)
        {
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
