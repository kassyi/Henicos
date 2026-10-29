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
    /// <param name="structSources">
    /// When given, receives each struct definition and <c>Dispose</c> definition written in front of the method, one by
    /// one (a device code export records them apart from the function). The returned text is the same either way.
    /// </param>
    internal static string GenerateCudaSource(in MethodEmissionPlan plan, Action<TranspileFailure>? reportFailure,
        ITranspilerDialect dialect, out HashSet<string> requiredIncludes, StructSourceCollector? structSources = null)
    {
        if (plan.Kind == MethodEmissionKind.Iterator)
        {
            ReportStandaloneIterator(in plan, reportFailure, dialect);
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
        context.Emitter.IsSuspendingDeviceFunction = plan.IsSuspendingOn(dialect);

        var lowered = plan.GetLoweredFor(dialect);
        context.Emitter.IteratorExpansionCounter = lowered?.RegionCount ?? 0;
        var resumeLocals = ImmutableArray<ResumeLocal>.Empty;
        if (lowered != null)
        {
            (lowered, resumeLocals) = ResumeLocalEmitter.Separate(lowered, in plan, context.Emitter.Dialect);
        }

        using var mainWriter = new SourceWriter();
        using var bodyWriter = new SourceWriter();
        using var disposeWriter = new SourceWriter();

        context.Emitter.Writer = bodyWriter;
        DynamicSharedMemoryEmitter.BeginMethod(in plan, ref context);
        WriteStateMachineHeader(in plan, lowered, ref context);
        WriteMethodBody(in plan, lowered, resumeLocals, ref context);

        // The struct members and Dispose bodies written below have no table of dynamic shared memory regions
        context.Emitter.DynamicSharedMemorySlotScope = null;

        if (plan.Kind == MethodEmissionKind.Kernel && !context.Emitter.Dialect.IsCuda)
        {
            HostLauncherTranspiler.WriteCpuLauncher(in plan, lowered?.ContextFields, ref context);
        }

        WriteStructsAndDisposes(mainWriter, disposeWriter, structSources, ref context);
        return mainWriter + disposeWriter.ToString() + bodyWriter.ToString();
    }

    // An iterator is inlined into its consumer and emits nothing itself. It is still transpiled once into a discarded
    // writer, so that what it cannot emit is reported; its consumer passes the table of dynamic shared memory regions on.
    private static void ReportStandaloneIterator(in MethodEmissionPlan plan, Action<TranspileFailure>? reportFailure,
        ITranspilerDialect dialect)
    {
        if (reportFailure == null || plan.Declaration.Body == null || plan.Symbol == null)
        {
            return;
        }

        using var iteratorSession = TranspilationSession.Begin(in plan, reportFailure, dialect);
        if (iteratorSession.ErrorMessage != null)
        {
            return;
        }

        using var dummyWriter = new SourceWriter();
        ref var iteratorContext = ref iteratorSession.Context;
        iteratorContext.Emitter.Writer = dummyWriter;
        DynamicSharedMemoryEmitter.BeginMethod(in plan, ref iteratorContext);
        var expanded = IteratorInliner.ExpandStandalone(
            plan.Declaration,
            plan.Symbol,
            plan.SemanticModel,
            ref iteratorContext.Emitter.IteratorExpansionCounter,
            ref iteratorContext.Emitter.ArgumentTempCounter,
            dialect,
            iteratorContext.Reporter);
        iteratorContext.Visitor.Visit(expanded);
    }

    // The frame struct of a state machine (a plan with one always has a symbol), then the signature of the function
    private static void WriteStateMachineHeader(in MethodEmissionPlan plan, LoweredStateMachine? lowered,
        ref TranspileContext context)
    {
        var isSuspendingDevice = context.Emitter.IsSuspendingDeviceFunction;
        if (lowered != null && plan.Symbol is { } loweredSymbol)
        {
            var prefix = isSuspendingDevice
                ? GSharpInternalNames.SuspendingCall.FrameTypePrefix
                : GSharpInternalNames.Kernel.ContextPrefix;
            var frameName = GlobalCppNames.GetKernelDerivedName(prefix, loweredSymbol);
            WriteContextStruct(frameName, plan.Declaration.Identifier.ValueText, lowered, in plan, isSuspendingDevice,
                ref context);
        }

        if (isSuspendingDevice && lowered != null && plan.Symbol is { } suspendingSymbol)
        {
            MethodSignatureTranspiler.WriteSuspendingDeviceSignature(
                plan.Declaration, suspendingSymbol, lowered.CopiedParameters, ref context);
        }
        else
        {
            MethodSignatureTranspiler.WriteMethodSignature(in plan, lowered != null, ref context,
                lowered?.CopiedParameters);
        }
    }

    // The Dispose definitions into disposeWriter, then the struct definitions into mainWriter, each also recorded into
    // structSources when it is given
    private static void WriteStructsAndDisposes(SourceWriter mainWriter, SourceWriter disposeWriter,
        StructSourceCollector? structSources, ref TranspileContext context)
    {
        // Before the structs: a Dispose body can use more of them
        context.Emitter.Writer = disposeWriter;
        UsingTranspiler.WriteDisposeDefinitions(ref context, structSources?.Disposes);

        // After the Dispose parts, which an export cuts off the front of the text: these stay with the method's text
        SortComparisonMembers.WriteDefinitions(ref context);

        context.Emitter.Writer = mainWriter;
        var sortedStructs = StructTranspiler.GetTopologicallySortedStructs(ref context);
        foreach (var structSym in sortedStructs)
        {
            if (structSources == null)
            {
                StructTranspiler.WriteStructDefinition(structSym, ref context);
                continue;
            }

            var part = StructTranspiler.WriteDefinitionPart(structSym, ref context, isolate: false);
            mainWriter.Append(part.Text);
            structSources.Definitions.Add(part);
        }

        // The structs that the definitions name (a constructor's parameter, an operator's result) without the method
        // defining them, for the export only
        if (structSources != null)
        {
            StructTranspiler.WriteDiscoveredDefinitions(ref context, structSources);
        }
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
    // thread; a device function frame reaches the warp exchange through the root. Both hold their copied parameters.
    private static void WriteContextStruct(string contextName, string kernelName, LoweredStateMachine lowered,
        in MethodEmissionPlan plan, bool isFrame, ref TranspileContext context)
    {
        using (context.Emitter.Writer.Scope($"struct {contextName} {{", "};"))
        {
            context.Emitter.Writer.AppendLine($"int {GSharpInternalNames.StateMachine.State} = 0;");
            if (!isFrame)
            {
                WriteThreadStateFields(contextName, ref context);
            }

            foreach (var parameter in plan.GetCopiedParameters(lowered))
            {
                WriteCopiedParameterMember(parameter, ref context);
            }

            foreach (var field in lowered.ContextFields)
            {
                // Field types come from HoistedFieldTypeResolver
                var typeSymbol = HoistedFieldTypeResolver.Resolve(field, context.Query.SemanticModel, kernelName);
                WriteMember(typeSymbol, CppNameResolver.Resolve(field, context.Query.SemanticModel, context.Emitter.Dialect),
                    ref context);
            }

            for (var i = 0; i < lowered.CallSites.Count; i++)
            {
                var callee = GetSuspendingCallee(lowered.CallSites[i], ref context);
                context.Emitter.Writer.AppendLine(
                    $"{GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.SuspendingCall.FrameTypePrefix, callee)} {GSharpInternalNames.SuspendingCall.CallFramePrefix}{i};");
            }
        }

        context.Emitter.Writer.AppendLine();
    }

    // The state of the thread that only a kernel context holds: the contexts of the block, the warp exchange and the
    // flags of a grid sync and a block barrier
    private static void WriteThreadStateFields(string contextName, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine($"{contextName}* {GSharpInternalNames.StateMachine.AllContexts} = nullptr;");
        // Two exchange slots alternate per shuffle, so a lane that has already published its next shuffle does not
        // overwrite the value a slower lane is still reading
        context.Emitter.Writer.AppendLine($"uint64_t {GSharpInternalNames.StateMachine.WarpExchange}[2] = {{}};");
        context.Emitter.Writer.AppendLine($"uint32_t {GSharpInternalNames.StateMachine.WarpShuffleCount} = 0;");
        context.Emitter.Writer.AppendLine($"bool {GSharpInternalNames.StateMachine.YieldedForGridSync} = false;");
        context.Emitter.Writer.AppendLine($"bool {GSharpInternalNames.StateMachine.YieldedForBlockBarrier} = false;");
    }

    // <parameter type> <name>; typed as the parameter is declared, so that a [GpuReadOnly] array keeps its const
    // elements and the copy on the first entry compiles
    private static void WriteCopiedParameterMember(IParameterSymbol parameter, ref TranspileContext context)
    {
        var name = CppNameResolver.Resolve(parameter, parameter.Name, context.Emitter.Dialect);
        if (!parameter.TryGetDeclaringSyntax<ParameterSyntax>(out var syntax))
        {
            WriteMember(parameter.Type, name, ref context);
            return;
        }

        var type = parameter.Type.UnwrapNullableArrayLike();
        StructTranspiler.RegisterUsedStructIfApplicable(type, ref context);
        context.Emitter.Dialect.AppendParameter(ref context, syntax, type, parameter);
        context.Emitter.Writer.Append(' ').AppendCppName(name).AppendLine(";");
    }

    // <type> <name>;
    private static void WriteMember(ITypeSymbol type, in CppName name, ref TranspileContext context)
    {
        StructTranspiler.RegisterUsedStructIfApplicable(type, ref context);
        context.Emitter.Dialect.AppendType(ref context, type);
        context.Emitter.Writer.Append(' ').AppendCppName(name).AppendLine(";");
    }

    internal static IMethodSymbol GetSuspendingCallee(InvocationExpressionSyntax callSite, ref TranspileContext context) =>
        context.Query.SemanticModel.GetSymbol(callSite) as IMethodSymbol
        ?? throw new InvalidOperationException("A call that may suspend does not resolve to a device function.");

    private static void WriteMethodBody(in MethodEmissionPlan plan, LoweredStateMachine? lowered,
        ImmutableArray<ResumeLocal> resumeLocals, ref TranspileContext context)
    {
        using (context.Emitter.Writer.Scope())
        {
            DynamicSharedMemoryEmitter.WriteKernelPrologue(in plan, ref context);

            if (plan.HasAutoResult)
            {
                context.Emitter.Writer.AppendLine(
                    $"unsigned int {GSharpInternalNames.ReturnValue.GlobalThreadIndex} = (blockIdx.z * gridDim.y * gridDim.x + blockIdx.y * gridDim.x + blockIdx.x)" +
                    " * (blockDim.x * blockDim.y * blockDim.z) +" +
                    " threadIdx.z * blockDim.y * blockDim.x + threadIdx.y * blockDim.x + threadIdx.x;");
            }

            if (lowered != null)
            {
                WriteStateMachineBody(in plan, lowered, resumeLocals, ref context);
            }
            else if (plan.Declaration.Body != null)
            {
                context.Visitor.Visit(plan.Declaration.Body);
            }
            else if (plan.Declaration.ExpressionBody != null)
            {
                WriteExpressionBody(in plan, plan.Declaration.ExpressionBody, ref context);
            }
        }
    }

    // The lowered body with the macros that name the state and the grid sync flag, after the copied parameters and the
    // storage of the resume locals
    private static void WriteStateMachineBody(in MethodEmissionPlan plan, LoweredStateMachine lowered,
        ImmutableArray<ResumeLocal> resumeLocals, ref TranspileContext context)
    {
        var isFrame = context.Emitter.IsSuspendingDeviceFunction;
        // The grid sync flag belongs to the thread, so a device function sets it on the root
        var gridSyncOwner = isFrame ? GSharpInternalNames.SuspendingCall.Root : GSharpInternalNames.StateMachine.Context;
        context.Emitter.Writer.AppendLine(
            $"#define {GSharpInternalNames.StateMachine.State} {GSharpInternalNames.StateMachine.Context}->{GSharpInternalNames.StateMachine.State}");
        context.Emitter.Writer.AppendLine(
            $"#define {GSharpInternalNames.StateMachine.YieldedForGridSync} {gridSyncOwner}->{GSharpInternalNames.StateMachine.YieldedForGridSync}");
        context.Emitter.Writer.AppendLine(
            $"#define {GSharpInternalNames.StateMachine.YieldedForBlockBarrier} {gridSyncOwner}->{GSharpInternalNames.StateMachine.YieldedForBlockBarrier}");

        if (!isFrame)
        {
            // A kernel is the root of its own thread
            context.Emitter.Writer.AppendLine($"auto* {GSharpInternalNames.SuspendingCall.Root} = {GSharpInternalNames.StateMachine.Context};");
        }

        WriteCopiedParameterInitialization(lowered, in plan, ref context);
        ResumeLocalEmitter.WriteDeclarations(resumeLocals, ref context);
        context.Visitor.Visit(lowered.LoweredBody);

        if (isFrame)
        {
            // Every state returns; this only satisfies the compiler
            context.Emitter.Writer.AppendLine("return false;");
        }

        context.Emitter.Writer.AppendLine($"#undef {GSharpInternalNames.StateMachine.State}");
        context.Emitter.Writer.AppendLine($"#undef {GSharpInternalNames.StateMachine.YieldedForGridSync}");
        context.Emitter.Writer.AppendLine($"#undef {GSharpInternalNames.StateMachine.YieldedForBlockBarrier}");
    }

    // => <expression>: returned, or stored into the result buffer of a kernel with a return value
    private static void WriteExpressionBody(in MethodEmissionPlan plan, ArrowExpressionClauseSyntax expressionBody,
        ref TranspileContext context)
    {
        if (plan.Symbol is { ReturnsVoid: false })
        {
            context.Emitter.Writer.Append(plan.HasAutoResult
                ? $"{GSharpInternalNames.ReturnValue.Buffer}[{GSharpInternalNames.ReturnValue.GlobalThreadIndex}] = "
                : "return ");
        }

        context.Visitor.Visit(expressionBody.Expression);
        context.Emitter.Writer.AppendLine(";");

        if (plan.HasAutoResult)
        {
            context.Emitter.Writer.AppendLine("return;");
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
                    .Append($"={GSharpInternalNames.StateMachine.CopiedParameterPrefix}").AppendCppName(name)
                    .AppendLine(";");
            }
        }
    }
}
