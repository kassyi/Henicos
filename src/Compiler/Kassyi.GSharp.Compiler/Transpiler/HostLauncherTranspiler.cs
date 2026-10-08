using Kassyi.GSharp.Compiler.Transpiler.Helpers;

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
        var methodName = $"{methodSymbol.ContainingType.Name}_{method.Identifier.Text}";
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
            context.Emitter.Writer.AppendLine(
                "int32_t _gsharp_total_blocks = (int32_t)(gridDimX * gridDimY * gridDimZ);");
            context.Emitter.Writer.AppendLine("#pragma omp parallel for schedule(dynamic)");
            context.Emitter.Writer.AppendLine("for (int32_t _b_idx = 0; _b_idx < _gsharp_total_blocks; _b_idx++)");
            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Writer.AppendLine("int32_t bx = _b_idx % gridDimX;");
                context.Emitter.Writer.AppendLine("int32_t by = (_b_idx / gridDimX) % gridDimY;");
                context.Emitter.Writer.AppendLine("int32_t bz = _b_idx / (gridDimX * gridDimY);");
                context.Emitter.Writer.AppendLine("__gsharp_reset_smem();");
                if (hasContext)
                {
                    context.Emitter.Writer.AppendLine(
                        $"std::vector<__gsharp_context_{methodName}> _gsharp_contexts(blockDimX * blockDimY * blockDimZ);");

                    if (contextFields != null)
                    {
                        WriteSharedMemoryEmulationBuffers(contextFields, ref context);
                    }

                    context.Emitter.Writer.AppendLine("for (auto& ctx : _gsharp_contexts) {");
                    context.Emitter.Writer.AppendLine("ctx._state = 0;");
                    context.Emitter.Writer.AppendLine("ctx._all_contexts = _gsharp_contexts.data();");

                    if (contextFields != null)
                    {
                        WriteSharedMemoryEmulationAssignments(contextFields, ref context);
                    }

                    context.Emitter.Writer.AppendLine("}");
                    context.Emitter.Writer.AppendLine("std::atomic<bool> anyActive{true};");
                    context.Emitter.Writer.AppendLine("while (anyActive)");
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
        }
    }

    private static void WriteSharedMemoryEmulationBuffers(IReadOnlyList<VariableDeclaratorSyntax> contextFields,
        ref TranspileContext context)
    {
        foreach (var field in contextFields)
        {
            var typeSyntax = (field.Parent as VariableDeclarationSyntax)?.Type;
            if (typeSyntax is not GenericNameSyntax genericName || genericName.Identifier.ValueText != "SharedMemory")
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
            context.Emitter.Writer.Append($"> _smem_buffer_{field.Identifier.ValueText}(");
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
            if (typeSyntax is GenericNameSyntax { Identifier.ValueText: "SharedMemory" })
            {
                context.Emitter.Writer.AppendLine(
                    $"ctx.{field.Identifier.ValueText}.data = _smem_buffer_{field.Identifier.ValueText}.data();");
            }
        }
    }

    private static void WriteBlockLoop(MethodDeclarationSyntax method, IMethodSymbol methodSymbol, string methodName,
        bool hasContext, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine(
            "int32_t _gsharp_total_threads = (int32_t)(blockDimX * blockDimY * blockDimZ);");
        context.Emitter.Writer.AppendLine("#pragma omp parallel for schedule(static)");
        context.Emitter.Writer.AppendLine("for (int32_t _t_idx = 0; _t_idx < _gsharp_total_threads; _t_idx++)");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine("int32_t tx = _t_idx % blockDimX;");
            context.Emitter.Writer.AppendLine("int32_t ty = (_t_idx / blockDimX) % blockDimY;");
            context.Emitter.Writer.AppendLine("int32_t tz = _t_idx / (blockDimX * blockDimY);");
            if (hasContext)
            {
                context.Emitter.Writer.AppendLine(
                    "uint32_t _ctxtid = tz * blockDimY * blockDimX + ty * blockDimX + tx;");
                context.Emitter.Writer.AppendLine("if (_gsharp_contexts[_ctxtid]._state != -1)");
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
                        if (!isFirst)
                        {
                            context.Emitter.Writer.AppendComma().AppendSpace();
                        }

                        isFirst = false;
                        context.Emitter.Writer.Append(parameter.Identifier.Text);
                    }

                    if (hasContext)
                    {
                        if (!isFirst)
                        {
                            context.Emitter.Writer.AppendComma().AppendSpace();
                        }

                        isFirst = false;
                        context.Emitter.Writer.Append("&_gsharp_contexts[_ctxtid]");
                    }

                    var isKernelWithReturn = methodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
                                             methodSymbol.ReturnType.SpecialType != SpecialType.System_Void;

                    if (isKernelWithReturn)
                    {
                        if (!isFirst)
                        {
                            context.Emitter.Writer.AppendComma().AppendSpace();
                        }

                        context.Emitter.Writer.Append("__gsharp_auto_result");
                    }
                }

                context.Emitter.Writer.AppendSemiColonLine();
            }
        }
    }
}
