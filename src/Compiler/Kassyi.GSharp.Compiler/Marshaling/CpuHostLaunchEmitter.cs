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

    private static void WriteManagedExecutionFallback(SourceWriter writer, in KernelHostModel kernel, string assemblyName)
    {
        var safeAssemblyName = AotHostNames.ToSafeIdentifier(assemblyName);
        var contextName = kernel.ContextName;

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

            // A cooperative kernel's blocks run in grid phases and keep their dynamic shared memory for the grid; the
            // blocks of any other kernel run to their end one after another and use one array in turn
            writer.AppendLine(
                $"global::Kassyi.GSharp.Core.ManagedGpuContext.InitializeGrid({GSharpInternalNames.Launcher.TotalBlocks}, sharedMemBytes, {(kernel.IsCooperative ? "true" : "false")});");

            // A thread that stopped at a block barrier waits there until every thread of its block that has not finished
            // waits too; in a cooperative kernel, a thread that reached the grid sync waits until the next grid phase (#664)
            writer.AppendLine($"var threadAtBlockBarrier = new bool[{GSharpInternalNames.Launcher.Contexts}.Length];");
            if (kernel.IsCooperative)
            {
                writer.AppendLine($"var threadAtGridSync = new bool[{GSharpInternalNames.Launcher.Contexts}.Length];");
            }

            writer.AppendLine("bool anyGridActive = true;");
            // The dynamic shared memory of the blocks lives for one grid (#544), also when a step throws
            writer.AppendLine("try");
            using (writer.Scope())
            {
                // Each pass runs every block until its threads reach Gpu.SyncGrid or finish: one grid phase (#664)
                writer.AppendLine("while (anyGridActive)");
                using (writer.Scope())
                {
                    writer.AppendLine("anyGridActive = false;");
                    if (kernel.IsCooperative)
                    {
                        writer.AppendLine("int firstFinishedBlock = -1;");
                        writer.AppendLine("bool anyBlockAtGridSync = false;");
                    }

                    writer.AppendLine(
                        $"for (int {GSharpInternalNames.Launcher.BlockLoopIndex} = 0; {GSharpInternalNames.Launcher.BlockLoopIndex} < {GSharpInternalNames.Launcher.TotalBlocks}; {GSharpInternalNames.Launcher.BlockLoopIndex}++)");
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
            }

            writer.AppendLine("finally");
            using (writer.Scope())
            {
                writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.EndGrid();");
            }

            WrapperEmitter.WriteAutoResultReturn(writer, kernel);
            if (!kernel.ReturnsValue)
            {
                writer.AppendLine("return;");
            }
        }
    }

    // One block of the managed launcher, stepped until its threads reach Gpu.SyncGrid or finish
    private static void WriteManagedBlock(SourceWriter writer, in KernelHostModel kernel)
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

            if (kernel.IsCooperative)
            {
                // The threads that wait at the grid sync go on in this phase
                writer.AppendLine(
                    $"global::System.Array.Clear(threadAtGridSync, {GSharpInternalNames.Launcher.BlockLoopIndex} * {GSharpInternalNames.Launcher.ThreadsPerBlock}, {GSharpInternalNames.Launcher.ThreadsPerBlock});");
                writer.AppendLine("bool blockAtGridSync = false;");
            }

            // [WHY] A thread that reached a barrier waits there while the other threads of the block go on to it: they
            // may need more steps (a shuffle before it). When no thread can run, the threads at a block barrier pass it
            // together; the block's part of the phase ends when no thread can run and none waits at a block barrier
            writer.AppendLine("bool anyBlockActive = true;");
            writer.AppendLine("while (anyBlockActive)");
            using (writer.Scope())
            {
                writer.AppendLine("anyBlockActive = false;");
                writer.AppendLine(
                    $"for (int {GSharpInternalNames.Launcher.ThreadLoopIndex} = 0; {GSharpInternalNames.Launcher.ThreadLoopIndex} < {GSharpInternalNames.Launcher.ThreadsPerBlock}; {GSharpInternalNames.Launcher.ThreadLoopIndex}++)");
                using (writer.Scope())
                {
                    WriteManagedThreadStep(writer, kernel);
                }

                writer.AppendLine("if (!anyBlockActive)");
                using (writer.Scope())
                {
                    writer.AppendLine(
                        $"for (int {GSharpInternalNames.Launcher.LinearThreadIndex} = {GSharpInternalNames.Launcher.BlockLoopIndex} * {GSharpInternalNames.Launcher.ThreadsPerBlock}; {GSharpInternalNames.Launcher.LinearThreadIndex} < ({GSharpInternalNames.Launcher.BlockLoopIndex} + 1) * {GSharpInternalNames.Launcher.ThreadsPerBlock}; {GSharpInternalNames.Launcher.LinearThreadIndex}++)");
                    using (writer.Scope())
                    {
                        writer.AppendLine($"if (threadAtBlockBarrier[{GSharpInternalNames.Launcher.LinearThreadIndex}])");
                        using (writer.Scope())
                        {
                            writer.AppendLine($"threadAtBlockBarrier[{GSharpInternalNames.Launcher.LinearThreadIndex}] = false;");
                            writer.AppendLine("anyBlockActive = true;");
                        }
                    }
                }
            }

            if (kernel.IsCooperative)
            {
                writer.AppendLine("if (blockAtGridSync)");
                using (writer.Scope())
                {
                    // The grid sync of the GPU waits for thread 0 of every block
                    writer.AppendLine(
                        $"if ({GSharpInternalNames.Launcher.Contexts}[{GSharpInternalNames.Launcher.BlockLoopIndex} * {GSharpInternalNames.Launcher.ThreadsPerBlock}].{GSharpInternalNames.StateMachine.State} == -1)");
                    using (writer.Scope())
                    {
                        writer.AppendLine(
                            $"throw global::Kassyi.GSharp.Core.ManagedGpuContext.RepresentativeFinishedAtGridSync({SymbolDisplay.FormatLiteral(kernel.MessageName, quote: true)}, {GSharpInternalNames.Launcher.BlockX}, {GSharpInternalNames.Launcher.BlockY}, {GSharpInternalNames.Launcher.BlockZ});");
                    }

                    writer.AppendLine("anyBlockAtGridSync = true;");
                }

                writer.AppendLine("else if (firstFinishedBlock < 0)");
                using (writer.Scope())
                {
                    writer.AppendLine($"firstFinishedBlock = {GSharpInternalNames.Launcher.BlockLoopIndex};");
                }
            }
        }

        writer.AppendLine("finally");
        using (writer.Scope())
        {
            writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.IsActive = false;");
        }
    }

    // One step of one thread: from where it stopped to its next barrier, grid sync or end
    private static void WriteManagedThreadStep(SourceWriter writer, in KernelHostModel kernel)
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
        // A thread that waits at a block barrier or at the grid sync does not run
        var runnable = kernel.IsCooperative
            ? $"{LinearContext}.{GSharpInternalNames.StateMachine.State} != -1 && !threadAtBlockBarrier[{GSharpInternalNames.Launcher.LinearThreadIndex}] && !threadAtGridSync[{GSharpInternalNames.Launcher.LinearThreadIndex}]"
            : $"{LinearContext}.{GSharpInternalNames.StateMachine.State} != -1 && !threadAtBlockBarrier[{GSharpInternalNames.Launcher.LinearThreadIndex}]";
        writer.AppendLine($"if ({runnable})");
        using (writer.Scope())
        {
            writer.AppendLine("anyGridActive = true;");
            // The state machines of the kernel and of the device functions it calls request a grid sync (#665) and report
            // a block barrier through thread-static flags; the launcher steps one GPU thread at a time on this host thread
            writer.AppendLine($"{ManagedExecutionRewriter.ManagedGridSyncRequested} = false;");
            writer.AppendLine($"{ManagedExecutionRewriter.ManagedBlockBarrierRequested} = false;");

            writer.Append($"{kernel.StateMachineName}(");
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
                if (kernel.IsCooperative)
                {
                    writer.AppendLine($"threadAtGridSync[{GSharpInternalNames.Launcher.LinearThreadIndex}] = true;");
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
                writer.AppendLine($"threadAtBlockBarrier[{GSharpInternalNames.Launcher.LinearThreadIndex}] = true;");
            }

            // The next round runs while a thread can go on: one that stopped at a shuffle runs again, one at a barrier
            // waits
            writer.AppendLine($"if ({runnable})");
            using (writer.Scope())
            {
                writer.AppendLine("anyBlockActive = true;");
            }
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
