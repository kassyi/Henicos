namespace Kassyi.GSharp.Compiler.Marshaling;

using static GSharpInternalNames.Launcher;

/// <summary>
/// Emits the managed fallback of the CPU launcher, which runs the kernel's C# state machine thread by thread when the
/// native kernel library is not used (<c>CpuKernelLoader.ShouldUseManagedExecution</c>).
/// </summary>
/// <remarks>
/// The managed launcher has the shape of the native one (HostLauncherTranspiler), one method per generated loop:
/// WriteManagedGridLoop (grid phases) &gt; WriteManagedGridPhase (blocks) &gt; WriteManagedBlock &gt;
/// WriteManagedBarrierRounds (rounds between barriers) &gt; WriteManagedThreadStep (one thread).
/// <para>
/// An exception ends the step of its thread only: the thread has finished, the other threads of the launch run on, as
/// on the GPU, where the failing thread exits, and the launch throws the first exception at its end. A cooperative
/// launch ends at the grid sync after the exception, as the GPU lets the waiters leave its grid barrier.
/// </para>
/// </remarks>
internal static class ManagedFallbackEmitter
{
    // The pointer to the context array that an unsafe context's launch pins once for the whole grid
    private const string PinnedContexts = "__gsharp_pinned_contexts";

    private const string ExceptionDispatchInfo = "global::System.Runtime.ExceptionServices.ExceptionDispatchInfo";

    // The managed launcher's context slot for the thread currently being stepped.
    private const string LinearContext =
        $"{GSharpInternalNames.Launcher.Contexts}[{GSharpInternalNames.Launcher.LinearThreadIndex}]";

    // The same slot through the pinned pointer (unsafe contexts)
    private const string PinnedLinearContext =
        $"{PinnedContexts}[{GSharpInternalNames.Launcher.LinearThreadIndex}]";

    /// <summary>
    /// Emits <c>if (CpuKernelLoader.ShouldUseManagedExecution(dllPath)) { &lt;run the grid&gt;; return; }</c>, after the
    /// launcher has declared <c>dllPath</c>.
    /// </summary>
    public static void WriteManagedExecutionFallback(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine(
            $"if (global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.ShouldUseManagedExecution({DllPath}))");
        using (writer.Scope())
        {
            WriteManagedContextAllocation(writer, kernel);
            WriteManagedGridLoop(writer, kernel);
            AutoResultEmitter.WriteReturn(writer, kernel);
            if (!kernel.ReturnsValue)
            {
                writer.AppendLine("return;");
            }
        }
    }

    // The contexts of every thread of the grid, the shared memory of the grid and the barrier waits
    private static void WriteManagedContextAllocation(SourceWriter writer, in KernelHostModel kernel)
    {
        // The contexts of the grid live in one array, which bounds the threads of a launch on this path only (#1443)
        LaunchLimitEmitter.WriteManagedCounts(writer, kernel);
        writer.AppendLine($"int {ThreadsPerBlock} = (int)(blockDimX * blockDimY * blockDimZ);");
        writer.AppendLine($"{kernel.ContextName}[] {Contexts} = new {kernel.ContextName}[{ContextCount}];");
        // An unsafe context is a struct, so the array already holds one per thread
        if (!kernel.IsUnsafeContext)
        {
            writer.AppendLine($"for (int {ContextIndex} = 0; {ContextIndex} < {Contexts}.Length; {ContextIndex}++)");
            using (writer.Scope())
            {
                writer.AppendLine($"{Contexts}[{ContextIndex}] = new {kernel.ContextName}();");
            }
        }

        if (kernel.ReachesWarpShuffle)
        {
            // The frames of every thread get the same numbers, which tell the paths through device functions apart at a
            // warp collective (#1468)
            writer.AppendLine($"for (int {ContextIndex} = 0; {ContextIndex} < {Contexts}.Length; {ContextIndex}++)");
            using (writer.Scope())
            {
                writer.AppendLine(
                    $"{Contexts}[{ContextIndex}].{GSharpInternalNames.StateMachine.NumberWarpPaths}(0u);");
            }
        }

        // A cooperative kernel's blocks run in grid phases and keep their dynamic shared memory for the grid; the
        // blocks of any other kernel run to their end one after another and use one array in turn
        var cooperativeLiteral = kernel.IsCooperative ? "true" : "false";
        writer.AppendLine(
            $"{ManagedNames.Context}.InitializeGrid({TotalBlocks}, sharedMemBytes, {cooperativeLiteral});");

        // A thread that stopped at a block barrier waits there until every thread of its block that has not finished
        // waits too; in a cooperative kernel, a thread that reached the grid sync waits until the next grid phase (#664)
        writer.AppendLine($"bool[] {ThreadAtBlockBarrier} = new bool[{Contexts}.Length];");
        if (kernel.IsCooperative)
        {
            writer.AppendLine($"bool[] {ThreadAtGridSync} = new bool[{Contexts}.Length];");
        }
    }

    // [fixed (<context>* __gsharp_pinned_contexts = __gsharp_contexts)] { <grid loop> }
    private static void WriteManagedGridLoop(SourceWriter writer, in KernelHostModel kernel)
    {
        if (kernel.IsUnsafeContext)
        {
            writer.AppendLine($"fixed ({kernel.ContextName}* {PinnedContexts} = {Contexts})");
            using (writer.Scope())
            {
                WriteManagedGridLoopBody(writer, kernel);
            }
        }
        else
        {
            WriteManagedGridLoopBody(writer, kernel);
        }
    }

    // ExceptionDispatchInfo? __gsharp_first_failure = null; bool __gsharp_any_grid_active = true; try { while
    // (__gsharp_any_grid_active) { <grid phase> } } catch (Exception) when (<a thread failed>) { <throw the first> }
    // finally { EndGrid(); } __gsharp_first_failure?.Throw();
    private static void WriteManagedGridLoopBody(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine($"{ExceptionDispatchInfo}? {FirstFailure} = null;");
        writer.AppendLine($"bool {AnyGridActive} = true;");
        // The dynamic shared memory of the blocks lives for one grid (#544), also when a step throws
        writer.AppendLine("try");
        using (writer.Scope())
        {
            // Each pass runs every block until its threads reach Gpu.SyncGrid or finish: one grid phase (#664)
            writer.AppendLine($"while ({AnyGridActive})");
            using (writer.Scope())
            {
                WriteManagedGridPhase(writer, kernel);
            }
        }

        // A failure of the launch itself after a thread failed (a stall, a block that finished at a grid sync) does not
        // replace the first failure
        writer.AppendLine($"catch (global::System.Exception) when ({FirstFailure} is not null)");
        using (writer.Scope())
        {
            writer.AppendLine($"{FirstFailure}.Throw();");
        }

        writer.AppendLine("finally");
        using (writer.Scope())
        {
            writer.AppendLine($"{ManagedNames.Context}.EndGrid();");
        }

        writer.AppendLine($"{FirstFailure}?.Throw();");
    }

    // One grid phase: every block in turn, then (cooperative) the check that no block finished while others wait
    private static void WriteManagedGridPhase(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine($"{AnyGridActive} = false;");
        if (kernel.IsCooperative)
        {
            writer.AppendLine($"int {FirstFinishedBlock} = -1;");
            writer.AppendLine($"bool {AnyBlockAtGridSync} = false;");
        }

        writer.AppendLine($"for (int {BlockLoopIndex} = 0; {BlockLoopIndex} < {TotalBlocks}; {BlockLoopIndex}++)");
        using (writer.Scope())
        {
            WriteManagedBlock(writer, kernel);
        }

        if (kernel.IsCooperative)
        {
            // After a thread failed, the launch ends at the grid sync
            writer.AppendLine($"if ({FirstFailure} is not null)");
            using (writer.Scope())
            {
                writer.AppendLine("break;");
            }

            // A block that finished while others wait never arrives at the grid sync of the GPU
            writer.AppendLine($"if ({AnyBlockAtGridSync} && {FirstFinishedBlock} >= 0)");
            using (writer.Scope())
            {
                var messageName = SymbolDisplay.FormatLiteral(kernel.MessageName, quote: true);
                writer.Append($"throw {ManagedNames.Context}.BlockFinishedAtGridSync(");
                writer.AppendLine($"{messageName}, {FirstFinishedBlock}, gridDimX, gridDimY);");
            }
        }
    }

    // One block of the managed launcher, stepped until its threads reach Gpu.SyncGrid or finish
    private static void WriteManagedBlock(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine($"int {BlockX} = {BlockLoopIndex} % (int)gridDimX;");
        writer.AppendLine($"int {BlockY} = ({BlockLoopIndex} / (int)gridDimX) % (int)gridDimY;");
        writer.AppendLine($"int {BlockZ} = {BlockLoopIndex} / (int)(gridDimX * gridDimY);");

        writer.AppendLine($"{ManagedNames.Context}.IsActive = true;");
        writer.AppendLine("try");
        using (writer.Scope())
        {
            WriteManagedBlockSetup(writer);
            if (kernel.IsCooperative && kernel.ReachesWarpShuffle)
            {
                WriteManagedExitedLanes(writer, kernel);
            }

            if (kernel.IsCooperative)
            {
                // The threads that wait at the grid sync go on in this phase
                writer.AppendLine(
                    $"global::System.Array.Clear({ThreadAtGridSync}, {BlockLoopIndex} * {ThreadsPerBlock}, {ThreadsPerBlock});");
                writer.AppendLine($"bool {BlockAtGridSyncFlag} = false;");
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
            writer.AppendLine($"{ManagedNames.Context}.IsActive = false;");
        }
    }

    // A cooperative kernel's block runs again in the next grid phase, after the warp collectives of another block: the
    // threads of the block that have exited stay out of its warp collectives
    // for (<threads of the block>) { if (<exited>) { ManagedGpuContext.MarkWarpLaneExited(<thread>); } }
    private static void WriteManagedExitedLanes(SourceWriter writer, in KernelHostModel kernel)
    {
        var contextSlot = kernel.IsUnsafeContext ? PinnedContexts : Contexts;
        writer.AppendLine($"for (int {ThreadLoopIndex} = 0; {ThreadLoopIndex} < {ThreadsPerBlock}; {ThreadLoopIndex}++)");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"if ({contextSlot}[{BlockLoopIndex} * {ThreadsPerBlock} + {ThreadLoopIndex}].{GSharpInternalNames.StateMachine.State} == -1)");
            using (writer.Scope())
            {
                writer.AppendLine($"{ManagedNames.Context}.MarkWarpLaneExited({ThreadLoopIndex});");
            }
        }
    }

    // The block's coordinates and dimensions in ManagedGpuContext, and a fresh warp exchange
    private static void WriteManagedBlockSetup(SourceWriter writer)
    {
        writer.AppendLine($"{ManagedNames.Context}.{ManagedNames.BlockIdxXName} = {BlockX};");
        writer.AppendLine($"{ManagedNames.Context}.{ManagedNames.BlockIdxYName} = {BlockY};");
        writer.AppendLine($"{ManagedNames.Context}.{ManagedNames.BlockIdxZName} = {BlockZ};");
        writer.AppendLine($"{ManagedNames.Context}.BlockDimX = (int)blockDimX;");
        writer.AppendLine($"{ManagedNames.Context}.BlockDimY = (int)blockDimY;");
        writer.AppendLine($"{ManagedNames.Context}.BlockDimZ = (int)blockDimZ;");
        writer.AppendLine($"{ManagedNames.Context}.GridDimX = (int)gridDimX;");
        writer.AppendLine($"{ManagedNames.Context}.GridDimY = (int)gridDimY;");
        writer.AppendLine($"{ManagedNames.Context}.GridDimZ = (int)gridDimZ;");
        writer.AppendLine($"{ManagedNames.Context}.ResetWarpExchange();");
    }

    // [WHY] A thread that reached a barrier waits there while the other threads of the block go on to it: they may need
    // more steps (a shuffle before it). When no thread can run, the threads at a block barrier pass it together; the
    // block's part of the phase ends when no thread can run and none waits at a block barrier.
    // bool anyBlockActive = true; while (anyBlockActive) { anyBlockActive = false; <threads> <block barrier release> }
    private static void WriteManagedBarrierRounds(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine($"bool {AnyBlockActive} = true;");
        if (kernel.ReachesWarpShuffle)
        {
            writer.AppendLine($"int {Round} = 0;");
        }

        writer.AppendLine($"while ({AnyBlockActive})");
        using (writer.Scope())
        {
            writer.AppendLine($"{AnyBlockActive} = false;");
            if (kernel.ReachesWarpShuffle)
            {
                writer.AppendLine($"{Round}++;");
                writer.AppendLine($"bool {Stepped} = false;");
            }

            writer.AppendLine($"for (int {ThreadLoopIndex} = 0; {ThreadLoopIndex} < {ThreadsPerBlock}; {ThreadLoopIndex}++)");
            using (writer.Scope())
            {
                WriteManagedThreadStep(writer, kernel);
            }

            if (kernel.ReachesWarpShuffle)
            {
                // [WHY] A thread that waits at a warp collective counts as one that can go on, so a round in which none of
                // them ran means that they wait for lanes that never reach the same call (#1468)
                writer.AppendLine($"if ({AnyBlockActive} && !{Stepped})");
                using (writer.Scope())
                {
                    writer.AppendLine($"throw {ManagedNames.Context}.WarpStall({ThreadsPerBlock});");
                }
            }

            WriteManagedBlockBarrierRelease(writer);
        }
    }

    // if (!__gsharp_any_block_active) { for (<threads of the block>) { if (<waits at the block barrier>) { <release it>; __gsharp_any_block_active = true; } } }
    private static void WriteManagedBlockBarrierRelease(SourceWriter writer)
    {
        writer.AppendLine($"if (!{AnyBlockActive})");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"for (int {LinearThreadIndex} = {BlockLoopIndex} * {ThreadsPerBlock}; {LinearThreadIndex} < ({BlockLoopIndex} + 1) * {ThreadsPerBlock}; {LinearThreadIndex}++)");
            using (writer.Scope())
            {
                writer.AppendLine($"if ({ThreadAtBlockBarrier}[{LinearThreadIndex}])");
                using (writer.Scope())
                {
                    writer.AppendLine($"{ThreadAtBlockBarrier}[{LinearThreadIndex}] = false;");
                    writer.AppendLine($"{AnyBlockActive} = true;");
                }
            }
        }
    }

    // Cooperative: whether the block waits at the grid sync, with the check of its thread 0, or finished
    private static void WriteManagedBlockGridSyncStatus(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine($"if ({BlockAtGridSyncFlag})");
        using (writer.Scope())
        {
            // The grid sync of the GPU waits for thread 0 of every block. After a thread failed, the launch ends at the
            // grid sync instead (the other blocks still run to it)
            var contextSlot = kernel.IsUnsafeContext ? PinnedContexts : Contexts;
            var representative = $"{contextSlot}[{BlockLoopIndex} * {ThreadsPerBlock}]";
            writer.AppendLine(
                $"if ({FirstFailure} is null && {representative}.{GSharpInternalNames.StateMachine.State} == -1)");
            using (writer.Scope())
            {
                var messageName = SymbolDisplay.FormatLiteral(kernel.MessageName, quote: true);
                writer.Append($"throw {ManagedNames.Context}.RepresentativeFinishedAtGridSync(");
                writer.AppendLine($"{messageName}, {BlockX}, {BlockY}, {BlockZ});");
            }

            writer.AppendLine($"{AnyBlockAtGridSync} = true;");
        }

        writer.AppendLine($"else if ({FirstFinishedBlock} < 0)");
        using (writer.Scope())
        {
            writer.AppendLine($"{FirstFinishedBlock} = {BlockLoopIndex};");
        }
    }

    // One step of one thread: from where it stopped to its next barrier, grid sync or end
    private static void WriteManagedThreadStep(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine($"int {ThreadX} = {ThreadLoopIndex} % (int)blockDimX;");
        writer.AppendLine($"int {ThreadY} = ({ThreadLoopIndex} / (int)blockDimX) % (int)blockDimY;");
        writer.AppendLine($"int {ThreadZ} = {ThreadLoopIndex} / (int)(blockDimX * blockDimY);");
        writer.AppendLine($"{ManagedNames.Context}.{ManagedNames.ThreadIdxXName} = {ThreadX};");
        writer.AppendLine($"{ManagedNames.Context}.{ManagedNames.ThreadIdxYName} = {ThreadY};");
        writer.AppendLine($"{ManagedNames.Context}.{ManagedNames.ThreadIdxZName} = {ThreadZ};");

        writer.AppendLine($"int {LinearThreadIndex} = {BlockLoopIndex} * {ThreadsPerBlock} + {ThreadLoopIndex};");
        // A thread that waits at a block barrier or at the grid sync does not run
        var runnable = GetRunnableCondition(kernel);
        writer.AppendLine($"if ({runnable})");
        using (writer.Scope())
        {
            writer.AppendLine($"{AnyGridActive} = true;");
            // A thread that waits at a warp collective runs once the lanes of its mask have met there (#1468)
            if (kernel.ReachesWarpShuffle)
            {
                writer.AppendLine($"if ({ManagedNames.Context}.WarpCanRun({ThreadLoopIndex}, {Round}))");
            }

            using (kernel.ReachesWarpShuffle ? writer.Scope() : default)
            {
                writer.AppendLineIf(kernel.ReachesWarpShuffle, $"{Stepped} = true;");
                writer.AppendLine("try");
                using (writer.Scope())
                {
                    // The state machines of the kernel and of the device functions it calls request a grid sync (#665)
                    // and report a block barrier through thread-static flags; the launcher steps one GPU thread at a
                    // time on this host thread
                    writer.AppendLine($"{ManagedNames.Context}.{ManagedNames.GridSyncRequestedName} = false;");
                    writer.AppendLine($"{ManagedNames.Context}.{ManagedNames.BlockBarrierRequestedName} = false;");
                    WriteStateMachineCall(writer, kernel);
                    WriteManagedWaits(writer, kernel);
                }

                WriteManagedThreadFailureCatch(writer, kernel);
                if (kernel.ReachesWarpShuffle)
                {
                    WriteManagedExitCheck(writer, kernel);
                }
            }

            // The next round runs while a thread can go on: one that stopped at a shuffle runs again, one at a barrier
            // waits
            writer.AppendLine($"if ({runnable})");
            using (writer.Scope())
            {
                writer.AppendLine($"{AnyBlockActive} = true;");
            }
        }
    }

    // The exception ends the step of the thread, which has finished and waits nowhere; the first one is thrown at the
    // end of the launch. An exception that left a generic struct constructor left its type-key salt set, which the next
    // thread would use for its shared memory sites.
    // catch (Exception __gsharp_failure) { __gsharp_first_failure ??= ExceptionDispatchInfo.Capture(__gsharp_failure);
    // ManagedGpuContext.ResetTypeKeyScope(); <context>.__gsharp_state = -1; <its waits> = false; }
    private static void WriteManagedThreadFailureCatch(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine($"catch (global::System.Exception {Failure})");
        using (writer.Scope())
        {
            writer.AppendLine($"{FirstFailure} ??= {ExceptionDispatchInfo}.Capture({Failure});");
            writer.AppendLine($"{ManagedNames.Context}.ResetTypeKeyScope();");
            var contextSlot = kernel.IsUnsafeContext ? PinnedLinearContext : LinearContext;
            writer.AppendLine($"{contextSlot}.{GSharpInternalNames.StateMachine.State} = -1;");
            writer.AppendLine($"{ThreadAtBlockBarrier}[{LinearThreadIndex}] = false;");
            if (kernel.IsCooperative)
            {
                writer.AppendLine($"{ThreadAtGridSync}[{LinearThreadIndex}] = false;");
            }
        }
    }

    // <context>.__gsharp_state != -1 && !__gsharp_thread_at_block_barrier[<thread>]
    private const string RunnableCondition =
        $"{LinearContext}.{GSharpInternalNames.StateMachine.State} != -1 && !{ThreadAtBlockBarrier}[{LinearThreadIndex}]";

    // The same through the pinned pointer (unsafe contexts)
    private const string PinnedRunnableCondition =
        $"{PinnedLinearContext}.{GSharpInternalNames.StateMachine.State} != -1 && !{ThreadAtBlockBarrier}[{LinearThreadIndex}]";

    // ... && !__gsharp_thread_at_grid_sync[<thread>]
    private const string CooperativeRunnableCondition = $"{RunnableCondition} && !{ThreadAtGridSync}[{LinearThreadIndex}]";

    private const string PinnedCooperativeRunnableCondition =
        $"{PinnedRunnableCondition} && !{ThreadAtGridSync}[{LinearThreadIndex}]";

    // Whether the thread being stepped can run: it has not finished and does not wait
    private static string GetRunnableCondition(in KernelHostModel kernel) =>
        (kernel.IsCooperative, kernel.IsUnsafeContext) switch
        {
            (true, true) => PinnedCooperativeRunnableCondition,
            (true, false) => CooperativeRunnableCondition,
            (false, true) => PinnedRunnableCondition,
            (false, false) => RunnableCondition
        };

    // A thread that has exited no longer takes part in a warp collective
    // if (<context>.__gsharp_state == -1) { ManagedGpuContext.MarkWarpLaneExited(<thread>); }
    private static void WriteManagedExitCheck(SourceWriter writer, in KernelHostModel kernel)
    {
        var contextSlot = kernel.IsUnsafeContext ? PinnedLinearContext : LinearContext;
        writer.AppendLine($"if ({contextSlot}.{GSharpInternalNames.StateMachine.State} == -1)");
        using (writer.Scope())
        {
            writer.AppendLine($"{ManagedNames.Context}.MarkWarpLaneExited({ThreadLoopIndex});");
        }
    }

    // <state machine>(<arguments>, __gsharp_contexts[<thread>][, ref __gsharp_auto_result[<thread>]]);
    private static void WriteStateMachineCall(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.Append($"{kernel.StateMachineName}(");
        foreach (var p in kernel.Parameters)
        {
            // An in parameter that the kernel receives by address stays the caller's variable
            writer.Append(p.RefKind switch
            {
                RefKind.Ref or RefKind.Out => "ref ",
                RefKind.RefReadOnlyParameter => "in ",
                _ when p.IsInReference => "in ",
                _ => string.Empty
            });
            writer.Append(p.Identifier);
            writer.Append(", ");
        }

        if (kernel.IsUnsafeContext)
        {
            writer.Append($"&{PinnedLinearContext}");
        }
        else
        {
            writer.Append(LinearContext);
        }

        if (kernel.ReturnsValue)
        {
            writer.Append($", ref {GSharpInternalNames.ReturnValue.Buffer}[{LinearThreadIndex}]");
        }

        writer.AppendLine(");");
    }

    // After a step: a thread that reached the grid sync (cooperative: waits; otherwise an error) or a block barrier waits
    private static void WriteManagedWaits(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine($"if ({ManagedNames.Context}.{ManagedNames.GridSyncRequestedName})");
        using (writer.Scope())
        {
            if (kernel.IsCooperative)
            {
                writer.AppendLine($"{ThreadAtGridSync}[{LinearThreadIndex}] = true;");
                writer.AppendLine($"{BlockAtGridSyncFlag} = true;");
            }
            else
            {
                // Undefined on the GPU: grid.sync() in a launch that is not cooperative
                var messageName = SymbolDisplay.FormatLiteral(kernel.MessageName, quote: true);
                writer.Append($"throw {ManagedNames.Context}.GridSyncInKernelThatIsNotCooperative(");
                writer.AppendLine($"{messageName});");
            }
        }

        writer.AppendLine($"if ({ManagedNames.Context}.{ManagedNames.BlockBarrierRequestedName})");
        using (writer.Scope())
        {
            writer.AppendLine($"{ThreadAtBlockBarrier}[{LinearThreadIndex}] = true;");
        }
    }
}
