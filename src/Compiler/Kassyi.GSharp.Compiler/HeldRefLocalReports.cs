using Kassyi.GSharp.Compiler.Aot;
using Kassyi.GSharp.Compiler.Instantiation;
using Kassyi.GSharp.Compiler.Models;
using Kassyi.GSharp.Compiler.Targets;
using Kassyi.GSharp.Compiler.Transpiler.Managed;

namespace Kassyi.GSharp.Compiler;

// The generator's own reports of the ref locals and spans that the CPU state machines would keep in their frames
// (HeldRefLocalSite), and, when both CPU paths run on stacks, of the cross-barrier locals of the kernels that fall back
// to a state machine (the safety net of CrossBarrierLocals)
internal static class HeldRefLocalReports
{
    internal readonly record struct KernelStackStatus(
        string KernelName,
        string MessageName,
        bool RunsOnNativeStack,
        bool RunsOnManagedStack,
        StateMachineFallback NativeFallback);

    internal static EquatableArray<KernelStackStatus> CreateKernelStackStatuses(
        ImmutableArray<TranspiledKernelResult> kernels)
    {
        var builder = ImmutableArray.CreateBuilder<KernelStackStatus>(kernels.Length);
        foreach (var kernel in kernels)
        {
            builder.Add(new KernelStackStatus(
                kernel.Host.KernelName,
                kernel.Host.MessageName,
                kernel.RunsOnNativeStack,
                kernel.ManagedStackSource.Length > 0,
                kernel.NativeFallback));
        }

        return builder.MoveToImmutable().AsEquatableArray();
    }

    private readonly record struct FallbackContext(
        string KernelMessageName,
        string PathLabel,
        string WhySentence);

    private readonly record struct FallbackMethodTarget(
        MethodDeclarationSyntax Declaration,
        SemanticModel Model,
        FallbackContext Context);

    // GSH0003 at the ref locals that the CPU state machines would keep in their frames (HeldRefLocalSite), GSH0039 and
    // GSH0121 (which only the generator reports; a goto across a barrier is GSH0040, the analyzer's); a device
    // function's sites once, however many results it has. A CUDA build emits managed state machines only for the
    // device functions it exports, so it reports only their GSH0121 (ChainedLabelJumpChecks.FindReported). Under
    // BothOnStacks, state-machine-only held ref / span sites of stack-eligible methods are skipped unless the method
    // belongs to a fallback kernel closure, and any kernel falling back to the state machine triggers the safety net.
    internal static void ReportHeldRefLocals(SourceProductionContext context,
        ImmutableArray<EquatableArray<HeldRefLocalSite>> kernels,
        ImmutableArray<EquatableArray<HeldRefLocalSite>> devices,
        GeneratorConfig config,
        Compilation compilation,
        EquatableArray<KernelStackStatus> kernelStatuses,
        EquatableArray<StateMachineFallback> managedFallbacks)
    {
        if (config.IsCuda && !config.ExportDeviceCode)
        {
            return;
        }

        var fallbackClosures = config.Models is { BothOnStacks: true, BuildsCpuPaths: true } &&
                               !kernelStatuses.IsDefaultOrEmpty
            ? CollectFallbackClosures(config.Models, compilation, kernelStatuses, managedFallbacks)
            : default;
        HashSet<(string Id, Location Location)>? reportedSafetyNet = !fallbackClosures.Targets.IsDefaultOrEmpty ? [] : null;

        var reported = new HashSet<HeldRefLocalSite>();
        foreach (var sites in config.IsCuda ? devices : kernels.AddRange(devices))
        {
            foreach (var site in sites)
            {
                if ((!config.IsCuda || site.IsChainedLabelGoto) && reported.Add(site))
                {
                    if (config.Models.BothOnStacks &&
                        !config.IsCuda &&
                        !site.IsChainedLabelGoto &&
                        site.TryFindEnclosingMethod(compilation, out var method, out var decl, out var model))
                    {
                        if (StackOnlyExecution.FindStateMachineReason(method, decl, model, config.Models) is { } reason)
                        {
                            ReportHeldRefSite(context, compilation, site,
                                StackOnlyExecution.FormatRefStructReason(method, reason, config.Models),
                                reportedSafetyNet);
                            continue;
                        }

                        if (fallbackClosures.ByMethod is not null &&
                            fallbackClosures.ByMethod.TryGetValue(
                                GenericExpansion.GetCanonicalDefinition(method), out var fallback))
                        {
                            ReportHeldRefSite(context, compilation, site,
                                StackOnlyExecution.FormatSafetyNetRefStructReason(
                                    fallback.KernelMessageName, fallback.PathLabel, fallback.WhySentence),
                                reportedSafetyNet);
                        }

                        continue;
                    }

                    ReportHeldRefSite(context, compilation, site,
                        StackOnlyExecution.DefaultRefStructReason, reportedSafetyNet);
                }
            }
        }

        if (!fallbackClosures.Targets.IsDefaultOrEmpty && reportedSafetyNet is not null)
        {
            ReportFallbackCrossBarrierLocals(context, fallbackClosures.Targets, reportedSafetyNet);
        }
    }

    private static void ReportHeldRefSite(
        SourceProductionContext context,
        Compilation compilation,
        HeldRefLocalSite site,
        string refStructReasonTail,
        HashSet<(string Id, Location Location)>? reportedSafetyNet)
    {
        var diagnostic = site.CreateDiagnostic(compilation, refStructReasonTail);
        reportedSafetyNet?.Add((diagnostic.Id, diagnostic.Location));
        context.ReportDiagnostic(diagnostic);
    }

    private static (Dictionary<IMethodSymbol, FallbackContext>? ByMethod, ImmutableArray<FallbackMethodTarget> Targets)
        CollectFallbackClosures(
            CpuBarrierModels models,
            Compilation compilation,
            EquatableArray<KernelStackStatus> kernelStatuses,
            EquatableArray<StateMachineFallback> managedFallbacks)
    {
        var hasFallback = kernelStatuses
            .AsStackEnumerable()
            .Any(static status => !status.RunsOnNativeStack || !status.RunsOnManagedStack);
        if (!hasFallback)
        {
            return (null, default);
        }

        var managedReasons = new Dictionary<string, StateMachineReason>(StringComparer.Ordinal);
        if (!managedFallbacks.IsDefaultOrEmpty)
        {
            foreach (var fallback in managedFallbacks)
            {
                if (!managedReasons.ContainsKey(fallback.MethodName))
                {
                    managedReasons.Add(fallback.MethodName, fallback.Reason);
                }
            }
        }

        var kernelsByName = CollectLaunchableKernelBodies(compilation);
        var byMethod = new Dictionary<IMethodSymbol, FallbackContext>(SymbolEqualityComparer.Default);
        var targets = ImmutableArray.CreateBuilder<FallbackMethodTarget>();

        foreach (var status in kernelStatuses)
        {
            if (status.RunsOnNativeStack && status.RunsOnManagedStack)
            {
                continue;
            }

            if (!kernelsByName.TryGetValue(status.KernelName, out var kernelEntry))
            {
                continue;
            }

            string pathLabel;
            string whySentence;
            if (!status.RunsOnNativeStack)
            {
                pathLabel = "native";
                whySentence = status.NativeFallback.HasValue
                    ? status.NativeFallback.Reason.Sentence
                    : StateMachineReason.ForNative(NativeStackObstacle.StackBodyLowering)?.Sentence ??
                      "its body could not be lowered for the stack model";
            }
            else
            {
                pathLabel = "managed";
                whySentence = managedReasons.TryGetValue(status.MessageName, out var managedReason)
                    ? managedReason.Sentence
                    : ManagedStackEligibility.FindKernelReason(kernelEntry.Symbol, compilation)?.Sentence ??
                      "its stack form could not be generated";
            }

            CollectFallbackClosureTargets(models, compilation, kernelEntry,
                new FallbackContext(status.MessageName, pathLabel, whySentence), byMethod, targets);
        }

        return targets.Count > 0
            ? (byMethod, targets.ToImmutable())
            : (null, ImmutableArray<FallbackMethodTarget>.Empty);
    }

    private static void ReportFallbackCrossBarrierLocals(
        SourceProductionContext context,
        ImmutableArray<FallbackMethodTarget> targets,
        HashSet<(string Id, Location Location)> reported)
    {
        foreach (var target in targets)
        {
            foreach (var local in CrossBarrierLocals.Find(target.Declaration, target.Model))
            {
                if (local.Descriptor.Id == GSharpDiagnostics.IteratorBodyConstructNotSupported.Id ||
                    !reported.Add((local.Descriptor.Id, local.Location)))
                {
                    continue;
                }

                if (local.Descriptor.Id == GSharpDiagnostics.CrossBarrierRefStructProhibited.Id)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        local.Descriptor,
                        local.Location,
                        local.Name,
                        StackOnlyExecution.FormatSafetyNetRefStructReason(
                            target.Context.KernelMessageName,
                            target.Context.PathLabel,
                            target.Context.WhySentence)));
                }
                else if (local.Descriptor.Id == GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        local.Descriptor,
                        local.Location,
                        StackOnlyExecution.FormatSafetyNetLocalPointerReason(
                            target.Context.KernelMessageName,
                            target.Context.PathLabel,
                            target.Context.WhySentence)));
                }
            }
        }
    }

    private static Dictionary<string, (IMethodSymbol Symbol, MethodDeclarationSyntax Declaration, SemanticModel Model)>
        CollectLaunchableKernelBodies(Compilation compilation)
    {
        var kernelsByName =
            new Dictionary<string, (IMethodSymbol Symbol, MethodDeclarationSyntax Declaration, SemanticModel Model)>(
                StringComparer.Ordinal);
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetCachedSemanticModel(tree);
            var declarations = tree.GetRoot()
                .DescendantNodes(static node =>
                    node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax);
            foreach (var node in declarations)
            {
                if (node is not MethodDeclarationSyntax declaration ||
                    model.GetDeclaredSymbol(declaration) is not { } declared)
                {
                    continue;
                }

                var symbol = declared.PartialDefinitionPart ?? declared;
                if (!seen.Add(symbol) ||
                    !symbol.HasAttribute(KnownAttributes.GpuKernel) ||
                    !KernelDeclaration.IsLaunchable(symbol, compilation, CancellationToken.None))
                {
                    continue;
                }

                var (bodyDeclaration, bodyModel) = GpuKernelGenerator.GetBodyDeclaration(declaration, symbol, model);
                if (!bodyDeclaration.HasBody())
                {
                    continue;
                }

                kernelsByName[GlobalCppNames.GetKernelName(symbol)] = (symbol, bodyDeclaration, bodyModel);
            }
        }

        return kernelsByName;
    }

    private static void CollectFallbackClosureTargets(
        CpuBarrierModels models,
        Compilation compilation,
        (IMethodSymbol Symbol, MethodDeclarationSyntax Declaration, SemanticModel Model) kernelEntry,
        FallbackContext fallbackContext,
        Dictionary<IMethodSymbol, FallbackContext> byMethod,
        ImmutableArray<FallbackMethodTarget>.Builder targets)
    {
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var visitedDefinitions = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var queue = new Queue<(IMethodSymbol Symbol, MethodDeclarationSyntax Declaration, SemanticModel Model)>();
        queue.Enqueue(kernelEntry);

        while (queue.Count > 0)
        {
            var (method, declaration, model) = queue.Dequeue();
            if (StackOnlyExecution.FindStateMachineReason(method, declaration, model, models) is null)
            {
                var definition = GenericExpansion.GetCanonicalDefinition(method);
                if (!byMethod.ContainsKey(definition))
                {
                    byMethod.Add(definition, fallbackContext);
                    targets.Add(new FallbackMethodTarget(declaration, model, fallbackContext));
                }
            }

            if (declaration.GetBodyNode() is not { } body)
            {
                continue;
            }

            foreach (var site in DirectCallees.Of([body], TypeArgumentMap.Of(method), compilation))
            {
                if (CalleeSite.IsOperatorMacro(site, compilation) ||
                    site.Callee.MethodKind is not (MethodKind.Ordinary or MethodKind.ReducedExtension) ||
                    !SuspensionAnalysis.HasDeviceFunctionAttribute(site.Callee) ||
                    !SuspensionAnalysis.IsSuspendingFunction(site.Callee, compilation))
                {
                    continue;
                }

                var canonical = SuspensionAnalysis.ToCanonicalDefinitionPart(
                    site.Callee.GetConstructedReducedFrom() ?? site.Callee);
                var definition = GenericExpansion.GetCanonicalDefinition(canonical);
                if (!visited.Add(canonical))
                {
                    continue;
                }

                // Prevent unbounded expansion on polymorphic recursion while still following distinct normalizations
                if (!visitedDefinitions.Add(definition) &&
                    GenericExpansion.GetExpandingCallees(GenericExpansion.GetCanonicalDefinition(method), compilation)
                        .AsStackEnumerable()
                        .Any(definition, static (expanding, own) => expanding.IsEqualTo(own)))
                {
                    continue;
                }

                if (SuspensionAnalysis.TryGetDeviceFunctionBody(definition, compilation, out var calleeDeclaration) &&
                    calleeDeclaration.HasBody())
                {
                    var calleeModel = compilation.GetCachedSemanticModel(calleeDeclaration.SyntaxTree);
                    queue.Enqueue((canonical, calleeDeclaration, calleeModel));
                }
            }
        }
    }
}
