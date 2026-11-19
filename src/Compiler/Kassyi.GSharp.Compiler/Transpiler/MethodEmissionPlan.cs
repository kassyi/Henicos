namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The classification and the lowered state machine of one method, shared by the CUDA, CPU C++ and managed C# outputs.
/// </summary>
/// <remarks>
/// <see cref="Create"/> is the only place that decides whether a device function suspends and the only place that decides
/// to lower a method, so every output sees the same decision and the method is lowered at most once. The lowering runs
/// when an output first needs the state machine, so a caller that emits only for hardware threads (the analyzer,
/// <see cref="TranspilerFailureCheck"/>) never lowers. A plan lives within one generator transform or analyzer callback; it
/// is not part of the incremental pipeline models.
/// </remarks>
internal readonly struct MethodEmissionPlan
{
    private readonly DeferredLowering? _lowering;

    private MethodEmissionPlan(MethodDeclarationSyntax declaration, SemanticModel semanticModel, MethodEmissionKind kind,
        IMethodSymbol? symbol, DeferredLowering? lowering, bool isGpuKernelEntry, bool hasAutoResult,
        KernelTwoFormsPlan twoFormsPlan = default)
    {
        Declaration = declaration;
        SemanticModel = semanticModel;
        Kind = kind;
        Symbol = symbol;
        _lowering = lowering;
        IsGpuKernelEntry = isGpuKernelEntry;
        HasAutoResult = hasAutoResult;
        TwoFormsPlan = twoFormsPlan;
    }

    // Shared by the copies of the plan (it is passed by value), so the method is lowered once for each form
    // The owner is the symbol of the declaration, kernel or device function, whose written parameters live in the frame
    private sealed class DeferredLowering(MethodDeclarationSyntax declaration, SemanticModel semanticModel,
        IMethodSymbol owner, bool isDeviceFunction)
    {
        private LoweredStateMachine? _lowered;
        private StackLoweredBody? _stackBody;

        public LoweredStateMachine Value =>
            _lowered ??= AstLoweringPipeline.Lower(declaration, semanticModel, owner, isDeviceFunction);

        public StackLoweredBody StackBody =>
            _stackBody ??= AstLoweringPipeline.LowerForStack(declaration, semanticModel, owner, isDeviceFunction);
    }

    public MethodDeclarationSyntax Declaration { get; }

    public SemanticModel SemanticModel { get; }

    public MethodEmissionKind Kind { get; }

    /// <summary>
    /// The declared method. Never <see langword="null"/> when <see cref="Lowered"/> is set.
    /// </summary>
    public IMethodSymbol? Symbol { get; }

    /// <summary>
    /// The state machine of a kernel with a body, or of a <see cref="MethodEmissionKind.SuspendingDeviceFunction"/>;
    /// <see langword="null"/> otherwise. The dialects with hardware threads do not use it. Reading it lowers the method the
    /// first time.
    /// </summary>
    public LoweredStateMachine? Lowered => _lowering?.Value;

    /// <summary>
    /// The body for the CPU stack models, with the reconvergence regions of a method that reaches
    /// <c>Warp.ActiveMask()</c> where <see cref="Lowered"/> puts them (<see cref="AstLoweringPipeline.LowerForStack"/>),
    /// for the same methods as <see cref="Lowered"/>. Reading it lowers the method for the stacks the first time, apart
    /// from <see cref="Lowered"/>, so an output that reads only one of them pays for one lowering.
    /// </summary>
    public StackLoweredBody? StackBody => _lowering?.StackBody;

    /// <summary>The method has <c>[GpuKernel]</c>.</summary>
    public bool IsGpuKernelEntry { get; }

    /// <summary>The plan for two-form emission (by-value and by-address) of kernels with aliasing in parameters.</summary>
    public KernelTwoFormsPlan TwoFormsPlan { get; }

    /// <summary>
    /// The method is a <c>[GpuKernel]</c> that returns a value, which each thread writes into the auto result buffer.
    /// </summary>
    public bool HasAutoResult { get; }

    /// <summary>
    /// The method is a device function that does not suspend and gets a managed version, which the managed bodies G#
    /// emits call instead of it (<see cref="ManagedVersionAnalysis.HasManagedVersion"/>, #711). The C++ outputs do not
    /// read it.
    /// </summary>
    public bool HasManagedVersion =>
        Kind is MethodEmissionKind.DeviceFunction or MethodEmissionKind.Iterator && Symbol is { } symbol &&
        Declaration.HasBody() && ManagedVersionAnalysis.HasManagedVersion(symbol, SemanticModel.Compilation);

    /// <summary>The method returns <see langword="void"/>; <see langword="false"/> when it has no symbol.</summary>
    public bool ReturnsVoid => Symbol is { ReturnType.SpecialType: SpecialType.System_Void };

    /// <summary>The state machine that <paramref name="dialect"/> emits for this method, if any.</summary>
    public LoweredStateMachine? GetLoweredFor(ITranspilerDialect dialect) =>
        dialect.EmulatesThreadsWithStateMachine() ? Lowered : null;

    /// <summary>Whether <paramref name="dialect"/> emits this method as a suspending device function with a frame.</summary>
    public bool IsSuspendingOn(ITranspilerDialect dialect) =>
        Kind == MethodEmissionKind.SuspendingDeviceFunction && dialect.EmulatesThreadsWithStateMachine();

    /// <summary>
    /// The by-value parameters that the state machine copies into its frame, as symbols of the method, in the order of
    /// <see cref="LoweredStateMachine.CopiedParameters"/>.
    /// </summary>
    public ImmutableArray<IParameterSymbol> GetCopiedParameters(LoweredStateMachine lowered)
    {
        var parameters = ImmutableArray.CreateBuilder<IParameterSymbol>(lowered.CopiedParameters.Count);
        if (Symbol == null)
        {
            return parameters.ToImmutable();
        }

        lowered.CopiedParameters
            .AsStackEnumerable()
            .Do(
                (Method: Symbol, Result: parameters),
                static (name, s) => s.Method.Parameters
                    .AsStackEnumerable()
                    .AddIf(s.Result, name, static (parameter, target) => parameter.Name == target)
            );

        return parameters.ToImmutable();
    }

    /// <summary>Classifies <paramref name="declaration"/> and lowers it when one of the outputs needs a state machine.</summary>
    /// <param name="declaration">The method to emit.</param>
    /// <param name="semanticModel">The semantic model of the compilation that contains the method.</param>
    /// <param name="isKernel">
    /// <see langword="true"/> to emit the method as a kernel entry point; <see langword="false"/> to emit it as a device
    /// function.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="declaration"/> or <paramref name="semanticModel"/> is <see langword="null"/>.
    /// </exception>
    public static MethodEmissionPlan Create(MethodDeclarationSyntax declaration, SemanticModel semanticModel, bool isKernel)
    {
        if (declaration is null)
        {
            throw new ArgumentNullException(nameof(declaration));
        }

        if (semanticModel is null)
        {
            throw new ArgumentNullException(nameof(semanticModel));
        }

        var symbol = semanticModel.GetDeclaredSymbol<IMethodSymbol>(declaration);
        var isGpuKernelEntry = symbol != null && symbol.HasAttribute(KnownAttributes.GpuKernel);
        var hasAutoResult = isGpuKernelEntry && symbol is { ReturnType.SpecialType: not SpecialType.System_Void };

        if (isKernel)
        {
            // A kernel entry always has a symbol; the null test only narrows it
            var kernelLowering = isGpuKernelEntry && symbol != null && declaration.HasBody()
                ? new DeferredLowering(declaration, semanticModel, symbol, isDeviceFunction: false)
                : null;
            var twoFormsPlan = KernelTwoFormsPlan.Empty;
            if (isGpuKernelEntry && symbol != null)
            {
                var paramBuilder = ImmutableArray.CreateBuilder<KernelParameterShape>(symbol.Parameters.Length);
                symbol.Parameters
                    .AsStackEnumerable()
                    .Select(KernelParameterShape.Classify)
                    .AddTo(paramBuilder);
                twoFormsPlan = KernelTwoFormsPlan.Create(GlobalCppNames.GetKernelName(symbol), paramBuilder.MoveToImmutable().AsEquatableArray());
            }

            return new MethodEmissionPlan(declaration, semanticModel, MethodEmissionKind.Kernel, symbol, kernelLowering,
                isGpuKernelEntry, hasAutoResult, twoFormsPlan);
        }

        if (symbol != null && declaration.HasBody() &&
            IteratorTypeResolver.IsDeviceIteratorMethod(symbol) &&
            !IteratorTypeResolver.IsRecursiveIterator(symbol, semanticModel.Compilation))
        {
            return new MethodEmissionPlan(declaration, semanticModel, MethodEmissionKind.Iterator, symbol,
                lowering: null, isGpuKernelEntry, hasAutoResult);
        }

        if (symbol == null || !declaration.HasBody() ||
            !SuspensionAnalysis.RequiresStateMachine(symbol, semanticModel.Compilation))
        {
            return new MethodEmissionPlan(declaration, semanticModel, MethodEmissionKind.DeviceFunction, symbol, null,
                isGpuKernelEntry, hasAutoResult);
        }

        return new MethodEmissionPlan(declaration, semanticModel, MethodEmissionKind.SuspendingDeviceFunction, symbol,
            new DeferredLowering(declaration, semanticModel, symbol, isDeviceFunction: true), isGpuKernelEntry,
            hasAutoResult);
    }
}

