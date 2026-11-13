namespace Kassyi.GSharp.Compiler.Transpiler;

using static GSharpInternalNames.Kernel;
using static GSharpInternalNames.Launcher;
using static GSharpInternalNames.StateMachine;
using static GSharpInternalNames.Prelude;

/// <summary>
/// Writes the grid of a cooperative kernel in the CPU launcher (<see cref="HostLauncherTranspiler"/>): the blocks run
/// in phases that end at the grid sync, and every block keeps its contexts and its shared memory arena across them.
/// </summary>
internal static class CooperativeLauncherTranspiler
{
    // The grid runs in phases; every block keeps its contexts and its shared memory arena across them:
    // try { <contexts and arenas of the grid> #pragma omp parallel { while (<next phase>) { #pragma omp for <blocks to
    // their grid sync> #pragma omp single { <end the phase> } } } } catch (const std::bad_alloc&) { <report> }
    public static void WriteCooperativeGrid(string contextName, in HostLauncherTranspiler.KernelStep step,
        ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine(
            $"int32_t {ThreadsPerBlock} = (int32_t)({BlockDimX} * {BlockDimY} * {BlockDimZ});");
        context.Emitter.Writer.AppendLine("try");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine(
                $"std::vector<{contextName}> {GridContexts}((size_t){TotalBlocks} * {ThreadsPerBlock});");
            context.Emitter.Writer.AppendLine(
                $"std::vector<{SharedMemoryArena}> {BlockArenas}((size_t){TotalBlocks});");
            context.Emitter.Writer.AppendLine($"std::vector<uint8_t> {BlockStatus}((size_t){TotalBlocks});");
            context.Emitter.Writer.AppendLine(
                $"for (int64_t {BlockLoopIndex} = 0; {BlockLoopIndex} < {TotalBlocks}; {BlockLoopIndex}++)");
            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Writer.AppendLine($"{BlockArenas}[{BlockLoopIndex}].reset({SharedMemBytes});");
                HostLauncherTranspiler.WriteBlockContexts(contextName, ref context);
                context.Emitter.Writer.AppendLine(
                    $"for (int32_t {ThreadLoopIndex} = 0; {ThreadLoopIndex} < {ThreadsPerBlock}; {ThreadLoopIndex}++)");
                using (context.Emitter.Writer.Scope())
                {
                    context.Emitter.Writer.AppendLine($"{Contexts}[{ThreadLoopIndex}].{AllContexts} = {Contexts};");
                }
            }

            context.Emitter.Writer.AppendLine($"bool {NextGridPhase} = true;");
            context.Emitter.Writer.AppendLine("#pragma omp parallel");
            using (context.Emitter.Writer.Scope())
            {
                // [WHY] Only the omp single below changes the flag, between two barriers, so every OpenMP thread reads
                // the same value and meets the same worksharing constructs
                context.Emitter.Writer.AppendLine($"while ({NextGridPhase})");
                using (context.Emitter.Writer.Scope())
                {
                    WriteCooperativePhase(contextName, step, ref context);
                    context.Emitter.Writer.AppendLine("#pragma omp single");
                    using (context.Emitter.Writer.Scope())
                    {
                        context.Emitter.Writer.Append(
                            $"{NextGridPhase} = {EndGridPhase}(&{Error}, {BlockStatus}.data(), {TotalBlocks}, ");
                        context.Emitter.Writer.Append($"{GridDimX}, {GridDimY}, ");
                        CppStringLiteral.Append(context.Emitter.Writer, step.MessageName);
                        context.Emitter.Writer.AppendLine(");");
                    }
                }

                context.Emitter.Writer.AppendLine($"{BindSharedMemory}(nullptr);");
            }
        }

        context.Emitter.Writer.AppendLine("catch (const std::bad_alloc&)");
        using (context.Emitter.Writer.Scope())
        {
            HostLauncherTranspiler.WriteAllocationFailure(ref context);
        }
    }

    // #pragma omp for: every block runs until its threads reach the grid sync or finish
    private static void WriteCooperativePhase(string contextName, in HostLauncherTranspiler.KernelStep step,
        ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine("#pragma omp for schedule(dynamic)");
        context.Emitter.Writer.AppendLine(
            $"for (int64_t {BlockLoopIndex} = 0; {BlockLoopIndex} < {TotalBlocks}; {BlockLoopIndex}++)");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine("try");
            using (context.Emitter.Writer.Scope())
            {
                HostLauncherTranspiler.WriteBlockCoordinates(ref context);
                context.Emitter.Writer.AppendLine($"{BindSharedMemory}(&{BlockArenas}[{BlockLoopIndex}]);");
                HostLauncherTranspiler.WriteBlockContexts(contextName, ref context);
                // The threads that wait at the grid sync go on in this phase
                context.Emitter.Writer.AppendLine(
                    $"for (int32_t {ThreadLoopIndex} = 0; {ThreadLoopIndex} < {ThreadsPerBlock}; {ThreadLoopIndex}++)");
                using (context.Emitter.Writer.Scope())
                {
                    context.Emitter.Writer.AppendLine($"{Contexts}[{ThreadLoopIndex}].{YieldedForGridSync} = false;");
                }

                context.Emitter.Writer.AppendLine($"std::atomic<bool> {AnyActive}{{true}};");
                context.Emitter.Writer.AppendLine($"bool {GridSyncReached} = false;");
                HostLauncherTranspiler.WriteRoundDeclaration(step, ref context);
                // [WHY] A thread that reached the grid sync waits there while the other threads of the block go on to
                // it: they may need more steps (a shuffle or a block barrier before it). The block's part of the phase
                // ends when no thread can run. A thread that failed has finished; the phase still runs to the grid
                // sync, where the launch then ends (__gsharp_end_grid_phase), as the GPU lets the waiters leave its
                // grid barrier
                context.Emitter.Writer.AppendLine($"while ({AnyActive})");
                using (context.Emitter.Writer.Scope())
                {
                    context.Emitter.Writer.AppendLine($"{AnyActive} = false;");
                    HostLauncherTranspiler.WriteBlockLoop(step, declaresThreadsPerBlock: false, ref context);
                }

                context.Emitter.Writer.AppendLine(
                    $"{BlockStatus}[{BlockLoopIndex}] = {GridSyncReached} ? {BlockAtGridSync} : {BlockFinished};");
                // The grid sync of the GPU waits for thread 0 of every block
                context.Emitter.Writer.AppendLine($"if ({GridSyncReached} && {Contexts}[0].{State} == -1)");
                using (context.Emitter.Writer.Scope())
                {
                    context.Emitter.Writer.Append(
                        $"{ReportRepresentativeFinished}(&{Error}, {BlockX}, {BlockY}, {BlockZ}, ");
                    CppStringLiteral.Append(context.Emitter.Writer, step.MessageName);
                    context.Emitter.Writer.AppendLine(");");
                }
            }

            HostLauncherTranspiler.WriteStepAbortCatches(ref context);
        }
    }
}
