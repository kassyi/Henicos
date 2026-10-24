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
    /// <param name="reportFailure">
    /// An optional callback that receives the constructs that could not be emitted (<see cref="TranspileFailure"/>); the
    /// transpiler reports no diagnostics itself.
    /// </param>
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
    /// <returns>
    /// A string containing the complete emitted source code, including topologically sorted struct dependencies and the function definition.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="methodDecl"/> or <paramref name="semanticModel"/> is <see langword="null"/>.
    /// </exception>
    public static string GenerateCudaSource(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel,
        Action<TranspileFailure>? reportFailure, bool isDevice,
        ITranspilerDialect dialect, out HashSet<string> requiredIncludes) =>
        GenerateCudaSource(MethodEmissionPlan.Create(methodDecl, semanticModel, isKernel: !isDevice), reportFailure,
            dialect, out requiredIncludes);

    /// <summary>
    /// Transpiles the method of <paramref name="plan"/> into a complete target dialect implementation string, including
    /// dependent struct definitions, parameter qualifiers, and the method body.
    /// </summary>
    /// <param name="plan">The classified and, where needed, lowered method.</param>
    /// <param name="reportFailure">An optional callback that receives the constructs that could not be emitted (<see cref="TranspileFailure"/>).</param>
    /// <param name="dialect">The target language dialect to use for code generation.</param>
    /// <param name="requiredIncludes">
    /// When this method returns, contains the set of header include directives required by the emitted code.
    /// </param>
    internal static string GenerateCudaSource(in MethodEmissionPlan plan, Action<TranspileFailure>? reportFailure,
        ITranspilerDialect dialect, out HashSet<string> requiredIncludes)
    {
        if (plan.Kind == MethodEmissionKind.Iterator)
        {
            if (reportFailure != null && plan.Declaration.Body != null)
            {
                void ReportNonYieldFailure(TranspileFailure failure)
                {
                    if (failure.Descriptor.Id == GSharpDiagnostics.UnsupportedSyntax.Id &&
                        failure.MessageArgs is
                            [nameof(SyntaxKind.YieldReturnStatement) or nameof(SyntaxKind.YieldBreakStatement)])
                    {
                        return;
                    }

                    reportFailure(failure);
                }

                using var iteratorSession = TranspilationSession.Begin(in plan, ReportNonYieldFailure, dialect);
                if (iteratorSession.ErrorMessage == null)
                {
                    using var dummyWriter = new SourceWriter();
                    iteratorSession.Context.Emitter.Writer = dummyWriter;
                    iteratorSession.Context.Visitor.Visit(plan.Declaration.Body);
                }
            }

            requiredIncludes = [];
            return "";
        }

        using var session = TranspilationSession.Begin(in plan, reportFailure, dialect);
        if (session.ErrorMessage != null)
        {
            requiredIncludes = [];
            return session.ErrorMessage;
        }

        ref var context = ref session.Context;
        requiredIncludes = context.Emitter.RequiredIncludes;

        var methodDecl = plan.Declaration;
        var isDevice = plan.Kind != MethodEmissionKind.Kernel;
        var isSuspendingDevice = plan.IsSuspendingOn(dialect);
        context.Emitter.IsSuspendingDeviceFunction = isSuspendingDevice;

        var lowered = plan.GetLoweredFor(dialect);

        using var mainWriter = new SourceWriter();
        using var bodyWriter = new SourceWriter();
        using var disposeWriter = new SourceWriter();

        context.Emitter.Writer = bodyWriter;

        // A plan with a state machine always has a symbol
        if (lowered != null && plan.Symbol is { } loweredSymbol)
        {
            var prefix = isSuspendingDevice
                ? GSharpInternalNames.SuspendingCall.FrameTypePrefix
                : GSharpInternalNames.Kernel.ContextPrefix;
            var frameName = GlobalCppNames.GetKernelDerivedName(prefix, loweredSymbol);
            WriteContextStruct(frameName, methodDecl.Identifier.ValueText, lowered, in plan, isSuspendingDevice,
                ref context);
        }

        if (isSuspendingDevice && lowered != null && plan.Symbol is { } suspendingSymbol)
        {
            MethodSignatureTranspiler.WriteSuspendingDeviceSignature(
                methodDecl, suspendingSymbol, lowered.CopiedParameters, ref context);
        }
        else
        {
            MethodSignatureTranspiler.WriteMethodSignature(in plan, lowered != null, ref context);
        }

        WriteMethodBody(in plan, lowered, ref context);

        if (!isDevice && !context.Emitter.Dialect.IsCuda)
        {
            HostLauncherTranspiler.WriteCpuLauncher(in plan, lowered?.ContextFields, ref context);
        }

        // Before the structs: a Dispose body can use more of them
        context.Emitter.Writer = disposeWriter;
        UsingTranspiler.WriteDisposeDefinitions(ref context);

        context.Emitter.Writer = mainWriter;
        var sortedStructs = StructTranspiler.GetTopologicallySortedStructs(ref context);
        foreach (var structSym in sortedStructs)
        {
            StructTranspiler.WriteStructDefinition(structSym, ref context);
        }

        return mainWriter + disposeWriter.ToString() + bodyWriter.ToString();
    }

    /// <summary>
    /// Transpiles the device function of <paramref name="plan"/> into a target dialect forward declaration signature,
    /// along with forward declarations of referenced struct types.
    /// </summary>
    /// <param name="plan">The classified device function.</param>
    /// <param name="reportFailure">An optional callback that receives the constructs that could not be emitted (<see cref="TranspileFailure"/>).</param>
    /// <param name="dialect">The target language dialect to use for code generation.</param>
    /// <returns>
    /// A string containing the forward declaration statements suitable for header inclusion.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="plan"/> is a kernel, which has no forward declaration.</exception>
    internal static string GenerateCudaSignature(in MethodEmissionPlan plan, Action<TranspileFailure>? reportFailure,
        ITranspilerDialect dialect)
    {
        if (plan.Kind == MethodEmissionKind.Kernel)
        {
            throw new ArgumentException("Only a device function has a forward declaration.", nameof(plan));
        }

        if (plan.Kind == MethodEmissionKind.Iterator)
        {
            return "";
        }

        using var session = TranspilationSession.Begin(in plan, reportFailure, dialect);
        if (session.ErrorMessage != null)
        {
            return session.ErrorMessage;
        }

        ref var context = ref session.Context;

        using var mainWriter = new SourceWriter();
        using var sigWriter = new SourceWriter();

        context.Emitter.Writer = sigWriter;

        // A suspending device function always has a symbol
        if (plan.IsSuspendingOn(dialect) && plan.Symbol is { } methodSymbol)
        {
            // The frame is defined with the body; the declaration only needs its name
            var frameName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.SuspendingCall.FrameTypePrefix,
                methodSymbol);
            context.Emitter.Writer.AppendLine($"struct {frameName};");
            MethodSignatureTranspiler.WriteSuspendingDeviceSignature(plan.Declaration, methodSymbol, [], ref context);
        }
        else
        {
            MethodSignatureTranspiler.WriteMethodSignature(in plan, hasContext: false, ref context);
        }

        context.Emitter.Writer.AppendLine(";");

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
    /// Transpiles the kernel of <paramref name="plan"/> into a pure C# managed state machine and wrapper for host-side debugging.
    /// </summary>
    /// <returns>A string containing the generated managed C# state machine source code.</returns>
    internal static string GenerateManagedCSharpSource(in MethodEmissionPlan plan) =>
        ManagedCSharpTranspiler.Generate(in plan);

    /// <summary>
    /// Transpiles a device function that may suspend into a managed C# state machine and its frame class, or returns an
    /// empty string for any other device function.
    /// </summary>
    internal static string GenerateManagedDeviceFunctionSource(in MethodEmissionPlan plan) =>
        ManagedCSharpTranspiler.GenerateDeviceFunction(in plan);

    // kernelName is the C# name, used only in internal-error reports. A kernel context also holds the warp exchange of the
    // thread; a device function frame holds its copied parameters instead and reaches the warp exchange through the root.
    private static void WriteContextStruct(string contextName, string kernelName, LoweredStateMachine lowered,
        in MethodEmissionPlan plan, bool isFrame, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine($"struct {contextName} {{");
        context.Emitter.Writer.AppendLine($"int {GSharpInternalNames.StateMachine.State} = 0;");
        if (!isFrame)
        {
            context.Emitter.Writer.AppendLine($"{contextName}* {GSharpInternalNames.StateMachine.AllContexts} = nullptr;");
            // Two exchange slots alternate per shuffle, so a lane that has already published its next shuffle does not
            // overwrite the value a slower lane is still reading
            context.Emitter.Writer.AppendLine($"uint64_t {GSharpInternalNames.StateMachine.WarpExchange}[2] = {{}};");
            context.Emitter.Writer.AppendLine($"uint32_t {GSharpInternalNames.StateMachine.WarpShuffleCount} = 0;");
            context.Emitter.Writer.AppendLine($"bool {GSharpInternalNames.StateMachine.YieldedForGridSync} = false;");
        }

        foreach (var parameter in plan.GetCopiedParameters(lowered))
        {
            StructTranspiler.RegisterUsedStructIfApplicable(parameter.Type, ref context);
            context.Emitter.Dialect.AppendType(ref context, parameter.Type);
            context.Emitter.Writer.Append(' ')
                .AppendCppName(CppNameResolver.Resolve(parameter, parameter.Name, context.Emitter.Dialect))
                .AppendLine(";");
        }

        foreach (var field in lowered.ContextFields)
        {
            // Field types come from HoistedFieldTypeResolver
            var typeSymbol = HoistedFieldTypeResolver.Resolve(field, context.Query.SemanticModel, kernelName);

            StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);
            context.Emitter.Dialect.AppendType(ref context, typeSymbol);
            context.Emitter.Writer.Append(' ');
            CppNameResolver.AppendDeclaredName(field, field.Identifier, ref context);
            context.Emitter.Writer.AppendLine(";");
        }

        for (var i = 0; i < lowered.CallSites.Count; i++)
        {
            var callee = GetSuspendingCallee(lowered.CallSites[i], ref context);
            context.Emitter.Writer.AppendLine(
                $"{GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.SuspendingCall.FrameTypePrefix, callee)} {GSharpInternalNames.SuspendingCall.CallFramePrefix}{i};");
        }

        context.Emitter.Writer.AppendLine("};");
        context.Emitter.Writer.AppendLine();
    }

    internal static IMethodSymbol GetSuspendingCallee(InvocationExpressionSyntax callSite, ref TranspileContext context) =>
        context.Query.SemanticModel.GetSymbol(callSite) as IMethodSymbol
        ?? throw new InvalidOperationException("A call that may suspend does not resolve to a device function.");

    private static void WriteMethodBody(in MethodEmissionPlan plan, LoweredStateMachine? lowered,
        ref TranspileContext context)
    {
        using (context.Emitter.Writer.Scope())
        {
            var method = plan.Declaration;
            var methodSymbol = plan.Symbol;
            var isKernelWithReturn = plan.HasAutoResult;
            var isFrame = context.Emitter.IsSuspendingDeviceFunction;

            if (isKernelWithReturn)
            {
                context.Emitter.Writer.AppendLine(
                    $"unsigned int {GSharpInternalNames.ReturnValue.GlobalThreadIndex} = (blockIdx.z * gridDim.y * gridDim.x + blockIdx.y * gridDim.x + blockIdx.x)" +
                    " * (blockDim.x * blockDim.y * blockDim.z) +" +
                    " threadIdx.z * blockDim.y * blockDim.x + threadIdx.y * blockDim.x + threadIdx.x;");
            }

            if (lowered != null)
            {
                // The grid sync flag belongs to the thread, so a device function sets it on the root
                var gridSyncOwner = isFrame ? GSharpInternalNames.SuspendingCall.Root : GSharpInternalNames.StateMachine.Context;
                context.Emitter.Writer.AppendLine(
                    $"#define {GSharpInternalNames.StateMachine.State} {GSharpInternalNames.StateMachine.Context}->{GSharpInternalNames.StateMachine.State}");
                context.Emitter.Writer.AppendLine(
                    $"#define {GSharpInternalNames.StateMachine.YieldedForGridSync} {gridSyncOwner}->{GSharpInternalNames.StateMachine.YieldedForGridSync}");

                if (!isFrame)
                {
                    // A kernel is the root of its own thread
                    context.Emitter.Writer.AppendLine($"auto* {GSharpInternalNames.SuspendingCall.Root} = {GSharpInternalNames.StateMachine.Context};");
                }

                WriteCopiedParameterInitialization(lowered, in plan, ref context);
                context.Visitor.Visit(lowered.LoweredBody);

                if (isFrame)
                {
                    // Every state returns; this only satisfies the compiler
                    context.Emitter.Writer.AppendLine("return false;");
                }

                context.Emitter.Writer.AppendLine($"#undef {GSharpInternalNames.StateMachine.State}");
                context.Emitter.Writer.AppendLine($"#undef {GSharpInternalNames.StateMachine.YieldedForGridSync}");
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
                        ? $"{GSharpInternalNames.ReturnValue.Buffer}[{GSharpInternalNames.ReturnValue.GlobalThreadIndex}] = "
                        : "return ");
                }

                context.Visitor.Visit(method.ExpressionBody.Expression);
                context.Emitter.Writer.AppendLine(";");

                if (isKernelWithReturn)
                {
                    context.Emitter.Writer.AppendLine("return;");
                }
            }
        }
    }

    // On the first entry, the parameters the body assigns are copied into the frame; later resumes keep the frame's copy
    private static void WriteCopiedParameterInitialization(LoweredStateMachine lowered, in MethodEmissionPlan plan,
        ref TranspileContext context)
    {
        var parameters = plan.GetCopiedParameters(lowered);
        if (parameters.Length == 0)
        {
            return;
        }

        context.Emitter.Writer.Append("if");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.Append($"{GSharpInternalNames.StateMachine.State}==0");
        }

        using (context.Emitter.Writer.Scope())
        {
            foreach (var parameter in parameters)
            {
                var name = CppNameResolver.Resolve(parameter, parameter.Name, context.Emitter.Dialect);
                context.Emitter.Writer.Append($"{GSharpInternalNames.StateMachine.Context}->").AppendCppName(name)
                    .Append($"={GSharpInternalNames.SuspendingCall.CopiedParameterPrefix}").AppendCppName(name)
                    .AppendLine(";");
            }
        }
    }
}
