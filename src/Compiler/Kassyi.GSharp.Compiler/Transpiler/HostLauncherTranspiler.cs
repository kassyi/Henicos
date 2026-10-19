
namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Emits CPU / OpenMP multi-threaded execution launchers and shared memory emulation wrappers for host execution.
/// </summary>
internal static class HostLauncherTranspiler
{
    public static void WriteCpuLauncher(MethodDeclarationSyntax method,
        IReadOnlyList<VariableDeclaratorSyntax>? contextFields, ref TranspileContext context)
    {
        var hasContext = contextFields != null;
        var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(method);
        if (methodSymbol == null)
        {
            return;
        }

        context.Emitter.Writer.AppendLine();
        context.Emitter.Writer.AppendKeyword("extern \"C\" __declspec(dllexport) void");
        var methodName = MethodSignatureTranspiler.GetKernelMethodName(method, methodSymbol);
        context.Emitter.Writer.Append($" __gsharp_launch_{methodName}(");

        // Launch dimensions
        context.Emitter.Writer.Append("uint32_t gridDimX, uint32_t gridDimY, uint32_t gridDimZ, ");
        context.Emitter.Writer.Append("uint32_t blockDimX, uint32_t blockDimY, uint32_t blockDimZ");

        if (method.ParameterList.Parameters.Count > 0)
        {
            context.Emitter.Writer.Append(", ");
        }

        MethodSignatureTranspiler.WriteParameters(method.ParameterList.Parameters, false, ref context);

        var isKernelWithReturn = methodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
                                 methodSymbol.ReturnType.SpecialType != SpecialType.System_Void;
        if (isKernelWithReturn)
        {
            context.Emitter.Writer.Append(", ");
            context.Emitter.Dialect.AppendType(ref context, methodSymbol.ReturnType);
            context.Emitter.Writer.Append("* __restrict__ __gsharp_auto_result");
        }

        context.Emitter.Writer.AppendLine(")");

        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine("__gsharp_host_last_error.Reset();");
            context.Emitter.Writer.AppendLine($"GSharpErrorSlot {GSharpInternalNames.LaunchError};");
            context.Emitter.Writer.AppendLine(
                $"int32_t {GSharpInternalNames.TotalBlocks} = (int32_t)(gridDimX * gridDimY * gridDimZ);");
            context.Emitter.Writer.AppendLine("#pragma omp parallel for schedule(dynamic)");
            context.Emitter.Writer.AppendLine(
                $"for (int32_t {GSharpInternalNames.BlockLoopIndex} = 0; {GSharpInternalNames.BlockLoopIndex} < {GSharpInternalNames.TotalBlocks}; {GSharpInternalNames.BlockLoopIndex}++)");
            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Writer.AppendLine($"if ({GSharpInternalNames.LaunchError}.HasError()) continue;");
                context.Emitter.Writer.AppendLine($"int32_t bx = {GSharpInternalNames.BlockLoopIndex} % gridDimX;");
                context.Emitter.Writer.AppendLine(
                    $"int32_t by = ({GSharpInternalNames.BlockLoopIndex} / gridDimX) % gridDimY;");
                context.Emitter.Writer.AppendLine(
                    $"int32_t bz = {GSharpInternalNames.BlockLoopIndex} / (gridDimX * gridDimY);");
                context.Emitter.Writer.AppendLine("__gsharp_reset_smem();");
                if (hasContext)
                {
                    context.Emitter.Writer.AppendLine(
                        $"std::vector<__gsharp_context_{methodName}> {GSharpInternalNames.LaunchContexts}(blockDimX * blockDimY * blockDimZ);");

                    if (contextFields != null)
                    {
                        WriteSharedMemoryEmulationBuffers(contextFields, ref context);
                    }

                    context.Emitter.Writer.AppendLine($"for (auto& ctx : {GSharpInternalNames.LaunchContexts}) {{");
                    context.Emitter.Writer.AppendLine($"ctx.{GSharpInternalNames.State} = 0;");
                    context.Emitter.Writer.AppendLine(
                        $"ctx.{GSharpInternalNames.AllContexts} = {GSharpInternalNames.LaunchContexts}.data();");

                    if (contextFields != null)
                    {
                        WriteSharedMemoryEmulationAssignments(contextFields, ref context);
                    }

                    context.Emitter.Writer.AppendLine("}");
                    context.Emitter.Writer.AppendLine("std::atomic<bool> anyActive{true};");
                    context.Emitter.Writer.AppendLine(
                        $"while (anyActive && !{GSharpInternalNames.LaunchError}.HasError())");
                    using (context.Emitter.Writer.Scope())
                    {
                        context.Emitter.Writer.AppendLine("anyActive = false;");
                        WriteBlockLoop(method, methodSymbol, methodName, hasContext, ref context);
                    }
                }
                else
                {
                    WriteBlockLoop(method, methodSymbol, methodName, hasContext, ref context);
                }
            }

            context.Emitter.Writer.AppendLine($"if ({GSharpInternalNames.LaunchError}.HasError()) {{");
            context.Emitter.Writer.AppendLine(
                $"__gsharp_host_last_error.Report(static_cast<GSharpErrorCode>({GSharpInternalNames.LaunchError}.code.load(std::memory_order_acquire)), {GSharpInternalNames.LaunchError}.message);");
            context.Emitter.Writer.AppendLine("}");
        }
    }

    private static void WriteSharedMemoryEmulationBuffers(IReadOnlyList<VariableDeclaratorSyntax> contextFields,
        ref TranspileContext context)
    {
        foreach (var field in contextFields)
        {
            var typeSyntax = (field.Parent as VariableDeclarationSyntax)?.Type;
            if (typeSyntax is not GenericNameSyntax { Identifier.ValueText: TypeConstants.SharedMemoryType })
            {
                continue;
            }

            var typeSymbolField = context.Query.SemanticModel.GetTypeSymbol(typeSyntax) as INamedTypeSymbol;
            var typeArg = typeSymbolField?.TypeArguments[0];

            var initializer = field.Initializer?.Value;
            var sizeExpression =
                initializer != null &&
                InitializationSyntaxExtractor.TryGetObjectCreationFirstArgument(initializer, out var sizeArg)
                    ? sizeArg
                    : null;

            if (typeArg == null || sizeExpression == null)
            {
                continue;
            }

            context.Emitter.Writer.Append("std::vector<");
            context.Emitter.Dialect.AppendType(ref context, typeArg);
            context.Emitter.Writer.Append(
                $"> {GSharpInternalNames.SharedMemoryBufferPrefix}{field.Identifier.ValueText}(");
            context.Visitor.Visit(sizeExpression);
            context.Emitter.Writer.AppendLine(");");
        }
    }

    private static void WriteSharedMemoryEmulationAssignments(IReadOnlyList<VariableDeclaratorSyntax> contextFields,
        ref TranspileContext context)
    {
        foreach (var field in contextFields)
        {
            var typeSyntax = (field.Parent as VariableDeclarationSyntax)?.Type;
            if (typeSyntax is GenericNameSyntax { Identifier.ValueText: TypeConstants.SharedMemoryType })
            {
                context.Emitter.Writer.AppendLine(
                    $"ctx.{field.Identifier.ValueText}.data = {GSharpInternalNames.SharedMemoryBufferPrefix}{field.Identifier.ValueText}.data();");
            }
        }
    }

    private static void WriteBlockLoop(MethodDeclarationSyntax method, IMethodSymbol methodSymbol, string methodName,
        bool hasContext, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine(
            $"int32_t {GSharpInternalNames.ThreadsPerBlock} = (int32_t)(blockDimX * blockDimY * blockDimZ);");
        context.Emitter.Writer.AppendLine("#pragma omp parallel for schedule(static)");
        context.Emitter.Writer.AppendLine(
            $"for (int32_t {GSharpInternalNames.ThreadLoopIndex} = 0; {GSharpInternalNames.ThreadLoopIndex} < {GSharpInternalNames.ThreadsPerBlock}; {GSharpInternalNames.ThreadLoopIndex}++)");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine($"if ({GSharpInternalNames.LaunchError}.HasError()) continue;");
            context.Emitter.Writer.AppendLine($"__gsharp_active_error_slot = &{GSharpInternalNames.LaunchError};");
            context.Emitter.Writer.AppendLine($"int32_t tx = {GSharpInternalNames.ThreadLoopIndex} % blockDimX;");
            context.Emitter.Writer.AppendLine(
                $"int32_t ty = ({GSharpInternalNames.ThreadLoopIndex} / blockDimX) % blockDimY;");
            context.Emitter.Writer.AppendLine(
                $"int32_t tz = {GSharpInternalNames.ThreadLoopIndex} / (blockDimX * blockDimY);");
            if (hasContext)
            {
                context.Emitter.Writer.AppendLine(
                    $"uint32_t {GSharpInternalNames.ContextThreadIndex} = tz * blockDimY * blockDimX + ty * blockDimX + tx;");
                context.Emitter.Writer.AppendLine(
                    $"if ({GSharpInternalNames.LaunchContexts}[{GSharpInternalNames.ContextThreadIndex}].{GSharpInternalNames.State} != -1)");
            }

            using (hasContext ? context.Emitter.Writer.Scope() : default)
            {
                if (hasContext)
                {
                    context.Emitter.Writer.AppendLine("anyActive = true;");
                }

                context.Emitter.Writer.AppendLine(
                    "__gsharp_set_thread_idx(tx, ty, tz, bx, by, bz, blockDimX, blockDimY, blockDimZ, gridDimX, gridDimY, gridDimZ);");
                context.Emitter.Writer.Append(methodName);
                using (context.Emitter.Writer.Parentheses())
                {
                    var isFirst = true;

                    foreach (var parameter in method.ParameterList.Parameters)
                    {
                        AppendArgument(parameter.Identifier.Text, ref isFirst, ref context);
                    }

                    if (hasContext)
                    {
                        AppendArgument(
                            $"&{GSharpInternalNames.LaunchContexts}[{GSharpInternalNames.ContextThreadIndex}]",
                            ref isFirst, ref context);
                    }

                    var isKernelWithReturn = methodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
                                             methodSymbol.ReturnType.SpecialType != SpecialType.System_Void;

                    if (isKernelWithReturn)
                    {
                        AppendArgument("__gsharp_auto_result", ref isFirst, ref context);
                    }
                }

                context.Emitter.Writer.AppendSemiColonLine();
            }
        }
    }

    private static void AppendArgument(string argument, ref bool isFirst, ref TranspileContext context)
    {
        if (!isFirst)
        {
            context.Emitter.Writer.AppendComma().AppendSpace();
        }

        isFirst = false;
        context.Emitter.Writer.Append(argument);
    }
}
