
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
        LoweredStateMachine? preLowered = null)
    {
        using var session = TranspilationSession.Begin(methodDecl, semanticModel, reportDiagnostic, dialect);
        if (session.ErrorMessage != null)
        {
            requiredIncludes = new HashSet<string>();
            return session.ErrorMessage;
        }

        ref var context = ref session.Context;
        requiredIncludes = context.Emitter.RequiredIncludes;

        var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl);
        var isSuspendingDevice = isDevice && IsLoweredSuspendingDeviceFunction(methodDecl, methodSymbol, ref context);
        context.Emitter.IsSuspendingDeviceFunction = isSuspendingDevice;

        LoweredStateMachine? lowered = null;
        if (!isDevice && context.Emitter.Dialect.EmulatesThreadsWithStateMachine() && context.Emitter.IsGpuKernelEntry && methodDecl.HasBody())
        {
            lowered = preLowered ?? AstLoweringPipeline.Lower(methodDecl, semanticModel);
        }
        else if (isSuspendingDevice)
        {
            lowered = AstLoweringPipeline.Lower(methodDecl, semanticModel, methodSymbol);
        }

        using var mainWriter = new SourceWriter();
        using var bodyWriter = new SourceWriter();

        context.Emitter.Writer = bodyWriter;

        if (lowered != null)
        {
            var prefix = isSuspendingDevice ? GSharpInternalNames.FrameTypePrefix : GSharpInternalNames.KernelContextPrefix;
            var frameName = MethodSignatureTranspiler.GetKernelDerivedName(prefix, methodDecl, methodSymbol);
            WriteContextStruct(frameName, methodDecl.Identifier.ValueText, lowered, methodSymbol, isSuspendingDevice,
                ref context);
        }

        if (isSuspendingDevice && lowered != null)
        {
            MethodSignatureTranspiler.WriteSuspendingDeviceSignature(methodDecl, lowered.CopiedParameters, ref context);
        }
        else
        {
            MethodSignatureTranspiler.WriteMethodSignature(methodDecl, isDevice, lowered != null, ref context);
        }

        WriteMethodBody(methodDecl, lowered, ref context);

        if (!isDevice && !context.Emitter.Dialect.IsCuda)
        {
            HostLauncherTranspiler.WriteCpuLauncher(methodDecl, lowered?.ContextFields, ref context);
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

        var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl);
        var isSuspendingDevice = isDevice && IsLoweredSuspendingDeviceFunction(methodDecl, methodSymbol, ref context);

        LoweredStateMachine? lowered = null;
        if (!isDevice && context.Emitter.Dialect.EmulatesThreadsWithStateMachine() && context.Emitter.IsGpuKernelEntry && methodDecl.HasBody())
        {
            lowered = AstLoweringPipeline.Lower(methodDecl, semanticModel);
        }

        using var mainWriter = new SourceWriter();
        using var sigWriter = new SourceWriter();

        context.Emitter.Writer = sigWriter;

        if (lowered != null)
        {
            var contextName = MethodSignatureTranspiler.GetKernelDerivedName(GSharpInternalNames.KernelContextPrefix,
                methodDecl, methodSymbol);
            WriteContextStruct(contextName, methodDecl.Identifier.ValueText, lowered, methodSymbol, isFrame: false,
                ref context);
        }

        if (isSuspendingDevice)
        {
            // The frame is defined with the body; the declaration only needs its name
            var frameName = MethodSignatureTranspiler.GetKernelDerivedName(GSharpInternalNames.FrameTypePrefix,
                methodDecl, methodSymbol);
            context.Emitter.Writer.AppendLine($"struct {frameName};");
            MethodSignatureTranspiler.WriteSuspendingDeviceSignature(methodDecl, [], ref context);
        }
        else
        {
            MethodSignatureTranspiler.WriteMethodSignature(methodDecl, isDevice, lowered != null, ref context);
        }

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
        LoweredStateMachine? preLowered = null) =>
        ManagedCSharpTranspiler.Generate(methodDecl, semanticModel, preLowered);

    /// <summary>
    /// Transpiles a device function that may suspend into a managed C# state machine and its frame class, or returns an
    /// empty string for any other device function.
    /// </summary>
    public static string GenerateManagedDeviceFunctionSource(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel) =>
        ManagedCSharpTranspiler.GenerateDeviceFunction(methodDecl, semanticModel);

    /// <summary>
    /// Whether <paramref name="methodDecl"/> is a device function that the CPU dialects lower into a state machine: one
    /// that reaches a barrier and is not recursive (GSH0061 rejects that).
    /// </summary>
    internal static bool IsLoweredSuspendingDeviceFunction(MethodDeclarationSyntax methodDecl, IMethodSymbol? methodSymbol,
        ref TranspileContext context) =>
        context.Emitter.Dialect.EmulatesThreadsWithStateMachine() && methodDecl.HasBody() && methodSymbol != null &&
        SuspensionAnalysis.RequiresStateMachine(methodSymbol, context.Query.SemanticModel.Compilation);

    // kernelName is the C# name, used only in internal-error reports. A kernel context also holds the warp exchange of the
    // thread; a device function frame holds its copied parameters instead and reaches the warp exchange through the root.
    private static void WriteContextStruct(string contextName, string kernelName, LoweredStateMachine lowered,
        IMethodSymbol? methodSymbol, bool isFrame, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine($"struct {contextName} {{");
        context.Emitter.Writer.AppendLine($"int {GSharpInternalNames.State} = 0;");
        if (!isFrame)
        {
            context.Emitter.Writer.AppendLine($"{contextName}* {GSharpInternalNames.AllContexts} = nullptr;");
            // Two exchange slots alternate per shuffle, so a lane that has already published its next shuffle does not
            // overwrite the value a slower lane is still reading
            context.Emitter.Writer.AppendLine($"uint64_t {GSharpInternalNames.WarpExchange}[2] = {{}};");
            context.Emitter.Writer.AppendLine($"uint32_t {GSharpInternalNames.WarpShuffleCount} = 0;");
            context.Emitter.Writer.AppendLine($"bool {GSharpInternalNames.YieldedForGridSync} = false;");
        }

        foreach (var parameter in GetCopiedParameters(lowered, methodSymbol))
        {
            StructTranspiler.RegisterUsedStructIfApplicable(parameter.Type, ref context);
            context.Emitter.Dialect.AppendType(ref context, parameter.Type);
            context.Emitter.Writer.AppendSpace()
                .AppendCppName(CppNameResolver.Resolve(parameter, parameter.Name, context.Emitter.Dialect))
                .AppendSemiColon().AppendLine();
        }

        foreach (var field in lowered.ContextFields)
        {
            // Field types come from HoistedFieldTypeResolver
            var typeSymbol = HoistedFieldTypeResolver.Resolve(field, context.Query.SemanticModel, kernelName);

            StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);
            context.Emitter.Dialect.AppendType(ref context, typeSymbol);
            WriteContextFieldName(field, ref context);
        }

        for (var i = 0; i < lowered.CallSites.Count; i++)
        {
            var callee = GetSuspendingCallee(lowered.CallSites[i], ref context);
            context.Emitter.Writer.AppendLine(
                $"{GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.FrameTypePrefix, callee)} {GSharpInternalNames.CallFramePrefix}{i};");
        }

        context.Emitter.Writer.AppendLine("};");
        context.Emitter.Writer.AppendLine();
    }

    internal static IMethodSymbol GetSuspendingCallee(InvocationExpressionSyntax callSite, ref TranspileContext context) =>
        context.Query.SemanticModel.GetSymbol(callSite) as IMethodSymbol
        ?? throw new InvalidOperationException("A call that may suspend does not resolve to a device function.");

    internal static ImmutableArray<IParameterSymbol> GetCopiedParameters(LoweredStateMachine lowered, IMethodSymbol? methodSymbol)
    {
        var parameters = ImmutableArray.CreateBuilder<IParameterSymbol>(lowered.CopiedParameters.Count);
        if (methodSymbol == null)
        {
            return parameters.ToImmutable();
        }

        foreach (var name in lowered.CopiedParameters)
        {
            foreach (var parameter in methodSymbol.Parameters)
            {
                if (parameter.Name == name)
                {
                    parameters.Add(parameter);
                }
            }
        }

        return parameters.ToImmutable();
    }

    private static void WriteContextFieldName(VariableDeclaratorSyntax field, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendSpace();
        CppNameResolver.AppendDeclaredName(field, field.Identifier, ref context);
        context.Emitter.Writer.AppendSemiColon().AppendLine();
    }

    private static void WriteMethodBody(MethodDeclarationSyntax method, LoweredStateMachine? lowered,
        ref TranspileContext context)
    {
        using (context.Emitter.Writer.Scope())
        {
            var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(method);
            var isKernelWithReturn = methodSymbol != null && methodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
                                     methodSymbol.ReturnType.SpecialType != SpecialType.System_Void;
            var isFrame = context.Emitter.IsSuspendingDeviceFunction;

            if (isKernelWithReturn)
            {
                context.Emitter.Writer.AppendLine(
                    $"unsigned int {GSharpInternalNames.GlobalThreadIndex} = (blockIdx.z * gridDim.y * gridDim.x + blockIdx.y * gridDim.x + blockIdx.x)" +
                    " * (blockDim.x * blockDim.y * blockDim.z) +" +
                    " threadIdx.z * blockDim.y * blockDim.x + threadIdx.y * blockDim.x + threadIdx.x;");
            }

            if (lowered != null)
            {
                // The grid sync flag belongs to the thread, so a device function sets it on the root
                var gridSyncOwner = isFrame ? GSharpInternalNames.Root : GSharpInternalNames.Context;
                context.Emitter.Writer.AppendLine(
                    $"#define {GSharpInternalNames.State} {GSharpInternalNames.Context}->{GSharpInternalNames.State}");
                context.Emitter.Writer.AppendLine(
                    $"#define {GSharpInternalNames.YieldedForGridSync} {gridSyncOwner}->{GSharpInternalNames.YieldedForGridSync}");

                if (!isFrame)
                {
                    // A kernel is the root of its own thread
                    context.Emitter.Writer.AppendLine($"auto* {GSharpInternalNames.Root} = {GSharpInternalNames.Context};");
                }

                WriteCopiedParameterInitialization(lowered, methodSymbol, ref context);
                context.Visitor.Visit(lowered.LoweredBody);

                if (isFrame)
                {
                    // Every state returns; this only satisfies the compiler
                    context.Emitter.Writer.AppendLine("return false;");
                }

                context.Emitter.Writer.AppendLine($"#undef {GSharpInternalNames.State}");
                context.Emitter.Writer.AppendLine($"#undef {GSharpInternalNames.YieldedForGridSync}");
                return;
            }

            if (method.Body != null)
            {
                context.Visitor.Visit(method.Body);
            }
            else if (method.ExpressionBody != null)
            {
                if (methodSymbol is { ReturnsVoid: false })
                {
                    context.Emitter.Writer.Append(isKernelWithReturn
                        ? $"{GSharpInternalNames.AutoResult}[{GSharpInternalNames.GlobalThreadIndex}] = "
                        : "return ");
                }

                context.Visitor.Visit(method.ExpressionBody.Expression);
                context.Emitter.Writer.AppendSemiColonLine();

                if (isKernelWithReturn)
                {
                    context.Emitter.Writer.AppendKeyword("return").AppendSemiColonLine();
                }
            }
        }
    }

    // On the first entry, the parameters the body assigns are copied into the frame; later resumes keep the frame's copy
    private static void WriteCopiedParameterInitialization(LoweredStateMachine lowered, IMethodSymbol? methodSymbol,
        ref TranspileContext context)
    {
        var parameters = GetCopiedParameters(lowered, methodSymbol);
        if (parameters.Length == 0)
        {
            return;
        }

        context.Emitter.Writer.AppendKeyword("if");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.Append($"{GSharpInternalNames.State}==0");
        }

        using (context.Emitter.Writer.Scope())
        {
            foreach (var parameter in parameters)
            {
                var name = CppNameResolver.Resolve(parameter, parameter.Name, context.Emitter.Dialect);
                context.Emitter.Writer.Append($"{GSharpInternalNames.Context}->").AppendCppName(name)
                    .Append($"={GSharpInternalNames.CopiedParameterPrefix}").AppendCppName(name)
                    .AppendSemiColon().AppendLine();
            }
        }
    }
}
