using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Transpiler;

#pragma warning disable SA1402 // The stack forms of a compilation, their entries and their collector live together
/// <summary>
/// The stack forms of the kernels and suspending device functions of one compilation, made only when
/// <c>GSharpManagedBarrierModel=Stack</c>; empty otherwise, so that the default build never makes one.
/// </summary>
/// <param name="Kernels">The stack form of each kernel that has one, by its entry point name.</param>
/// <param name="Devices">The stack forms of the suspending device functions, in declaration order.</param>
/// <param name="Failures">
/// The functions whose stack form could not be made, each with the reason; they and the functions that call them run as
/// state machines.
/// </param>
internal readonly record struct ManagedStackForms(
    EquatableArray<ManagedStackKernelForm> Kernels,
    EquatableArray<ManagedStackDeviceForm> Devices,
    EquatableArray<string> Failures);

/// <summary>The stack form of one kernel.</summary>
/// <param name="KernelName">The entry point name of the kernel (<see cref="GlobalCppNames.GetKernelName"/>).</param>
/// <param name="Source">The stack form.</param>
internal readonly record struct ManagedStackKernelForm(string KernelName, string Source);

/// <summary>The stack form of one suspending device function.</summary>
/// <param name="Class">The class that the form goes into.</param>
/// <param name="Source">The stack form.</param>
internal readonly record struct ManagedStackDeviceForm(HostClassHeader Class, string Source);

/// <summary>Makes the <see cref="ManagedStackForms"/> of a compilation and attaches them to the kernels.</summary>
internal static class ManagedStackFormCollector
{
    /// <summary>
    /// The stack forms of <paramref name="compilation"/> under <paramref name="model"/>: none unless it is
    /// <see cref="ManagedBarrierModel.Stack"/>.
    /// </summary>
    public static ManagedStackForms Collect(Compilation compilation, ManagedBarrierModel model,
        CancellationToken cancellationToken) =>
        model == ManagedBarrierModel.Stack ? Collect(compilation, cancellationToken) : default;

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

    private static ManagedStackForms Collect(Compilation compilation, CancellationToken cancellationToken)
    {
        var kernels = new List<IMethodSymbol>();
        var devices = new List<IMethodSymbol>();
        var failures = ImmutableArray.CreateBuilder<string>();
        CollectFunctions(compilation, kernels, devices, failures, cancellationToken);

        var deviceSources = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGenerate(device, compilation, isKernel: false, failures) is { } source)
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
                if (deviceSources.ContainsKey(device) && CallsFunctionWithoutForm(device, compilation, deviceSources))
                {
                    deviceSources.Remove(device);
                    failures.Add(GetDisplayName(device) + ": calls a function without a stack form");
                    changed = true;
                }
            }
        }

        var deviceForms = ImmutableArray.CreateBuilder<ManagedStackDeviceForm>(deviceSources.Count);
        foreach (var device in devices)
        {
            if (deviceSources.TryGetValue(device, out var source))
            {
                deviceForms.Add(new ManagedStackDeviceForm(
                    HostClassHeader.Create(device.ContainingType, compilation), source));
            }
        }

        var kernelForms = ImmutableArray.CreateBuilder<ManagedStackKernelForm>();
        foreach (var kernel in kernels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CallsFunctionWithoutForm(kernel, compilation, deviceSources))
            {
                failures.Add(GetDisplayName(kernel) + ": calls a function without a stack form");
                continue;
            }

            if (TryGenerate(kernel, compilation, isKernel: true, failures) is { } source)
            {
                kernelForms.Add(new ManagedStackKernelForm(GlobalCppNames.GetKernelName(kernel), source));
            }
        }

        return new ManagedStackForms(
            kernelForms.ToImmutable().AsEquatableArray(),
            deviceForms.MoveToImmutable().AsEquatableArray(),
            failures.ToImmutable().AsEquatableArray());
    }

    // The kernels and device functions that can have a stack form, each once (a partial method by its defining part),
    // in the order of their first declaration; a kernel whose warp collectives cannot run on its stack goes to
    // <failures> with the reason (ManagedStackEligibility.GetWarpFailure)
    private static void CollectFunctions(Compilation compilation, List<IMethodSymbol> kernels,
        List<IMethodSymbol> devices, ImmutableArray<string>.Builder failures, CancellationToken cancellationToken)
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
                if (isKernel && KernelDeclaration.IsLaunchable(method, compilation, cancellationToken))
                {
                    if (ManagedStackEligibility.IsStackKernel(method, compilation))
                    {
                        kernels.Add(method);
                    }
                    else if (ManagedStackEligibility.GetWarpFailure(method, compilation) is { } reason)
                    {
                        failures.Add(GetDisplayName(method) + ": " + reason);
                    }
                }
                else if (!isKernel &&
                         method.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
                         !method.IsPartialWithoutImplementation() &&
                         ManagedStackEligibility.IsStackFunction(method, compilation))
                {
                    devices.Add(method);
                }
            }
        }
    }

    // [WHY] A stack form is an addition of the Stack model: when it cannot be made, the function keeps its state
    // machine and the reason is stated in the build output (ManagedStackPipeline.EmitFailures) instead of failing it
    private static string? TryGenerate(IMethodSymbol method, Compilation compilation, bool isKernel,
        ImmutableArray<string>.Builder failures)
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
            failures.Add(GetDisplayName(method) + ": " + exception.GetType().Name + ": " + exception.Message);
            return null;
        }
    }

    private static bool CallsFunctionWithoutForm(IMethodSymbol method, Compilation compilation,
        Dictionary<IMethodSymbol, string> deviceSources)
    {
        if (!method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            declaration.GetBodyNode() is not { } body)
        {
            return false;
        }

        return SuspensionAnalysis.GetSuspendingCallees(body, compilation)
            .AsStackEnumerable()
            .Any(deviceSources, static (callee, sources) => !sources.ContainsKey(GetDefinition(callee)));
    }

    // <type>.<method>, for the build output
    private static string GetDisplayName(IMethodSymbol method) => method.ContainingType.Name + "." + method.Name;

    // The symbol a call names, as CollectFunctions keys it
    private static IMethodSymbol GetDefinition(IMethodSymbol method)
    {
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        return definition.PartialDefinitionPart ?? definition;
    }
}
#pragma warning restore SA1402
