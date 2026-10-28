namespace Kassyi.GSharp.Compiler.Transpiler;

using Kassyi.GSharp.Compiler.Marshaling;
using static GSharpInternalNames.Kernel;
using static GSharpInternalNames.Launcher;
using static GSharpInternalNames.ReturnValue;
using static GSharpInternalNames.StateMachine;
using static GSharpInternalNames.Prelude;

/// <summary>
/// Emits CPU / OpenMP multi-threaded execution launchers for host execution.
/// </summary>
/// <remarks>
/// <para>
/// Each block starts by resetting the shared memory arena of the OpenMP thread that runs it with the launch's
/// <c>sharedMemBytes</c> (<c>__gsharp_reset_smem</c>); the kernel and its device functions place their
/// <c>SharedMemory&lt;T&gt;</c> declarations in it, also those that a state machine keeps in its context (#544).
/// </para>
/// <para>
/// A cooperative kernel with a state machine runs the grid in phases instead (#664): every block has its own contexts and
/// arena for the whole launch, each phase runs every block until each of its threads has reached <c>Gpu.SyncGrid</c> or
/// finished (a thread that arrives first waits there for the others), and
/// the implicit barrier of the OpenMP loop over the blocks is the grid barrier. A block or a representative thread that
/// finishes while the others wait is an error, and so is a grid sync in a kernel that is not cooperative: the GPU never
/// completes such a grid sync, or leaves it undefined.
/// </para>
/// </remarks>
internal static class HostLauncherTranspiler
{
    public static void WriteCpuLauncher(in MethodEmissionPlan plan,
        IReadOnlyList<VariableDeclaratorSyntax>? contextFields, ref TranspileContext context)
    {
        var hasContext = contextFields != null;
        var method = plan.Declaration;
        var methodSymbol = plan.Symbol;
        if (methodSymbol == null)
        {
            return;
        }

        WriteLauncherSignature(in plan, methodSymbol, ref context);
        var step = new KernelStep(
            method,
            GlobalCppNames.GetKernelName(methodSymbol),
            hasContext,
            plan.HasAutoResult,
            hasContext && methodSymbol.IsCooperativeKernel(),
            KernelHostNames.GetMessageName(methodSymbol)
        );
        WriteLauncherBody(GlobalCppNames.GetKernelDerivedName(ContextPrefix, methodSymbol), step, ref context);
    }

    // extern "C" __declspec(dllexport) void <launch export>(<launch dimensions>, <sharedMemBytes>, <kernel parameters>[, <result>* __restrict__ __gsharp_auto_result])
    private static void WriteLauncherSignature(in MethodEmissionPlan plan, IMethodSymbol methodSymbol,
        ref TranspileContext context)
    {
        var method = plan.Declaration;
        context.Emitter.Writer.AppendLine();
        context.Emitter.Writer.Append("extern \"C\" __declspec(dllexport) void");
        context.Emitter.Writer.Append(' ')
            .Append(GlobalCppNames.GetKernelDerivedName(LaunchExportPrefix, methodSymbol))
            .Append('(');

        // Launch dimensions
        context.Emitter.Writer.Append(
            $"uint32_t {GridDimX}, uint32_t {GridDimY}, uint32_t {GridDimZ}, ");
        context.Emitter.Writer.Append(
            $"uint32_t {BlockDimX}, uint32_t {BlockDimY}, uint32_t {BlockDimZ}, uint32_t {SharedMemBytes}");

        if (method.ParameterList.Parameters.Count > 0)
        {
            context.Emitter.Writer.Append(", ");
        }

        MethodSignatureTranspiler.WriteParameters(method.ParameterList.Parameters, false, ref context);

        if (plan.HasAutoResult)
        {
            context.Emitter.Writer.Append(", ");
            context.Emitter.Dialect.AppendType(ref context, methodSymbol.ReturnType);
            context.Emitter.Writer.Append($"* __restrict__ {Buffer}");
        }

        context.Emitter.Writer.AppendLine(")");
    }

    // { <error slot> <the grid: cooperative phases or independent blocks> <report the error to the host> }
    private static void WriteLauncherBody(string contextName, in KernelStep step, ref TranspileContext context)
    {
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine($"{HostLastError}.Reset();");
            context.Emitter.Writer.AppendLine($"GSharpErrorSlot {Error};");
            context.Emitter.Writer.AppendLine(
                $"int32_t {TotalBlocks} = (int32_t)({GridDimX} * {GridDimY} * {GridDimZ});");
            if (step.IsCooperative)
            {
                WriteCooperativeGrid(contextName, step, ref context);
            }
            else
            {
                WriteIndependentBlocks(contextName, step, ref context);
            }

            context.Emitter.Writer.AppendLine($"if ({Error}.HasError()) {{");
            context.Emitter.Writer.AppendLine(
                $"{HostLastError}.Report(static_cast<GSharpErrorCode>({Error}.code.load(std::memory_order_acquire)), {Error}.message);");
            context.Emitter.Writer.AppendLine("}");
        }
    }

    // Every block runs to its end on the OpenMP thread that takes it, with contexts of its own and the thread's arena
    private static void WriteIndependentBlocks(string contextName, in KernelStep step, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine("#pragma omp parallel for schedule(dynamic)");
        context.Emitter.Writer.AppendLine(
            $"for (int32_t {BlockLoopIndex} = 0; {BlockLoopIndex} < {TotalBlocks}; {BlockLoopIndex}++)");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine($"if ({Error}.HasError()) continue;");
            // The whole block runs in a try inside the iteration: a step that cannot go on, or memory that cannot be
            // allocated (also the contexts of a large block), ends the block there and never leaves the OpenMP loop
            context.Emitter.Writer.AppendLine("try");
            using (context.Emitter.Writer.Scope())
            {
                WriteBlockCoordinates(ref context);
                context.Emitter.Writer.AppendLine($"{ResetSharedMemory}({SharedMemBytes});");
                if (step.HasContext)
                {
                    context.Emitter.Writer.AppendLine(
                        $"std::vector<{contextName}> {Contexts}({BlockDimX} * {BlockDimY} * {BlockDimZ});");

                    context.Emitter.Writer.AppendLine($"for (auto& {ContextItem} : {Contexts}) {{");
                    context.Emitter.Writer.AppendLine($"{ContextItem}.{State} = 0;");
                    context.Emitter.Writer.AppendLine($"{ContextItem}.{AllContexts} = {Contexts}.data();");

                    context.Emitter.Writer.AppendLine("}");
                    context.Emitter.Writer.AppendLine($"std::atomic<bool> {AnyActive}{{true}};");
                    context.Emitter.Writer.AppendLine($"while ({AnyActive} && !{Error}.HasError())");
                    using (context.Emitter.Writer.Scope())
                    {
                        context.Emitter.Writer.AppendLine($"{AnyActive} = false;");
                        WriteBlockLoop(step, declaresThreadsPerBlock: true, ref context);
                    }
                }
                else
                {
                    WriteBlockLoop(step, declaresThreadsPerBlock: true, ref context);
                }
            }

            WriteStepAbortCatches(ref context);
        }
    }

    // The grid runs in phases; every block keeps its contexts and its shared memory arena across them:
    // try { <contexts and arenas of the grid> #pragma omp parallel { while (<next phase>) { #pragma omp for <blocks to
    // their grid sync> #pragma omp single { <end the phase> } } } } catch (const std::bad_alloc&) { <report> }
    private static void WriteCooperativeGrid(string contextName, in KernelStep step, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine(
            $"int32_t {ThreadsPerBlock} = (int32_t)({BlockDimX} * {BlockDimY} * {BlockDimZ});");
        context.Emitter.Writer.AppendLine("try");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine(
                $"std::vector<{contextName}> {GridContexts}((size_t){TotalBlocks} * {ThreadsPerBlock});");
            context.Emitter.Writer.AppendLine($"std::vector<{SharedMemoryArena}> {BlockArenas}({TotalBlocks});");
            context.Emitter.Writer.AppendLine($"std::vector<uint8_t> {BlockStatus}({TotalBlocks});");
            context.Emitter.Writer.AppendLine(
                $"for (int32_t {BlockLoopIndex} = 0; {BlockLoopIndex} < {TotalBlocks}; {BlockLoopIndex}++)");
            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Writer.AppendLine($"{BlockArenas}[{BlockLoopIndex}].reset({SharedMemBytes});");
                WriteBlockContexts(contextName, ref context);
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
                // [WHY] Only the omp single below changes the flag, between two barriers, so every OpenMP thread reads the
                // same value and meets the same worksharing constructs
                context.Emitter.Writer.AppendLine($"while ({NextGridPhase})");
                using (context.Emitter.Writer.Scope())
                {
                    WriteCooperativePhase(contextName, step, ref context);
                    context.Emitter.Writer.AppendLine("#pragma omp single");
                    using (context.Emitter.Writer.Scope())
                    {
                        context.Emitter.Writer.Append(
                            $"{NextGridPhase} = {EndGridPhase}(&{Error}, {BlockStatus}.data(), {TotalBlocks}, {GridDimX}, {GridDimY}, ");
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
            WriteAllocationFailure(ref context);
        }
    }

    // #pragma omp for: every block runs until its threads reach the grid sync or finish
    private static void WriteCooperativePhase(string contextName, in KernelStep step, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine("#pragma omp for schedule(dynamic)");
        context.Emitter.Writer.AppendLine(
            $"for (int32_t {BlockLoopIndex} = 0; {BlockLoopIndex} < {TotalBlocks}; {BlockLoopIndex}++)");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine($"if ({Error}.HasError()) continue;");
            context.Emitter.Writer.AppendLine("try");
            using (context.Emitter.Writer.Scope())
            {
                WriteBlockCoordinates(ref context);
                context.Emitter.Writer.AppendLine($"{BindSharedMemory}(&{BlockArenas}[{BlockLoopIndex}]);");
                WriteBlockContexts(contextName, ref context);
                // The threads that wait at the grid sync go on in this phase
                context.Emitter.Writer.AppendLine(
                    $"for (int32_t {ThreadLoopIndex} = 0; {ThreadLoopIndex} < {ThreadsPerBlock}; {ThreadLoopIndex}++)");
                using (context.Emitter.Writer.Scope())
                {
                    context.Emitter.Writer.AppendLine($"{Contexts}[{ThreadLoopIndex}].{YieldedForGridSync} = false;");
                }

                context.Emitter.Writer.AppendLine($"std::atomic<bool> {AnyActive}{{true}};");
                context.Emitter.Writer.AppendLine($"bool {GridSyncReached} = false;");
                // [WHY] A thread that reached the grid sync waits there while the other threads of the block go on to it:
                // they may need more steps (a shuffle or a block barrier before it). The block's part of the phase ends
                // when no thread can run
                context.Emitter.Writer.AppendLine($"while ({AnyActive} && !{Error}.HasError())");
                using (context.Emitter.Writer.Scope())
                {
                    context.Emitter.Writer.AppendLine($"{AnyActive} = false;");
                    WriteBlockLoop(step, declaresThreadsPerBlock: false, ref context);
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

            WriteStepAbortCatches(ref context);
        }
    }

    // <context>* __gsharp_contexts = __gsharp_grid_contexts.data() + (size_t)<block> * __gsharp_threads_per_block;
    private static void WriteBlockContexts(string contextName, ref TranspileContext context) =>
        context.Emitter.Writer.AppendLine(
            $"{contextName}* {Contexts} = {GridContexts}.data() + (size_t){BlockLoopIndex} * {ThreadsPerBlock};");

    private static void WriteBlockCoordinates(ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine($"int32_t {BlockX} = {BlockLoopIndex} % {GridDimX};");
        context.Emitter.Writer.AppendLine(
            $"int32_t {BlockY} = ({BlockLoopIndex} / {GridDimX}) % {GridDimY};");
        context.Emitter.Writer.AppendLine(
            $"int32_t {BlockZ} = {BlockLoopIndex} / ({GridDimX} * {GridDimY});");
    }

    private static void WriteBlockLoop(in KernelStep step, bool declaresThreadsPerBlock, ref TranspileContext context)
    {
        if (declaresThreadsPerBlock)
        {
            context.Emitter.Writer.AppendLine(
                $"int32_t {ThreadsPerBlock} = (int32_t)({BlockDimX} * {BlockDimY} * {BlockDimZ});");
        }

        // [WHY] The threads of a block run one after another on the OpenMP thread that runs the block: the shared memory
        // arena and the warp exchange of the block belong to that thread, so a nested parallel loop must not spread them
        context.Emitter.Writer.AppendLine(
            $"for (int32_t {ThreadLoopIndex} = 0; {ThreadLoopIndex} < {ThreadsPerBlock}; {ThreadLoopIndex}++)");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine($"if ({Error}.HasError()) continue;");
            context.Emitter.Writer.AppendLine($"{ActiveErrorSlot} = &{Error};");
            context.Emitter.Writer.AppendLine($"int32_t {ThreadX} = {ThreadLoopIndex} % {BlockDimX};");
            context.Emitter.Writer.AppendLine(
                $"int32_t {ThreadY} = ({ThreadLoopIndex} / {BlockDimX}) % {BlockDimY};");
            context.Emitter.Writer.AppendLine(
                $"int32_t {ThreadZ} = {ThreadLoopIndex} / ({BlockDimX} * {BlockDimY});");
            if (step.HasContext)
            {
                context.Emitter.Writer.AppendLine(
                    $"uint32_t {ContextThreadIndex} = {ThreadZ} * {BlockDimY} * {BlockDimX} + {ThreadY} * {BlockDimX} + {ThreadX};");
                // A thread that waits at a block barrier or at the grid sync does not run
                context.Emitter.Writer.AppendLine($"if ({RunnableCondition})");
            }

            using (step.HasContext ? context.Emitter.Writer.Scope() : default)
            {
                context.Emitter.Writer.AppendLine(
                    $"{SetThreadIndex}({ThreadX}, {ThreadY}, {ThreadZ}, {BlockX}, {BlockY}, {BlockZ}, {BlockDimX}, {BlockDimY}, {BlockDimZ}, {GridDimX}, {GridDimY}, {GridDimZ});");
                WriteKernelCall(step, ref context);
                if (step.HasContext)
                {
                    WriteGridSyncCheck(step, ref context);
                    // The next round runs while a thread can go on: one that stopped at a shuffle runs again, one at a
                    // barrier waits
                    context.Emitter.Writer.AppendLine($"if ({RunnableCondition})");
                    using (context.Emitter.Writer.Scope())
                    {
                        context.Emitter.Writer.AppendLine($"{AnyActive} = true;");
                    }
                }
            }
        }

        if (step.HasContext)
        {
            WriteBlockBarrierRelease(ref context);
        }
    }

    // <thread>.__gsharp_state != -1 && !<thread>.__gsharp_yielded_for_block_barrier && !<thread>.__gsharp_yielded_for_grid_sync
    private const string RunnableCondition =
        $"{Contexts}[{ContextThreadIndex}].{State} != -1 && !{Contexts}[{ContextThreadIndex}].{YieldedForBlockBarrier} && !{Contexts}[{ContextThreadIndex}].{YieldedForGridSync}";

    // [WHY] A thread that stopped at Gpu.SyncThreads waits there until no thread of the block can run any more, that is,
    // until every thread that has not finished waits at a barrier: only then does the block barrier let them all pass,
    // as on the GPU. A thread that stopped at a shuffle can run, so a warp that is still in a shuffle reaches the barrier
    // first (#664 review).
    // if (!__gsharp_any_active) { for (<thread>) { if (<thread waits at the block barrier>) { <release it>; __gsharp_any_active = true; } } }
    private static void WriteBlockBarrierRelease(ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine($"if (!{AnyActive})");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine(
                $"for (int32_t {ThreadLoopIndex} = 0; {ThreadLoopIndex} < {ThreadsPerBlock}; {ThreadLoopIndex}++)");
            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Writer.AppendLine($"if ({Contexts}[{ThreadLoopIndex}].{YieldedForBlockBarrier})");
                using (context.Emitter.Writer.Scope())
                {
                    context.Emitter.Writer.AppendLine($"{Contexts}[{ThreadLoopIndex}].{YieldedForBlockBarrier} = false;");
                    context.Emitter.Writer.AppendLine($"{AnyActive} = true;");
                }
            }
        }
    }

    // <kernel>(<arguments>[, &__gsharp_contexts[<thread>]][, __gsharp_auto_result]);
    private static void WriteKernelCall(in KernelStep step, ref TranspileContext context)
    {
        context.Emitter.Writer.Append(step.MethodName);
        using (context.Emitter.Writer.Parentheses())
        {
            var isFirst = true;

            foreach (var parameter in step.Method.ParameterList.Parameters)
            {
                AppendArgumentSeparator(ref isFirst, ref context);
                CppNameResolver.AppendDeclaredName(parameter, parameter.Identifier, ref context);
            }

            if (step.HasContext)
            {
                AppendArgumentSeparator(ref isFirst, ref context);
                context.Emitter.Writer.Append($"&{Contexts}[{ContextThreadIndex}]");
            }

            if (step.HasAutoResult)
            {
                AppendArgumentSeparator(ref isFirst, ref context);
                context.Emitter.Writer.Append(Buffer);
            }
        }

        context.Emitter.Writer.AppendLine(";");
    }

    // The thread stopped at Gpu.SyncGrid: in a cooperative kernel it waits there until the next phase, which clears the
    // flag; any other kernel fails.
    // if (__gsharp_contexts[<thread>].__gsharp_yielded_for_grid_sync) { __gsharp_grid_sync_reached = true; | __gsharp_trap(...); }
    private static void WriteGridSyncCheck(in KernelStep step, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine($"if ({Contexts}[{ContextThreadIndex}].{YieldedForGridSync})");
        using (context.Emitter.Writer.Scope())
        {
            if (step.IsCooperative)
            {
                context.Emitter.Writer.AppendLine($"{GridSyncReached} = true;");
            }
            else
            {
                context.Emitter.Writer.Append($"{Trap}(GSharpErrorCode::InvalidOperation, ");
                CppStringLiteral.Append(
                    context.Emitter.Writer,
                    $"G#: Gpu.SyncGrid reached in {step.MessageName}, which is not [GpuKernel(Cooperative = true)]");
                context.Emitter.Writer.AppendLine(");");
            }
        }
    }

    // A step that cannot go on (__gsharp_trap_abort, or memory that cannot be allocated) ends with an exception, which stops
    // at the try of the block, inside the iteration of the OpenMP loop; the error slot then stops the launch:
    // catch (const __gsharp_step_abort&) {} catch (const std::bad_alloc&) { <report> }
    private static void WriteStepAbortCatches(ref TranspileContext context)
    {
        // The failure was reported before the throw
        context.Emitter.Writer.AppendLine($"catch (const {StepAbort}&) {{}}");
        context.Emitter.Writer.AppendLine("catch (const std::bad_alloc&)");
        using (context.Emitter.Writer.Scope())
        {
            WriteAllocationFailure(ref context);
        }
    }

    // [WHY] Reported into the slot of the launch itself: memory can run out before a thread of this launch has set the
    // active slot of the OpenMP thread, which may still point at the slot of an earlier launch
    private static void WriteAllocationFailure(ref TranspileContext context) =>
        context.Emitter.Writer.AppendLine(
            $"{Error}.Report(GSharpErrorCode::InvalidOperation, \"G#: the native CPU path could not allocate memory for a kernel step\");");

    private static void AppendArgumentSeparator(ref bool isFirst, ref TranspileContext context)
    {
        if (!isFirst)
        {
            context.Emitter.Writer.Append(", ");
        }

        isFirst = false;
    }

    // What the launcher calls for each thread: the kernel, how it is called and how its grid syncs are handled
    private readonly struct KernelStep(
        MethodDeclarationSyntax method,
        string methodName,
        bool hasContext,
        bool hasAutoResult,
        bool isCooperative,
        string messageName)
    {
        public MethodDeclarationSyntax Method { get; } = method;

        public string MethodName { get; } = methodName;

        public bool HasContext { get; } = hasContext;

        public bool HasAutoResult { get; } = hasAutoResult;

        // A cooperative kernel with a state machine: it runs in grid phases
        public bool IsCooperative { get; } = isCooperative;

        public string MessageName { get; } = messageName;
    }
}
