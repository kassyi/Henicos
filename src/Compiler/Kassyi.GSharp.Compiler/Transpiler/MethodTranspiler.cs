using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Lowering;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Provides high-level compilation services to transpile C# method and GPU kernel declarations into CUDA C++ source definitions or forward declarations.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b> Utilizes pooled syntax walkers and string builders to achieve near zero-allocation transpilation.</para>
/// <para><b>Thread Safety:</b> All static methods in this class are thread-safe and can be invoked concurrently across multiple compilation workers.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler-time execution). Cannot be invoked within GPU kernels.</para>
/// </remarks>
public static class MethodTranspiler
{
    /// <summary>
    /// Transpiles a C# method declaration into a complete target dialect (CUDA C++ / host C++) implementation string,
    /// including dependent struct definitions, parameter qualifiers, and the method body.
    /// </summary>
    /// <param name="methodDecl">The Roslyn method declaration syntax node to transpile.</param>
    /// <param name="semanticModel">The semantic model bound to the compilation containing the method.</param>
    /// <param name="reportDiagnostic">An optional callback delegate to receive emitted compiler diagnostics and warnings.</param>
    /// <param name="isDevice">
    /// <see langword="true"/> to emit a device-side function (<c>__device__</c>); 
    /// <see langword="false"/> to emit a kernel entry point (<c>extern "C" __global__</c>).
    /// </param>
    /// <param name="dialect">
    /// The target language dialect to use for code generation. If <see langword="null"/>, defaults to <see cref="GSharpCudaDialect"/>.
    /// </param>
    /// <returns>
    /// A string containing the complete emitted source code, including topologically sorted struct dependencies and the function definition.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="methodDecl"/> or <paramref name="semanticModel"/> is <see langword="null"/>.
    /// </exception>
    public static string GenerateCudaSource(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel,
        Action<Diagnostic>? reportDiagnostic = null, bool isDevice = true,
        ITranspilerDialect? dialect = null)
    {
        using var session = TranspilationSession.Begin(methodDecl, semanticModel, reportDiagnostic, dialect);
        if (session.HasError)
        {
            return session.ErrorMessage!;
        }

        ref var context = ref session.Context;

        IReadOnlyList<VariableDeclaratorSyntax>? contextFields = null;
        BlockSyntax? loweredBody = null;

        if (!isDevice && !context.Emitter.Dialect.IsCuda && context.Emitter.IsGpuKernelEntry && methodDecl.Body != null)
        {
            var tuple = AstLoweringPipeline.Lower(methodDecl.Body!);
            loweredBody = tuple.LoweredBody;
            contextFields = tuple.ContextFields;
        }

        using var mainWriter = new SourceWriter();
        using var bodyWriter = new SourceWriter();

        context.Emitter.Writer = bodyWriter;

        if (contextFields != null)
        {
            var methodName = methodDecl.Identifier.Text;
            var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl);
            if (methodSymbol != null)
            {
                methodName = $"{methodSymbol.ContainingType.Name}_{methodName}";
            }

            WriteContextStruct(methodName, contextFields, ref context);
        }

        WriteMethodSignature(methodDecl, isDevice, contextFields != null, ref context);
        WriteMethodBody(methodDecl, loweredBody, ref context);

        if (!isDevice && !context.Emitter.Dialect.IsCuda)
        {
            WriteCpuLauncher(methodDecl, contextFields != null, ref context);
        }

        context.Emitter.Writer = mainWriter;
        var sortedStructs = StructTranspiler.GetTopologicallySortedStructs(ref context);
        foreach (var structSym in sortedStructs)
        {
            StructTranspiler.WriteStructDefinition(structSym, ref context);
        }

        return mainWriter + bodyWriter.ToString();
    }

    private static void WriteContextStruct(string methodName, IReadOnlyList<VariableDeclaratorSyntax> fields,
        ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine($"struct __gsharp_context_{methodName} {{");
        context.Emitter.Writer.AppendLine("int _state = 0;");
        context.Emitter.Writer.AppendLine($"__gsharp_context_{methodName}* _all_contexts = nullptr;");
        context.Emitter.Writer.AppendLine("uint64_t _warpExchange = 0;");
        foreach (var field in fields)
        {
            var typeSyntax = (field.Parent as VariableDeclarationSyntax)?.Type;
            if (typeSyntax == null)
            {
                continue;
            }

            var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(typeSyntax);
            if (typeSymbol == null && typeSyntax.IsVar)
            {
                // Fallback for dynamically generated AST nodes (e.g., _warpTemp_X)
                // Since they are not in the semantic model, default to 'int' for now.
                context.Emitter.Writer.Append("int");
                context.Emitter.Writer.Append($" {field.Identifier.ValueText};");
                context.Emitter.Writer.AppendLine();
                continue;
            }

            if (typeSymbol == null)
            {
                continue;
            }

            StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);
            context.Emitter.Dialect.AppendType(ref context, typeSymbol);
            context.Emitter.Writer.Append($" {field.Identifier.ValueText};");
            context.Emitter.Writer.AppendLine();
        }

        context.Emitter.Writer.AppendLine("};");
        context.Emitter.Writer.AppendLine();
    }

    /// <summary>
    /// Transpiles a C# method declaration into a target dialect forward declaration signature,
    /// along with forward declarations of referenced struct types.
    /// </summary>
    /// <param name="methodDecl">The Roslyn method declaration syntax node to transpile.</param>
    /// <param name="semanticModel">The semantic model bound to the compilation containing the method.</param>
    /// <param name="reportDiagnostic">An optional callback delegate to receive emitted compiler diagnostics and warnings.</param>
    /// <param name="isDevice">
    /// <see langword="true"/> to emit a device-side function forward signature; 
    /// <see langword="false"/> to emit a kernel entry point signature.
    /// </param>
    /// <param name="dialect">
    /// The target language dialect to use for code generation. If <see langword="null"/>, defaults to <see cref="GSharpCudaDialect"/>.
    /// </param>
    /// <returns>
    /// A string containing the forward declaration statements suitable for header inclusion.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="methodDecl"/> or <paramref name="semanticModel"/> is <see langword="null"/>.
    /// </exception>
    public static string GenerateCudaSignature(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel,
        Action<Diagnostic>? reportDiagnostic = null, bool isDevice = true,
        ITranspilerDialect? dialect = null)
    {
        using var session = TranspilationSession.Begin(methodDecl, semanticModel, reportDiagnostic, dialect);
        if (session.HasError)
        {
            return session.ErrorMessage!;
        }

        ref var context = ref session.Context;

        IReadOnlyList<VariableDeclaratorSyntax>? contextFields = null;
        if (!isDevice && !context.Emitter.Dialect.IsCuda && context.Emitter.IsGpuKernelEntry && methodDecl.Body != null)
        {
            var tuple = AstLoweringPipeline.Lower(methodDecl.Body!);
            contextFields = tuple.ContextFields;
        }

        using var mainWriter = new SourceWriter();
        using var sigWriter = new SourceWriter();

        context.Emitter.Writer = sigWriter;

        if (contextFields != null)
        {
            var methodName = methodDecl.Identifier.Text;
            var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl);
            if (methodSymbol != null)
            {
                methodName = $"{methodSymbol.ContainingType.Name}_{methodName}";
            }

            WriteContextStruct(methodName, contextFields, ref context);
        }

        WriteMethodSignature(methodDecl, isDevice, contextFields != null, ref context);
        context.Emitter.Writer.AppendSemiColonLine();

        context.Emitter.Writer = mainWriter;
        foreach (var structSym in StructTranspiler.GetTopologicallySortedStructs(ref context))
        {
            StructTranspiler.WriteTemplatePrefix(ref context, structSym);
            context.Emitter.Writer.Append("struct ").AppendFullyQualifiedCppName(structSym).AppendLine(";");
        }

        context.Emitter.Writer.Append(sigWriter.ToString());
        return mainWriter.ToString();
    }


    private static void WriteMethodSignature(MethodDeclarationSyntax method, bool isDevice, bool hasContext,
        ref TranspileContext context)
    {
        var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(method);

        if (isDevice)
        {
            WriteDeviceMethodPrefix(methodSymbol, ref context);
        }
        else
        {
            WriteKernelMethodPrefix(ref context);
        }

        var methodName = method.Identifier.Text;
        if (!isDevice && methodSymbol != null)
        {
            methodName = $"{methodSymbol.ContainingType.Name}_{methodName}";
        }

        context.Emitter.Writer.Append($" {methodName}");
        using (context.Emitter.Writer.Parentheses())
        {
            WriteParameters(method.ParameterList.Parameters, isDevice, ref context);

            if (hasContext)
            {
                if (method.ParameterList.Parameters.Count > 0)
                {
                    context.Emitter.Writer.Append(", ");
                }

                context.Emitter.Writer.Append($"__gsharp_context_{methodName}* _ctx");
            }

            if (isDevice || methodSymbol is null || !methodSymbol.HasAttribute(KnownAttributes.GpuKernel) ||
                methodSymbol.ReturnType.SpecialType == SpecialType.System_Void)
            {
                return;
            }

            if (method.ParameterList.Parameters.Count > 0 || hasContext)
            {
                context.Emitter.Writer.Append(", ");
            }

            context.Emitter.Dialect.AppendType(ref context, methodSymbol.ReturnType);
            context.Emitter.Writer.Append("* __restrict__ __gsharp_auto_result");
        }
    }

    private static void WriteDeviceMethodPrefix(IMethodSymbol? methodSymbol, ref TranspileContext context)
    {
        var inlineKeyword = GetInliningKeyword(methodSymbol);

        if (context.Emitter.Dialect.IsCuda)
        {
            context.Emitter.Writer.Append($"{inlineKeyword} __device__ ");
        }
        else
        {
            context.Emitter.Writer.Append($"{inlineKeyword} ");
        }

        var returnType = methodSymbol?.ReturnType;
        if (returnType is { SpecialType: not SpecialType.System_Void })
        {
            context.Emitter.Dialect.AppendType(ref context, returnType);
        }
        else
        {
            context.Emitter.Writer.AppendKeyword("void");
        }
    }

    private static string GetInliningKeyword(IMethodSymbol? methodSymbol)
    {
        if (methodSymbol == null)
        {
            return "inline";
        }

        foreach (var attr in methodSymbol.GetAttributes())
        {
            var keyword = attr.AttributeClass?.Name switch
            {
                KnownAttributes.GpuInline or KnownAttributes.GpuInline + "Attribute" => "__forceinline__",
                KnownAttributes.GpuNoInline or KnownAttributes.GpuNoInline + "Attribute" => "__noinline__",
                KnownAttributes.MethodImpl or KnownAttributes.MethodImpl + "Attribute" => ExtractMethodImplOptions(attr)
                    switch
                    {
                        var opt when (opt & MethodImplOptions.AggressiveInlining) != 0 => "__forceinline__",
                        var opt when (opt & MethodImplOptions.NoInlining) != 0 => "__noinline__",
                        _ => null
                    },
                _ => null
            };

            if (keyword != null)
            {
                return keyword;
            }
        }

        return "inline";
    }

    private static MethodImplOptions ExtractMethodImplOptions(AttributeData attr)
    {
        if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int or short)
        {
            return (MethodImplOptions)Convert.ToInt32(attr.ConstructorArguments[0].Value);
        }

        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg is { Key: "Value", Value.Value: int or short })
            {
                return (MethodImplOptions)Convert.ToInt32(namedArg.Value.Value);
            }
        }

        return default;
    }

    private static void WriteKernelMethodPrefix(ref TranspileContext context)
    {
        // Auto-allocate feature: GpuKernel methods are now allowed to return values.
        context.Emitter.Writer.AppendKeyword(context.Emitter.Dialect.IsCuda
            ? "extern \"C\" __global__ void"
            : "extern \"C\" __declspec(dllexport) void");
    }

    private static void WriteMethodBody(MethodDeclarationSyntax method, BlockSyntax? loweredBody,
        ref TranspileContext context)
    {
        using (context.Emitter.Writer.Scope())
        {
            var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(method);
            var isKernelWithReturn = methodSymbol != null && methodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
                                     methodSymbol.ReturnType.SpecialType != SpecialType.System_Void;

            if (isKernelWithReturn)
            {
                context.Emitter.Writer.AppendLine(
                    "unsigned int __gsharp_global_tid = (blockIdx.z * gridDim.y * gridDim.x + blockIdx.y * gridDim.x + blockIdx.x)" +
                    " * (blockDim.x * blockDim.y * blockDim.z) +" +
                    " threadIdx.z * blockDim.y * blockDim.x + threadIdx.y * blockDim.x + threadIdx.x;");
            }

            if (loweredBody != null)
            {
                context.Emitter.Writer.AppendLine("#define _state _ctx->_state");
            }

            if (loweredBody != null)
            {
                context.Visitor.Visit(loweredBody);
            }
            else if (method.Body != null)
            {
                context.Visitor.Visit(method.Body);
            }
            else if (method.ExpressionBody != null)
            {
                if (methodSymbol is { ReturnsVoid: false })
                {
                    context.Emitter.Writer.Append(isKernelWithReturn
                        ? "__gsharp_auto_result[__gsharp_global_tid] = "
                        : "return ");
                }

                context.Visitor.Visit(method.ExpressionBody.Expression);
                context.Emitter.Writer.AppendSemiColonLine();

                if (isKernelWithReturn)
                {
                    context.Emitter.Writer.AppendKeyword("return").AppendSemiColonLine();
                }
            }

            if (loweredBody != null)
            {
                context.Emitter.Writer.AppendLine("#undef _state");
            }
        }
    }

    private static void WriteParameters(SeparatedSyntaxList<ParameterSyntax> parameters, bool isDevice,
        ref TranspileContext context)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (p.Type == null)
            {
                continue;
            }

            var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(p.Type);

            StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);

            var paramSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IParameterSymbol>(p);
            if (paramSymbol != null && paramSymbol.HasAttribute(KnownAttributes.GpuReadOnly))
            {
                context.Emitter.Writer.Append("const ");
            }

            var (isRefPointer, isPointerType, hasRestrict) =
                context.Emitter.Dialect.AppendParameter(ref context, p, typeSymbol);
            StructTranspiler.CheckLargeStructByValue(p, typeSymbol, isRefPointer, ref context);

            if (!isDevice && isPointerType && !hasRestrict)
            {
                context.Emitter.Writer.Append(" __restrict__");
            }

            context.Emitter.Writer.Append($" {p.Identifier.Text}");
            if (i < parameters.Count - 1)
            {
                context.Emitter.Writer.Append(", ");
            }
        }
    }

    private static void WriteCpuLauncher(MethodDeclarationSyntax method, bool hasContext, ref TranspileContext context)
    {
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

        WriteParameters(method.ParameterList.Parameters, false, ref context);

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
            if (hasContext)
            {
                context.Emitter.Writer.AppendLine(
                    $"std::vector<__gsharp_context_{methodName}> _gsharp_contexts(blockDimX * blockDimY * blockDimZ);");
            }

            context.Emitter.Writer.AppendLine("for (uint32_t bz = 0; bz < gridDimZ; bz++)");
            context.Emitter.Writer.AppendLine("for (uint32_t by = 0; by < gridDimY; by++)");
            context.Emitter.Writer.AppendLine("for (uint32_t bx = 0; bx < gridDimX; bx++)");
            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Writer.AppendLine("__gsharp_reset_smem();");
                if (hasContext)
                {
                    context.Emitter.Writer.AppendLine("for (auto& ctx : _gsharp_contexts) { ctx._state = 0; ctx._all_contexts = _gsharp_contexts.data(); }");
                    context.Emitter.Writer.AppendLine("bool anyActive = true;");
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

    private static void WriteBlockLoop(MethodDeclarationSyntax method, IMethodSymbol methodSymbol, string methodName,
        bool hasContext, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine("for (uint32_t tz = 0; tz < blockDimZ; tz++)");
        context.Emitter.Writer.AppendLine("for (uint32_t ty = 0; ty < blockDimY; ty++)");
        context.Emitter.Writer.AppendLine("for (uint32_t tx = 0; tx < blockDimX; tx++)");
        using (context.Emitter.Writer.Scope())
        {
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
