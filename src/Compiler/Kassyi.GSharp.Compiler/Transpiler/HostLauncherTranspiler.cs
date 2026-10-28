namespace Kassyi.GSharp.Compiler.Transpiler;

using static GSharpInternalNames.Kernel;
using static GSharpInternalNames.Launcher;
using static GSharpInternalNames.ReturnValue;
using static GSharpInternalNames.StateMachine;
using static GSharpInternalNames.Prelude;

/// <summary>
/// Emits CPU / OpenMP multi-threaded execution launchers for host execution.
/// </summary>
/// <remarks>
/// Each block starts by resetting the shared memory arena of the OpenMP thread that runs it with the launch's
/// <c>sharedMemBytes</c> (<c>__gsharp_reset_smem</c>); the kernel and its device functions place their
/// <c>SharedMemory&lt;T&gt;</c> declarations in it, also those that a state machine keeps in its context (#544).
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

        context.Emitter.Writer.AppendLine();
        context.Emitter.Writer.Append("extern \"C\" __declspec(dllexport) void");
        var methodName = GlobalCppNames.GetKernelName(methodSymbol);
        var contextName = GlobalCppNames.GetKernelDerivedName(ContextPrefix, methodSymbol);
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

        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine($"{HostLastError}.Reset();");
            context.Emitter.Writer.AppendLine($"GSharpErrorSlot {Error};");
            context.Emitter.Writer.AppendLine(
                $"int32_t {TotalBlocks} = (int32_t)({GridDimX} * {GridDimY} * {GridDimZ});");
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
                    context.Emitter.Writer.AppendLine($"int32_t {BlockX} = {BlockLoopIndex} % {GridDimX};");
                    context.Emitter.Writer.AppendLine(
                        $"int32_t {BlockY} = ({BlockLoopIndex} / {GridDimX}) % {GridDimY};");
                    context.Emitter.Writer.AppendLine(
                        $"int32_t {BlockZ} = {BlockLoopIndex} / ({GridDimX} * {GridDimY});");
                    context.Emitter.Writer.AppendLine($"{ResetSharedMemory}({SharedMemBytes});");
                    if (hasContext)
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
                            WriteBlockLoop(method, methodName, hasContext, plan.HasAutoResult, ref context);
                        }
                    }
                    else
                    {
                        WriteBlockLoop(method, methodName, hasContext, plan.HasAutoResult, ref context);
                    }
                }

                WriteStepAbortCatches(ref context);
            }

            context.Emitter.Writer.AppendLine($"if ({Error}.HasError()) {{");
            context.Emitter.Writer.AppendLine(
                $"{HostLastError}.Report(static_cast<GSharpErrorCode>({Error}.code.load(std::memory_order_acquire)), {Error}.message);");
            context.Emitter.Writer.AppendLine("}");
        }
    }

    private static void WriteBlockLoop(MethodDeclarationSyntax method, string methodName, bool hasContext,
        bool hasAutoResult, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine(
            $"int32_t {ThreadsPerBlock} = (int32_t)({BlockDimX} * {BlockDimY} * {BlockDimZ});");
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
            if (hasContext)
            {
                context.Emitter.Writer.AppendLine(
                    $"uint32_t {ContextThreadIndex} = {ThreadZ} * {BlockDimY} * {BlockDimX} + {ThreadY} * {BlockDimX} + {ThreadX};");
                context.Emitter.Writer.AppendLine(
                    $"if ({Contexts}[{ContextThreadIndex}].{State} != -1)");
            }

            using (hasContext ? context.Emitter.Writer.Scope() : default)
            {
                if (hasContext)
                {
                    context.Emitter.Writer.AppendLine($"{AnyActive} = true;");
                }

                context.Emitter.Writer.AppendLine(
                    $"{SetThreadIndex}({ThreadX}, {ThreadY}, {ThreadZ}, {BlockX}, {BlockY}, {BlockZ}, {BlockDimX}, {BlockDimY}, {BlockDimZ}, {GridDimX}, {GridDimY}, {GridDimZ});");
                WriteKernelCall(method, methodName, hasContext, hasAutoResult, ref context);
            }
        }
    }

    // <kernel>(<arguments>[, &__gsharp_contexts[<thread>]][, __gsharp_auto_result]);
    private static void WriteKernelCall(MethodDeclarationSyntax method, string methodName, bool hasContext,
        bool hasAutoResult, ref TranspileContext context)
    {
        context.Emitter.Writer.Append(methodName);
        using (context.Emitter.Writer.Parentheses())
        {
            var isFirst = true;

            foreach (var parameter in method.ParameterList.Parameters)
            {
                AppendArgumentSeparator(ref isFirst, ref context);
                CppNameResolver.AppendDeclaredName(parameter, parameter.Identifier, ref context);
            }

            if (hasContext)
            {
                AppendArgumentSeparator(ref isFirst, ref context);
                context.Emitter.Writer.Append($"&{Contexts}[{ContextThreadIndex}]");
            }

            if (hasAutoResult)
            {
                AppendArgumentSeparator(ref isFirst, ref context);
                context.Emitter.Writer.Append(Buffer);
            }
        }

        context.Emitter.Writer.AppendLine(";");
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
            // [WHY] Reported into the slot of the launch itself: memory can run out before a thread of this launch has
            // set the active slot of the OpenMP thread, which may still point at the slot of an earlier launch
            context.Emitter.Writer.AppendLine(
                $"{Error}.Report(GSharpErrorCode::InvalidOperation, \"G#: the native CPU path could not allocate memory for a kernel step\");");
        }
    }

    private static void AppendArgumentSeparator(ref bool isFirst, ref TranspileContext context)
    {
        if (!isFirst)
        {
            context.Emitter.Writer.Append(", ");
        }

        isFirst = false;
    }
}
