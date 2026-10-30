namespace Kassyi.GSharp.Compiler.Marshaling;

using static GSharpInternalNames.Launcher;

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

            if (kernel.Host.ReturnTypeName is { } returnType)
            {
                writer.Append($", {returnType}* {GSharpInternalNames.ReturnValue.Buffer}");
            }
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    // The managed launcher has the shape of the native one (HostLauncherTranspiler), one method per generated loop:
    // WriteManagedGridLoop (grid phases) > WriteManagedGridPhase (blocks) > WriteManagedBlock > WriteManagedBarrierRounds
    // (rounds between barriers) > WriteManagedThreadStep (one thread)
    private static void WriteManagedExecutionFallback(SourceWriter writer, in KernelHostModel kernel, string assemblyName)
    {
        WriteDllPath(writer, assemblyName);
        writer.AppendLine(
            "if (global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.ShouldUseManagedExecution(dllPath))");
        using (writer.Scope())
        {
            WriteManagedContextAllocation(writer, kernel);
            WriteManagedGridLoop(writer, kernel);
            WrapperEmitter.WriteAutoResultReturn(writer, kernel);
            if (!kernel.ReturnsValue)
            {
                writer.AppendLine("return;");
            }
        }
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

    // The contexts of every thread of the grid, the shared memory of the grid and the barrier waits
    private static void WriteManagedContextAllocation(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine($"int {TotalBlocks} = (int)(gridDimX * gridDimY * gridDimZ);");
        writer.AppendLine($"int {ThreadsPerBlock} = (int)(blockDimX * blockDimY * blockDimZ);");
        writer.AppendLine($"var {Contexts} = new {kernel.ContextName}[{TotalBlocks} * {ThreadsPerBlock}];");
        writer.AppendLine($"for (int i = 0; i < {Contexts}.Length; i++)");
        using (writer.Scope())
        {
            writer.AppendLine($"{Contexts}[i] = new {kernel.ContextName}();");
        }

        // A cooperative kernel's blocks run in grid phases and keep their dynamic shared memory for the grid; the
        // blocks of any other kernel run to their end one after another and use one array in turn
        writer.AppendLine(
            $"global::Kassyi.GSharp.Core.ManagedGpuContext.InitializeGrid({TotalBlocks}, sharedMemBytes, {(kernel.IsCooperative ? "true" : "false")});");

        // A thread that stopped at a block barrier waits there until every thread of its block that has not finished
        // waits too; in a cooperative kernel, a thread that reached the grid sync waits until the next grid phase (#664)
        writer.AppendLine($"var threadAtBlockBarrier = new bool[{Contexts}.Length];");
        if (kernel.IsCooperative)
        {
            writer.AppendLine($"var threadAtGridSync = new bool[{Contexts}.Length];");
        }
    }

    // bool anyGridActive = true; try { while (anyGridActive) { <grid phase> } } finally { EndGrid(); }
    private static void WriteManagedGridLoop(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine("bool anyGridActive = true;");
        // The dynamic shared memory of the blocks lives for one grid (#544), also when a step throws
        writer.AppendLine("try");
        using (writer.Scope())
        {
            // Each pass runs every block until its threads reach Gpu.SyncGrid or finish: one grid phase (#664)
            writer.AppendLine("while (anyGridActive)");
            using (writer.Scope())
            {
                WriteManagedGridPhase(writer, kernel);
            }
        }

        writer.AppendLine("finally");
        using (writer.Scope())
        {
            writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.EndGrid();");
        }
    }

    // One grid phase: every block in turn, then (cooperative) the check that no block finished while others wait
    private static void WriteManagedGridPhase(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine("anyGridActive = false;");
        if (kernel.IsCooperative)
        {
            writer.AppendLine("int firstFinishedBlock = -1;");
            writer.AppendLine("bool anyBlockAtGridSync = false;");
        }

        writer.AppendLine($"for (int {BlockLoopIndex} = 0; {BlockLoopIndex} < {TotalBlocks}; {BlockLoopIndex}++)");
        using (writer.Scope())
        {
            WriteManagedBlock(writer, kernel);
        }

        if (kernel.IsCooperative)
        {
            // A block that finished while others wait never arrives at the grid sync of the GPU
            writer.AppendLine("if (anyBlockAtGridSync && firstFinishedBlock >= 0)");
            using (writer.Scope())
            {
                writer.AppendLine(
                    $"throw global::Kassyi.GSharp.Core.ManagedGpuContext.BlockFinishedAtGridSync({SymbolDisplay.FormatLiteral(kernel.MessageName, quote: true)}, firstFinishedBlock, gridDimX, gridDimY);");
            }
        }
    }

    // One block of the managed launcher, stepped until its threads reach Gpu.SyncGrid or finish
    private static void WriteManagedBlock(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine($"int {BlockX} = {BlockLoopIndex} % (int)gridDimX;");
        writer.AppendLine($"int {BlockY} = ({BlockLoopIndex} / (int)gridDimX) % (int)gridDimY;");
        writer.AppendLine($"int {BlockZ} = {BlockLoopIndex} / (int)(gridDimX * gridDimY);");

        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.IsActive = true;");
        writer.AppendLine("try");
        using (writer.Scope())
        {
            WriteManagedBlockSetup(writer);
            if (kernel.IsCooperative)
            {
                // The threads that wait at the grid sync go on in this phase
                writer.AppendLine(
                    $"global::System.Array.Clear(threadAtGridSync, {BlockLoopIndex} * {ThreadsPerBlock}, {ThreadsPerBlock});");
                writer.AppendLine("bool blockAtGridSync = false;");
            }

            WriteManagedBarrierRounds(writer, kernel);
            if (kernel.IsCooperative)
            {
                WriteManagedBlockGridSyncStatus(writer, kernel);
            }
        }

        writer.AppendLine("finally");
        using (writer.Scope())
        {
            writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.IsActive = false;");
        }
    }

    // The block's coordinates and dimensions in ManagedGpuContext, and a fresh warp exchange
    private static void WriteManagedBlockSetup(SourceWriter writer)
    {
        writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.BlockIdxX = {BlockX};");
        writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.BlockIdxY = {BlockY};");
        writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.BlockIdxZ = {BlockZ};");
        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimX = (int)blockDimX;");
        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimY = (int)blockDimY;");
        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimZ = (int)blockDimZ;");
        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.GridDimX = (int)gridDimX;");
        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.GridDimY = (int)gridDimY;");
        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.GridDimZ = (int)gridDimZ;");
        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.ResetWarpExchange();");
    }

    // [WHY] A thread that reached a barrier waits there while the other threads of the block go on to it: they may need
    // more steps (a shuffle before it). When no thread can run, the threads at a block barrier pass it together; the
    // block's part of the phase ends when no thread can run and none waits at a block barrier.
    // bool anyBlockActive = true; while (anyBlockActive) { anyBlockActive = false; <threads> <block barrier release> }
    private static void WriteManagedBarrierRounds(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine("bool anyBlockActive = true;");
        writer.AppendLine("while (anyBlockActive)");
        using (writer.Scope())
        {
            writer.AppendLine("anyBlockActive = false;");
            writer.AppendLine($"for (int {ThreadLoopIndex} = 0; {ThreadLoopIndex} < {ThreadsPerBlock}; {ThreadLoopIndex}++)");
            using (writer.Scope())
            {
                WriteManagedThreadStep(writer, kernel);
            }

            WriteManagedBlockBarrierRelease(writer);
        }
    }

    // if (!anyBlockActive) { for (<threads of the block>) { if (<waits at the block barrier>) { <release it>; anyBlockActive = true; } } }
    private static void WriteManagedBlockBarrierRelease(SourceWriter writer)
    {
        writer.AppendLine("if (!anyBlockActive)");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"for (int {LinearThreadIndex} = {BlockLoopIndex} * {ThreadsPerBlock}; {LinearThreadIndex} < ({BlockLoopIndex} + 1) * {ThreadsPerBlock}; {LinearThreadIndex}++)");
            using (writer.Scope())
            {
                writer.AppendLine($"if (threadAtBlockBarrier[{LinearThreadIndex}])");
                using (writer.Scope())
                {
                    writer.AppendLine($"threadAtBlockBarrier[{LinearThreadIndex}] = false;");
                    writer.AppendLine("anyBlockActive = true;");
                }
            }
        }
    }

    // Cooperative: whether the block waits at the grid sync, with the check of its thread 0, or finished
    private static void WriteManagedBlockGridSyncStatus(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine("if (blockAtGridSync)");
        using (writer.Scope())
        {
            // The grid sync of the GPU waits for thread 0 of every block
            writer.AppendLine(
                $"if ({Contexts}[{BlockLoopIndex} * {ThreadsPerBlock}].{GSharpInternalNames.StateMachine.State} == -1)");
            using (writer.Scope())
            {
                writer.AppendLine(
                    $"throw global::Kassyi.GSharp.Core.ManagedGpuContext.RepresentativeFinishedAtGridSync({SymbolDisplay.FormatLiteral(kernel.MessageName, quote: true)}, {BlockX}, {BlockY}, {BlockZ});");
            }

            writer.AppendLine("anyBlockAtGridSync = true;");
        }

        writer.AppendLine("else if (firstFinishedBlock < 0)");
        using (writer.Scope())
        {
            writer.AppendLine($"firstFinishedBlock = {BlockLoopIndex};");
        }
    }

    // One step of one thread: from where it stopped to its next barrier, grid sync or end
    private static void WriteManagedThreadStep(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine($"int {ThreadX} = {ThreadLoopIndex} % (int)blockDimX;");
        writer.AppendLine($"int {ThreadY} = ({ThreadLoopIndex} / (int)blockDimX) % (int)blockDimY;");
        writer.AppendLine($"int {ThreadZ} = {ThreadLoopIndex} / (int)(blockDimX * blockDimY);");
        writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.ThreadIdxX = {ThreadX};");
        writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.ThreadIdxY = {ThreadY};");
        writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.ThreadIdxZ = {ThreadZ};");

        writer.AppendLine($"int {LinearThreadIndex} = {BlockLoopIndex} * {ThreadsPerBlock} + {ThreadLoopIndex};");
        // A thread that waits at a block barrier or at the grid sync does not run
        var runnable = kernel.IsCooperative ? CooperativeRunnableCondition : RunnableCondition;
        writer.AppendLine($"if ({runnable})");
        using (writer.Scope())
        {
            writer.AppendLine("anyGridActive = true;");
            // The state machines of the kernel and of the device functions it calls request a grid sync (#665) and report
            // a block barrier through thread-static flags; the launcher steps one GPU thread at a time on this host thread
            writer.AppendLine($"{ManagedExecutionRewriter.ManagedGridSyncRequested} = false;");
            writer.AppendLine($"{ManagedExecutionRewriter.ManagedBlockBarrierRequested} = false;");
            WriteStateMachineCall(writer, kernel);
            WriteManagedWaits(writer, kernel);

            // The next round runs while a thread can go on: one that stopped at a shuffle runs again, one at a barrier
            // waits
            writer.AppendLine($"if ({runnable})");
            using (writer.Scope())
            {
                writer.AppendLine("anyBlockActive = true;");
            }
        }
    }

    // <context>.__gsharp_state != -1 && !threadAtBlockBarrier[<thread>]
    private const string RunnableCondition =
        $"{LinearContext}.{GSharpInternalNames.StateMachine.State} != -1 && !threadAtBlockBarrier[{LinearThreadIndex}]";

    // ... && !threadAtGridSync[<thread>]
    private const string CooperativeRunnableCondition = $"{RunnableCondition} && !threadAtGridSync[{LinearThreadIndex}]";

    // <state machine>(<arguments>, __gsharp_contexts[<thread>][, ref __gsharp_auto_result[<thread>]]);
    private static void WriteStateMachineCall(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.Append($"{kernel.StateMachineName}(");
        foreach (var p in kernel.Parameters)
        {
            writer.Append(p.RefKind switch
            {
                RefKind.Ref or RefKind.Out => "ref ",
                RefKind.RefReadOnlyParameter => "in ",
                _ => string.Empty
            });
            writer.Append(p.Identifier);
            writer.Append(", ");
        }

        writer.Append(LinearContext);
        if (kernel.ReturnsValue)
        {
            writer.Append($", ref {GSharpInternalNames.ReturnValue.Buffer}[{LinearThreadIndex}]");
        }

        writer.AppendLine(");");
    }

    // After a step: a thread that reached the grid sync (cooperative: waits; otherwise an error) or a block barrier waits
    private static void WriteManagedWaits(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine($"if ({ManagedExecutionRewriter.ManagedGridSyncRequested})");
        using (writer.Scope())
        {
            if (kernel.IsCooperative)
            {
                writer.AppendLine($"threadAtGridSync[{LinearThreadIndex}] = true;");
                writer.AppendLine("blockAtGridSync = true;");
            }
            else
            {
                // Undefined on the GPU: grid.sync() in a launch that is not cooperative
                writer.AppendLine(
                    $"throw global::Kassyi.GSharp.Core.ManagedGpuContext.GridSyncInKernelThatIsNotCooperative({SymbolDisplay.FormatLiteral(kernel.MessageName, quote: true)});");
            }
        }

        writer.AppendLine($"if ({ManagedExecutionRewriter.ManagedBlockBarrierRequested})");
        using (writer.Scope())
        {
            writer.AppendLine($"threadAtBlockBarrier[{LinearThreadIndex}] = true;");
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
                    ConvertedArrayEmitter.WriteElementMarshalLoop(writer, shape, shape.Cpu, $"{shape.Name}_allocatedPtrs");
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
