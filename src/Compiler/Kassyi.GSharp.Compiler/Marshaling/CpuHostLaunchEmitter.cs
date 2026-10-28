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
                    writer.Append($"{p.TypeName}{(p.IsByRef ? "*" : "")} {p.Identifier}");
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

    private static void WriteCpuFixedBlocksAndInvoke(SourceWriter writer, in KernelHostModel kernel)
    {
        var hasFixed = false;

        foreach (var shape in kernel.Parameters)
        {
            if (shape is { IsArray: true, ElementTypeName: { } elementType })
            {
                writer.AppendLine($"fixed ({elementType}* p_{shape.Name} = {shape.SpanExpression})");
                hasFixed = true;
            }
            else if (shape.IsByRef)
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

        WrapperEmitter.WriteAutoResultReturn(writer, kernel);
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

                if (shape.IsArray)
                {
                    writer.Append(
                        $"new global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy {{ Data = (IntPtr)p_{shape.Name}, Length = {shape.LengthExpression} }}");
                }
                else if (shape.IsByRef)
                {
                    writer.Append($"p_{shape.Name}");
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
