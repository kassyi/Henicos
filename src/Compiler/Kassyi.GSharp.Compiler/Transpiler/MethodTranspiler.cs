using Kassyi.GSharp.Compiler.Dialects;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Provides high-level compilation services to transpile C# method and GPU kernel declarations into CUDA C++ source definitions or forward declarations.
/// </summary>
/// <remarks>
/// <para>Implements: <b>GEN-20</b></para>
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

        using var mainWriter = new SourceWriter();
        using var bodyWriter = new SourceWriter();

        ref var context = ref session.Context;
        context.Writer = bodyWriter;

        WriteMethodSignature(methodDecl, isDevice, ref context);
        WriteMethodBody(methodDecl, ref context);

        if (!isDevice && !context.Dialect.IsCuda)
        {
            WriteCpuLauncher(methodDecl, ref context);
        }

        context.Writer = mainWriter;
        var sortedStructs = StructTranspiler.GetTopologicallySortedStructs(ref context);
        foreach (var structSym in sortedStructs)
        {
            StructTranspiler.WriteStructDefinition(structSym, ref context);
        }

        return mainWriter + bodyWriter.ToString();
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

        using var mainWriter = new SourceWriter();
        using var sigWriter = new SourceWriter();

        ref var context = ref session.Context;
        context.Writer = sigWriter;

        WriteMethodSignature(methodDecl, isDevice, ref context);
        context.Writer.AppendSemiColonLine();

        context.Writer = mainWriter;
        foreach (var structSym in StructTranspiler.GetTopologicallySortedStructs(ref context))
        {
            StructTranspiler.WriteTemplatePrefix(ref context, structSym);
            context.Writer.Append("struct ").AppendFullyQualifiedCppName(structSym).AppendLine(";");
        }

        context.Writer.Append(sigWriter.ToString());
        return mainWriter.ToString();
    }


    private static void WriteMethodSignature(MethodDeclarationSyntax method, bool isDevice,
        ref TranspileContext context)
    {
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(method);

        if (isDevice)
        {
            WriteDeviceMethodPrefix(methodSymbol, ref context);
        }
        else
        {
            WriteKernelMethodPrefix(ref context);
        }

        context.Writer.Append($" {method.Identifier.Text}");
        using (context.Writer.Parentheses())
        {
            WriteParameters(method.ParameterList.Parameters, isDevice, ref context);

            if (isDevice || methodSymbol is null || !methodSymbol.HasAttribute(KnownAttributes.GpuKernel) ||
                methodSymbol.ReturnType.SpecialType == SpecialType.System_Void)
            {
                return;
            }

            if (method.ParameterList.Parameters.Count > 0)
            {
                context.Writer.Append(", ");
            }

            context.AppendType(methodSymbol.ReturnType);
            context.Writer.Append("* __restrict__ __gsharp_auto_result");
        }
    }

    private static void WriteDeviceMethodPrefix(IMethodSymbol? methodSymbol, ref TranspileContext context)
    {
        var inlineKeyword = GetInliningKeyword(methodSymbol);

        if (context.Dialect.IsCuda)
        {
            context.Writer.Append($"{inlineKeyword} __device__ ");
        }
        else
        {
            context.Writer.Append($"{inlineKeyword} ");
        }

        var returnType = methodSymbol?.ReturnType;
        if (returnType is { SpecialType: not SpecialType.System_Void })
        {
            context.AppendType(returnType);
        }
        else
        {
            context.Writer.AppendKeyword("void");
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
        context.Writer.AppendKeyword(context.Dialect.IsCuda
            ? "extern \"C\" __global__ void"
            : "extern \"C\" __declspec(dllexport) void");
    }

    private static void WriteMethodBody(MethodDeclarationSyntax method, ref TranspileContext context)
    {
        using (context.Writer.Scope())
        {
            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(method);
            var isKernelWithReturn = methodSymbol != null && methodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
                                     methodSymbol.ReturnType.SpecialType != SpecialType.System_Void;

            if (isKernelWithReturn)
            {
                context.Writer.AppendLine(
                    "unsigned int __gsharp_global_tid = (blockIdx.z * gridDim.y * gridDim.x + blockIdx.y * gridDim.x + blockIdx.x)" +
                    " * (blockDim.x * blockDim.y * blockDim.z) + threadIdx.z * blockDim.y * blockDim.x + threadIdx.y * blockDim.x + threadIdx.x;");
            }

            if (method.Body != null)
            {
                context.Visitor.Visit(method.Body);
            }
            else if (method.ExpressionBody != null)
            {
                if (methodSymbol is { ReturnsVoid: false })
                {
                    context.Writer.Append(isKernelWithReturn
                        ? "__gsharp_auto_result[__gsharp_global_tid] = "
                        : "return ");
                }

                context.Visitor.Visit(method.ExpressionBody.Expression);
                context.Writer.AppendSemiColonLine();

                if (isKernelWithReturn)
                {
                    context.Writer.AppendKeyword("return").AppendSemiColonLine();
                }
            }
        }
    }

    private static void WriteParameters(SeparatedSyntaxList<ParameterSyntax> parameters, bool isDevice,
        ref TranspileContext context)
    {
        var validParameters = new List<ParameterSyntax>(parameters.Count);
        foreach (var p in parameters)
        {
            validParameters.Add(p);
        }

        for (var i = 0; i < validParameters.Count; i++)
        {
            var p = validParameters[i];
            if (p.Type == null)
            {
                continue;
            }

            var typeSymbol = context.SemanticModel.GetTypeInfo(p.Type).Type;

            StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);

            var paramSymbol = context.SemanticModel.GetDeclaredSymbol(p);
            if (paramSymbol != null && paramSymbol.HasAttribute(KnownAttributes.GpuConst))
            {
                context.Writer.Append("const ");
            }

            var (isRefPointer, isPointerType, hasRestrict) = context.AppendParameterCppType(p, typeSymbol);
            StructTranspiler.CheckLargeStructByValue(p, typeSymbol, isRefPointer, ref context);

            if (!isDevice && isPointerType && !hasRestrict)
            {
                context.Writer.Append(" __restrict__");
            }

            context.Writer.Append($" {p.Identifier.Text}");
            if (i < validParameters.Count - 1)
            {
                context.Writer.Append(", ");
            }
        }
    }

    private static void WriteCpuLauncher(MethodDeclarationSyntax method, ref TranspileContext context)
    {
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(method);
        if (methodSymbol == null)
        {
            return;
        }

        context.Writer.AppendLine();
        context.Writer.AppendKeyword("extern \"C\" __declspec(dllexport) void");
        context.Writer.Append($" __gsharp_launch_{method.Identifier.Text}(");

        // Launch dimensions
        context.Writer.Append("uint32_t gridDimX, uint32_t gridDimY, uint32_t gridDimZ, ");
        context.Writer.Append("uint32_t blockDimX, uint32_t blockDimY, uint32_t blockDimZ");

        if (method.ParameterList.Parameters.Count > 0)
        {
            context.Writer.Append(", ");
        }

        WriteParameters(method.ParameterList.Parameters, false, ref context);

        var isKernelWithReturn = methodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
                                 methodSymbol.ReturnType.SpecialType != SpecialType.System_Void;
        if (isKernelWithReturn)
        {
            context.Writer.Append(", ");
            context.AppendType(methodSymbol.ReturnType);
            context.Writer.Append("* __restrict__ __gsharp_auto_result");
        }

        context.Writer.AppendLine(")");

        using (context.Writer.Scope())
        {
            context.Writer.AppendLine("for (uint32_t bz = 0; bz < gridDimZ; bz++)");
            context.Writer.AppendLine("for (uint32_t by = 0; by < gridDimY; by++)");
            context.Writer.AppendLine("for (uint32_t bx = 0; bx < gridDimX; bx++)");
            context.Writer.AppendLine("for (uint32_t tz = 0; tz < blockDimZ; tz++)");
            context.Writer.AppendLine("for (uint32_t ty = 0; ty < blockDimY; ty++)");
            context.Writer.AppendLine("for (uint32_t tx = 0; tx < blockDimX; tx++)");
            using (context.Writer.Scope())
            {
                context.Writer.AppendLine(
                    "__gsharp_set_thread_idx(tx, ty, tz, bx, by, bz, blockDimX, blockDimY, blockDimZ, gridDimX, gridDimY, gridDimZ);");
                context.Writer.Append(method.Identifier.Text);
                using (context.Writer.Parentheses())
                {
                    var isFirst = true;

                    foreach (var parameter in method.ParameterList.Parameters)
                    {
                        if (!isFirst)
                        {
                            context.Writer.AppendComma().AppendSpace();
                        }

                        isFirst = false;

                        context.Writer.Append(parameter.Identifier.Text);
                    }

                    if (isKernelWithReturn)
                    {
                        if (!isFirst)
                        {
                            context.Writer.AppendComma().AppendSpace();
                        }

                        context.Writer.Append("__gsharp_auto_result");
                    }
                }

                context.Writer.AppendSemiColonLine();
            }
        }
    }
}
