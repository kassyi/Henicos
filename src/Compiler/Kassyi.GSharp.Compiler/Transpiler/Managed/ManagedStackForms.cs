using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Transpiler;

#pragma warning disable SA1402 // The stack forms of a compilation, their entries and their collector live together
/// <summary>
/// The stack forms of the kernels and suspending device functions of one compilation: kernels are made only when
/// <c>GSharpManagedBarrierModel=Stack</c>, and suspending device functions are made whenever
/// <c>GSharpManagedBarrierModel=Stack</c> or <c>GSharpExportDeviceCode</c> is enabled.
/// </summary>
/// <param name="Kernels">The stack form of each kernel that has one, by its entry point name.</param>
/// <param name="Devices">The stack forms of the suspending device functions, in declaration order.</param>
/// <param name="Fallbacks">
/// The kernels and suspending device functions that stay on the managed state machine, each with its reason.
/// </param>
internal readonly record struct ManagedStackForms(
    EquatableArray<ManagedStackKernelForm> Kernels,
    EquatableArray<ManagedStackDeviceForm> Devices,
    EquatableArray<StateMachineFallback> Fallbacks);

/// <summary>The stack form of one kernel.</summary>
/// <param name="KernelName">The entry point name of the kernel (<see cref="GlobalCppNames.GetKernelName"/>).</param>
/// <param name="Source">The stack form.</param>
internal readonly record struct ManagedStackKernelForm(string KernelName, string Source);

/// <summary>The stack form of one suspending device function.</summary>
/// <param name="Id">The documentation comment ID of the function's original definition (<see cref="DeviceExportRecord.Id"/>).</param>
/// <param name="Class">The class that the form goes into.</param>
/// <param name="Source">The stack form.</param>
internal readonly record struct ManagedStackDeviceForm(string Id, HostClassHeader Class, string Source);

/// <summary>Makes the <see cref="ManagedStackForms"/> of a compilation and attaches them to the kernels.</summary>
internal static class ManagedStackFormCollector
{
    /// <summary>
    /// The stack forms of <paramref name="compilation"/> under <paramref name="model"/>: kernels and device functions
    /// when <paramref name="model"/> is <see cref="ManagedBarrierModel.Stack"/>, device functions only when
    /// <paramref name="exportDeviceCode"/> is <see langword="true"/>, and none otherwise.
    /// </summary>
    public static ManagedStackForms Collect(Compilation compilation, ManagedBarrierModel model,
        bool exportDeviceCode, CancellationToken cancellationToken) =>
        model == ManagedBarrierModel.Stack || exportDeviceCode
            ? Collect(compilation, includeKernels: model == ManagedBarrierModel.Stack, cancellationToken)
            : default;

    /// <summary>
    /// The kernels with their stack forms attached: a kernel whose managed path runs a copy of its body and that has a
    /// stack form supports the stack model (<see cref="KernelHostModel.SupportsManagedStack"/>).
    /// </summary>
    public static ImmutableArray<TranspiledKernelResult> Attach(ImmutableArray<TranspiledKernelResult> kernels,
        ManagedStackForms forms)
    {
        if (forms.Kernels.IsDefaultOrEmpty || kernels.IsDefaultOrEmpty)
        {
            return kernels;
        }

        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var form in forms.Kernels)
        {
            byName[form.KernelName] = form.Source;
        }

        var attached = ImmutableArray.CreateBuilder<TranspiledKernelResult>(kernels.Length);
        foreach (var kernel in kernels)
        {
            var host = kernel.Host with { SupportsManagedStack = true };
            // A launcher that cannot pass the arguments to the stack form keeps the state machine
            attached.Add(kernel.Host.ManagedMode == ManagedKernelMode.Copy &&
                         byName.TryGetValue(kernel.Host.KernelName, out var source) &&
                         CSharpExecutionPlan.CreateForStack(host).CannotRunReason is null
                ? kernel with { ManagedStackSource = source, Host = host }
                : kernel);
        }

        return attached.MoveToImmutable();
    }

    private static ManagedStackForms Collect(Compilation compilation, bool includeKernels,
        CancellationToken cancellationToken)
    {
        var kernels = new List<IMethodSymbol>();
        var devices = new List<IMethodSymbol>();
        var fallbacks = includeKernels ? ImmutableArray.CreateBuilder<StateMachineFallback>() : null;
        CollectFunctions(compilation, kernels, devices, fallbacks, cancellationToken);

        var deviceSources = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGenerate(device, compilation, isKernel: false, fallbacks) is { } source)
            {
                deviceSources.Add(device, source);
            }
        }

        // A form that calls a function without one would not compile: drop the callers until none is left
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var device in devices)
            {
                if (deviceSources.ContainsKey(device) &&
                    CallsFunctionWithoutForm(device, compilation, deviceSources, out var fallbackReason))
                {
                    deviceSources.Remove(device);
                    fallbacks?.Add(new StateMachineFallback(KernelHostNames.GetMessageName(device), fallbackReason));
                    changed = true;
                }
            }
        }

        var deviceForms = ImmutableArray.CreateBuilder<ManagedStackDeviceForm>(deviceSources.Count);
        foreach (var device in devices)
        {
            if (deviceSources.TryGetValue(device, out var source))
            {
                var id = device.OriginalDefinition.GetDocumentationCommentId() ?? GlobalCppNames.GetFunctionName(device);
                deviceForms.Add(new ManagedStackDeviceForm(
                    id, HostClassHeader.Create(device.ContainingType, compilation), source));
            }
        }

        var kernelForms = ImmutableArray.CreateBuilder<ManagedStackKernelForm>();
        if (includeKernels)
        {
            foreach (var kernel in kernels)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (CallsFunctionWithoutForm(kernel, compilation, deviceSources, out var fallbackReason))
                {
                    fallbacks?.Add(new StateMachineFallback(KernelHostNames.GetMessageName(kernel), fallbackReason));
                    continue;
                }

                if (TryGenerate(kernel, compilation, isKernel: true, fallbacks) is { } source)
                {
                    kernelForms.Add(new ManagedStackKernelForm(GlobalCppNames.GetKernelName(kernel), source));
                }
            }
        }

        return new ManagedStackForms(
            kernelForms.ToImmutable().AsEquatableArray(),
            deviceForms.MoveToImmutable().AsEquatableArray(),
            fallbacks is not null ? fallbacks.ToImmutable().AsEquatableArray() : default);
    }

    // The kernels and device functions that can have a stack form, each once (a partial method by its defining part),
    // in the order of their first declaration; when collecting fallbacks, those that stay on the managed state machine
    // go to <fallbacks> with the reason (ManagedStackEligibility)
    private static void CollectFunctions(Compilation compilation, List<IMethodSymbol> kernels,
        List<IMethodSymbol> devices, ImmutableArray<StateMachineFallback>.Builder? fallbacks,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetCachedSemanticModel(tree);
            var methods = tree.GetRoot(cancellationToken)
                .DescendantNodes(static node =>
                    node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax);
            foreach (var node in methods)
            {
                if (node is not MethodDeclarationSyntax declaration ||
                    model.GetDeclaredSymbol(declaration, cancellationToken) is not { } declared)
                {
                    continue;
                }

                var method = declared.PartialDefinitionPart ?? declared;
                if (!seen.Add(method))
                {
                    continue;
                }

                var isKernel = method.HasAttribute(KnownAttributes.GpuKernel);
                if (fallbacks is not null && isKernel &&
                    KernelDeclaration.IsLaunchable(method, compilation, cancellationToken))
                {
                    // Kernels whose ManagedMode is Unavailable (GSH0119) or UserMethod do not run as a managed state machine
                    if (IsManagedCopyExcluded(method, compilation))
                    {
                        continue;
                    }

                    if (ManagedStackEligibility.FindKernelReason(method, compilation) is not { } reason)
                    {
                        kernels.Add(method);
                    }
                    else if (reason.Name != nameof(NativeStackObstacle.NoBlockBarrier))
                    {
                        fallbacks.Add(new StateMachineFallback(KernelHostNames.GetMessageName(method), reason));
                    }
                }
                else if (!isKernel &&
                         method.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
                         !method.IsPartialWithoutImplementation())
                {
                    if (ManagedStackEligibility.FindFunctionReason(method, compilation) is not { } reason)
                    {
                        devices.Add(method);
                    }
                    else if (fallbacks is not null &&
                             SuspensionAnalysis.IsSuspendingFunction(method, compilation) &&
                             reason.Name != nameof(NativeStackObstacle.NoBlockBarrier))
                    {
                        fallbacks.Add(new StateMachineFallback(KernelHostNames.GetMessageName(method), reason));
                    }
                }
            }
        }
    }

    // [WHY] A stack form is an addition of the Stack model: when it cannot be made, the function keeps its state
    // machine and the reason is stated in the build output (StateMachineFallbackPipeline) instead of failing it
    private static string? TryGenerate(IMethodSymbol method, Compilation compilation, bool isKernel,
        ImmutableArray<StateMachineFallback>.Builder? fallbacks)
    {
        try
        {
            if (!method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration))
            {
                return null;
            }

            var semanticModel = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
            var plan = MethodEmissionPlan.Create(declaration, semanticModel, isKernel);
            if (!isKernel && plan.Kind != MethodEmissionKind.SuspendingDeviceFunction)
            {
                return null;
            }

            return MethodTranspiler.GenerateManagedStackSource(in plan);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            fallbacks?.Add(new StateMachineFallback(
                KernelHostNames.GetMessageName(method),
                new StateMachineReason(
                    CpuPath.Managed,
                    "StackFormGenerationFailed",
                    "its stack form could not be generated",
                    exception.GetType().Name + ": " + exception.Message)));
            return null;
        }
    }

    private static StateMachineReason CreateCallsWithoutFormReason(string detail) =>
        new(
            CpuPath.Managed,
            "CallsFunctionWithoutForm",
            "it calls a suspending device function without a stack form",
            detail);

    private static bool IsManagedCopyExcluded(IMethodSymbol kernel, Compilation compilation) =>
        FileLocalNames.FindOwnName(kernel, compilation) is not null;

    private static bool CallsFunctionWithoutForm(IMethodSymbol method, Compilation compilation,
        Dictionary<IMethodSymbol, string> deviceSources, out StateMachineReason fallbackReason)
    {
        if (!method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            declaration.GetBodyNode() is not { } body)
        {
            fallbackReason = default;
            return false;
        }

        var semanticModel = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        foreach (var (callee, isMethodGroup) in
                 SuspensionAnalysis.GetReferencedSuspendingFunctions(body, compilation, semanticModel))
        {
            if (!SuspensionAnalysis.TryGetDeviceFunctionBody(callee, compilation, out _))
            {
                if (!ManagedStackEligibility.IsStackFunction(callee, compilation))
                {
                    fallbackReason = ManagedStackEligibility.CreateImportedMissingFormReason(
                        callee, compilation, isMethodGroup);
                    return true;
                }

                continue;
            }

            var definition = GetDefinition(callee);
            if (!deviceSources.ContainsKey(definition))
            {
                var detail = isMethodGroup
                    ? "passes " + KernelHostNames.GetMessageName(definition) +
                      ", which has no stack form, as a method group"
                    : "calls a function without a stack form";
                fallbackReason = CreateCallsWithoutFormReason(detail);
                return true;
            }
        }

        fallbackReason = default;
        return false;
    }
    // The symbol a call names, as CollectFunctions keys it
    private static IMethodSymbol GetDefinition(IMethodSymbol method)
    {
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        return definition.PartialDefinitionPart ?? definition;
    }
}
#pragma warning restore SA1402
