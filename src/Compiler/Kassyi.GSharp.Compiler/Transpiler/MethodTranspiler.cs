using Kassyi.GSharp.Compiler.Lowering;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Provides high-level compilation services to transpile C# method and GPU kernel declarations into CUDA C++ source definitions or forward declarations.
/// Acts as a unified facade coordinating specialized transpilers for signatures, bodies, host launchers, and managed state machines.
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
    /// <param name="requiredIncludes">
    /// When this method returns, contains the set of header include directives required by the emitted code.
    /// </param>
    /// <param name="preLowered">
    /// Optional pre-computed lowered body and context fields to avoid redundant AST lowering.
    /// </param>
    /// <returns>
    /// A string containing the complete emitted source code, including topologically sorted struct dependencies and the function definition.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="methodDecl"/> or <paramref name="semanticModel"/> is <see langword="null"/>.
    /// </exception>
    public static string GenerateCudaSource(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel,
        Action<Diagnostic>? reportDiagnostic, bool isDevice,
        ITranspilerDialect dialect, out HashSet<string> requiredIncludes,
        (BlockSyntax LoweredBody, IReadOnlyList<VariableDeclaratorSyntax> ContextFields)? preLowered = null)
    {
        using var session = TranspilationSession.Begin(methodDecl, semanticModel, reportDiagnostic, dialect);
        if (session.ErrorMessage != null)
        {
            requiredIncludes = new HashSet<string>();
            return session.ErrorMessage;
        }

        ref var context = ref session.Context;
        requiredIncludes = context.Emitter.RequiredIncludes;

        IReadOnlyList<VariableDeclaratorSyntax>? contextFields = null;
        BlockSyntax? loweredBody = null;

        if (!isDevice && !context.Emitter.Dialect.IsCuda && context.Emitter.IsGpuKernelEntry && methodDecl.Body != null)
        {
            var tuple = preLowered ?? AstLoweringPipeline.Lower(methodDecl.Body);
            loweredBody = tuple.LoweredBody;
            contextFields = tuple.ContextFields;
        }

        using var mainWriter = new SourceWriter();
        using var bodyWriter = new SourceWriter();

        context.Emitter.Writer = bodyWriter;

        if (contextFields != null)
        {
            var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl);
            var methodName = MethodSignatureTranspiler.GetKernelMethodName(methodDecl, methodSymbol);
            WriteContextStruct(methodName, contextFields, ref context);
        }

        MethodSignatureTranspiler.WriteMethodSignature(methodDecl, isDevice, contextFields != null, ref context);
        WriteMethodBody(methodDecl, loweredBody, ref context);

        if (!isDevice && !context.Emitter.Dialect.IsCuda)
        {
            HostLauncherTranspiler.WriteCpuLauncher(methodDecl, contextFields, ref context);
        }

        context.Emitter.Writer = mainWriter;
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
        if (session.ErrorMessage != null)
        {
            return session.ErrorMessage;
        }

        ref var context = ref session.Context;

        IReadOnlyList<VariableDeclaratorSyntax>? contextFields = null;
        if (!isDevice && !context.Emitter.Dialect.IsCuda && context.Emitter.IsGpuKernelEntry && methodDecl.Body != null)
        {
            var tuple = AstLoweringPipeline.Lower(methodDecl.Body);
            contextFields = tuple.ContextFields;
        }

        using var mainWriter = new SourceWriter();
        using var sigWriter = new SourceWriter();

        context.Emitter.Writer = sigWriter;

        if (contextFields != null)
        {
            var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl);
            var methodName = MethodSignatureTranspiler.GetKernelMethodName(methodDecl, methodSymbol);
            WriteContextStruct(methodName, contextFields, ref context);
        }

        MethodSignatureTranspiler.WriteMethodSignature(methodDecl, isDevice, contextFields != null, ref context);
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

    /// <summary>
    /// Transpiles a C# method declaration into a pure C# managed state machine and wrapper for host-side debugging.
    /// </summary>
    /// <param name="methodDecl">The Roslyn method declaration syntax node to transpile.</param>
    /// <param name="semanticModel">The semantic model bound to the compilation containing the method.</param>
    /// <param name="preLowered">Optional pre-computed lowered body and context fields to avoid redundant AST lowering.</param>
    /// <returns>A string containing the generated managed C# state machine source code.</returns>
    public static string GenerateManagedCSharpSource(
        MethodDeclarationSyntax methodDecl,
        SemanticModel semanticModel,
        (BlockSyntax LoweredBody, IReadOnlyList<VariableDeclaratorSyntax> ContextFields)? preLowered = null) =>
        ManagedCSharpTranspiler.Generate(methodDecl, semanticModel, preLowered);

    private static void WriteContextStruct(string methodName, IReadOnlyList<VariableDeclaratorSyntax> fields,
        ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine($"struct __gsharp_context_{methodName} {{");
        context.Emitter.Writer.AppendLine("int _state = 0;");
        context.Emitter.Writer.AppendLine($"__gsharp_context_{methodName}* _all_contexts = nullptr;");
        context.Emitter.Writer.AppendLine("uint64_t _warpExchange = 0;");
        context.Emitter.Writer.AppendLine("bool _yieldedForGridSync = false;");
        foreach (var field in fields)
        {
            var typeSyntax = (field.Parent as VariableDeclarationSyntax)?.Type;
            if (typeSyntax == null)
            {
                continue;
            }

            var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(typeSyntax);
            switch (typeSymbol)
            {
                case null when typeSyntax.IsVar:
                    // Fallback for dynamically generated AST nodes (e.g., _warTemp_X)
                    // Since they are not in the semantic model, default to 'int' for C++ (which allows implicit cast).
                    context.Emitter.Writer.Append("int");
                    context.Emitter.Writer.Append($" {field.Identifier.ValueText};");
                    context.Emitter.Writer.AppendLine();
                    continue;
                case null:
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
                context.Emitter.Writer.AppendLine("#define _yieldedForGridSync _ctx->_yieldedForGridSync");
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
                context.Emitter.Writer.AppendLine("#undef _yieldedForGridSync");
            }
        }
    }
}
