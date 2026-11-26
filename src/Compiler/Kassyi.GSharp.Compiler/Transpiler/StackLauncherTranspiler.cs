namespace Kassyi.GSharp.Compiler.Transpiler;

using static GSharpInternalNames.Launcher;
using static GSharpInternalNames.NativeStack;
using static GSharpInternalNames.Prelude;
using static GSharpInternalNames.StateMachine;

/// <summary>
/// Emits the part of the CPU launcher that runs the GPU threads of a block on native stacks of their own
/// (<c>GSharpNativeBarrierModel=Stack</c>); <see cref="HostLauncherTranspiler"/> emits the rest.
/// </summary>
internal static class StackLauncherTranspiler
{
    // Runs each block's GPU threads on their own native stacks while keeping the OpenMP thread's stack pool
    // for the launch
    public static void WriteIndependentBlocks(in HostLauncherTranspiler.KernelStep step, ref TranspileContext context)
    {
        context.Emitter.Writer.Append($"auto {Body} = [&]() ");
        using (context.Emitter.Writer.Braces())
        {
            HostLauncherTranspiler.WriteDirectKernelCall(step, includeContext: false, ref context);
            context.Emitter.Writer.Append(';');
        }
        context.Emitter.Writer.AppendLine(";");

        context.Emitter.Writer.AppendLine($"++{LaunchCount};");
        context.Emitter.Writer.AppendLine($"const auto {Fp} = {CaptureFp}();");
        context.Emitter.Writer.Append(
            $"#pragma omp parallel num_threads({ThreadLimit}((uint64_t){BlockDimX} * {BlockDimY} * {BlockDimZ}, ");
        context.Emitter.Writer.AppendLine($"{TotalBlocks}))");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine($"{PoolType} {Stacks}({Fp});");
            context.Emitter.Writer.AppendLine("#pragma omp for schedule(dynamic)");
            context.Emitter.Writer.AppendLine(
                $"for (int64_t {BlockLoopIndex} = 0; {BlockLoopIndex} < {TotalBlocks}; {BlockLoopIndex}++)");
            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Writer.AppendLine("try");
                using (context.Emitter.Writer.Scope())
                {
                    HostLauncherTranspiler.WriteBlockCoordinates(ref context);
                    context.Emitter.Writer.AppendLine($"{ResetSharedMemory}({SharedMemBytes});");
                    context.Emitter.Writer.AppendLine($"{ActiveErrorSlot} = &{Error};");
                    // The threads of a kernel that reaches a warp collective carry the warp state of the state machine
                    var (blockType, threadType) = step.ReachesWarpShuffle
                        ? (GSharpInternalNames.StackWarp.BlockType, GSharpInternalNames.StackWarp.ThreadType)
                        : (BlockType, ThreadType);
                    context.Emitter.Writer.AppendLine(
                        $"{blockType} {Block}({Stacks}, {BlockDimX} * {BlockDimY} * {BlockDimZ}, {Body});");
                    context.Emitter.Writer.AppendLine(
                        $"{threadType}* {Contexts} = {Block}.threads.data();");
                    context.Emitter.Writer.AppendLine($"std::atomic<bool> {AnyActive}{{true}};");
                    HostLauncherTranspiler.WriteRoundDeclaration(step, ref context);
                    context.Emitter.Writer.AppendLine($"while ({AnyActive})");
                    using (context.Emitter.Writer.Scope())
                    {
                        context.Emitter.Writer.AppendLine($"{AnyActive} = false;");
                        HostLauncherTranspiler.WriteBlockLoop(step, declaresThreadsPerBlock: true, ref context);
                    }
                }

                HostLauncherTranspiler.WriteStepAbortCatches(ref context);
            }
        }
    }
}
