using Kassyi.GSharp.Compiler.Aot;
using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler;

/// <summary>Roslyn incremental source generator that transpiles C# GPU kernels to CUDA C++ and emits host invocations.</summary>
[Generator]
public class GpuKernelGenerator : IIncrementalGenerator
{
    /// <summary>The tracking name of the step that transpiles each <c>[GpuKernel]</c>, for incremental-caching tests.</summary>
    public const string KernelTrackingName = "GpuKernelMethod";

    /// <summary>The tracking name of the step that transpiles each <c>[GpuDeviceFunction]</c>.</summary>
    public const string DeviceFunctionTrackingName = "GpuDeviceFunction";

    /// <summary>The tracking name of the step that builds the model of each kernel class, which its wrapper is emitted from.</summary>
    public const string KernelClassTrackingName = "GpuKernelClass";

    /// <summary>The tracking name of the input of the output that emits the shared prelude and device function sources.</summary>
    public const string SharedSourcesTrackingName = "GpuSharedSourcesInput";

    /// <summary>The tracking name of the input of the output that emits each kernel class's wrapper.</summary>
    public const string KernelWrapperTrackingName = "GpuKernelWrapperInput";

    /// <summary>The tracking name of the input of the output that emits each class's managed device state machines.</summary>
    public const string DeviceStateMachinesTrackingName = "GpuDeviceStateMachinesInput";

    /// <summary>The tracking name of the input of the output that writes the AOT artifact.</summary>
    public const string AotArtifactTrackingName = "GpuAotArtifactInput";

    /// <summary>The tracking name of the input of the output that emits the CUDA module context.</summary>
    public const string ModuleContextTrackingName = "GpuModuleContextInput";

    /// <summary>
    /// Collects what one dialect's transpilation could not emit (<see cref="TranspileFailure"/>), each reason once, and
    /// states the reasons in place of that dialect's output.
    /// </summary>
    private sealed class FailureCollector
    {
        private readonly ImmutableArray<string>.Builder _failures = ImmutableArray.CreateBuilder<string>();

        // The text is what the generated source states, so a failure found by both the signature and the body is one
        public void Fail(TranspileFailure failure)
        {
            var reason = failure.Format();
            if (!_failures.Contains(reason))
            {
                _failures.Add(reason);
            }
        }

        /// <summary>
        /// <paramref name="source"/> when nothing failed; otherwise one <c>#error</c> per reason, so that a failure the
        /// analyzer misses still stops the native build. The analyzer reports the same failures as diagnostics.
        /// </summary>
        public string Apply(string source) =>
            _failures.Count == 0 ? source : NativeModuleAssembler.FormatTranspileFailures(_failures);
    }

    private static bool CheckSuppressGs0001(MethodDeclarationSyntax methodDeclaration)
    {
        var root = methodDeclaration.SyntaxTree.GetRoot();
        if (!root.ContainsDirectives)
        {
            return false;
        }

        var suppressGs0001 = false;
        var nodes = root.DescendantNodes(TextSpan.FromBounds(0, methodDeclaration.Span.End), descendIntoTrivia: true);

        foreach (var node in nodes)
        {
            if (node is not PragmaWarningDirectiveTriviaSyntax pragma)
            {
                continue;
            }

            foreach (var errorCode in pragma.ErrorCodes)
            {
                if (errorCode is not IdentifierNameSyntax id || id.Identifier.ValueText != "GS0001")
                {
                    continue;
                }

                if (pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword))
                {
                    suppressGs0001 = true;
                }
                else if (pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.RestoreKeyword))
                {
                    suppressGs0001 = false;
                }
            }
        }

        return suppressGs0001;
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var configProvider = context.AnalyzerConfigOptionsProvider
            .Select((options, _) =>
            {
                var dialect = TranspilerDialects.GetName(options.GlobalOptions);

                options.GlobalOptions.TryGetValue("build_property.GSharpEmitSource", out var emitSrcStr);
                var emitSource = string.Equals(emitSrcStr, "true", StringComparison.OrdinalIgnoreCase);

                options.GlobalOptions.TryGetValue("build_property.IntermediateOutputPath", out var objPath);
                options.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var projDir);
                if (string.IsNullOrWhiteSpace(projDir))
                {
                    options.GlobalOptions.TryGetValue("build_property.ProjectDir", out projDir);
                }

                var intermediatePath = !string.IsNullOrWhiteSpace(objPath) && !string.IsNullOrWhiteSpace(projDir)
                    ? Path.Combine(projDir, objPath)
                    : null;

                return new GeneratorConfig(dialect, emitSource, intermediatePath);
            });

        // The wrappers take only the assembly names from the compilation, so this value compares equal across edits and
        // the steps after it stay cached; the compilation itself is never combined with an output
        var hostOptions = context.CompilationProvider
            .Select(static (compilation, _) => (compilation.Assembly.Name, compilation.AssemblyName))
            .Combine(configProvider)
            .Select(static (input, _) => HostEmitOptions.Create(input.Right, input.Left.Item1, input.Left.Item2));

        var aotTarget = context.CompilationProvider.Select(static (compilation, _) => AotTarget.Create(compilation));

        // ForAttributeWithMetadataName resolves the attribute through the symbol, so an alias of it is found as well; the
        // predicate only checks the node kind
        var kernelMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownAttributes.GpuKernelMetadataName,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => TranspileKernel(ctx))
            .WithTrackingName(KernelTrackingName)
            .Where(static m => m.HasValue)
            .Select(static (m, _) => m.GetValueOrDefault());

        var deviceMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownAttributes.GpuDeviceFunctionMetadataName,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => TranspileDeviceMethod(ctx))
            .WithTrackingName(DeviceFunctionTrackingName)
            .Where(static m => m.HasValue)
            .Select(static (m, _) => m.GetValueOrDefault());

        var allKernels = kernelMethods.Collect();
        var allDevices = deviceMethods.Collect();

        // A project without kernels gets nothing, not even the sources of its device functions
        var hasKernels = allKernels.Select(static (kernels, _) => !kernels.IsDefaultOrEmpty);

        // The class models come from the kernels of one compilation (#383); each class and the warmup list compare on
        // their own, so an edit reruns only the outputs whose model changed
        var kernelModule = allKernels.Select(static (kernels, _) => KernelModuleModel.Create(kernels));
        var kernelClasses = kernelModule
            .SelectMany(static (module, _) => module.Classes.AsImmutableArray())
            .WithTrackingName(KernelClassTrackingName);
        var warmups = kernelModule.Select(static (module, _) => module.Warmups);
        var stateMachines = allDevices
            .SelectMany(static (devices, _) => GroupDeviceStateMachines(devices));

        context.RegisterSourceOutput(
            allDevices.Combine(configProvider).Combine(hasKernels).WithTrackingName(SharedSourcesTrackingName),
            static (spc, input) => EmitSharedSources(spc, input.Left.Left, input.Left.Right, input.Right));
        context.RegisterSourceOutput(kernelClasses.Combine(hostOptions).WithTrackingName(KernelWrapperTrackingName),
            static (spc, input) => EmitKernelClass(spc, input.Left, input.Right));
        context.RegisterSourceOutput(
            stateMachines.Combine(configProvider).Combine(hasKernels).WithTrackingName(DeviceStateMachinesTrackingName),
            static (spc, input) => EmitDeviceStateMachines(spc, input.Left.Left, input.Left.Right, input.Right));
        context.RegisterSourceOutput(
            aotTarget.Combine(allKernels).Combine(allDevices).Combine(configProvider).WithTrackingName(AotArtifactTrackingName),
            static (spc, input) => WriteAot(spc, input.Left.Left.Left, input.Left.Left.Right, input.Left.Right, input.Right));
        context.RegisterSourceOutput(
            warmups.Combine(hostOptions).Combine(hasKernels).WithTrackingName(ModuleContextTrackingName),
            static (spc, input) => EmitModuleContext(spc, input.Left.Left, input.Left.Right, input.Right));
    }

    private static TranspiledKernelResult? TranspileKernel(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetNode is not MethodDeclarationSyntax methodDeclaration ||
            context.TargetSymbol is not IMethodSymbol methodSymbol)
        {
            return null;
        }

        // The values that the host wrappers need are read here, from this compilation, so that a result taken from the
        // cache never refers to an earlier one (#383)
        var header = HostClassHeader.Create(methodSymbol.ContainingType);
        var host = KernelHostModel.Create(methodSymbol, StructMarshalingEmitter.RenderStructs(methodSymbol));

        try
        {
            var cudaFailures = new FailureCollector();
            var cppFailures = new FailureCollector();
            var cudaDialect = new GSharpCudaDialect();
            var cppDialect = new StandardCppDialect();

            var plan = MethodEmissionPlan.Create(methodDeclaration, context.SemanticModel, isKernel: true);

            var cudaSource = cudaFailures.Apply(MethodTranspiler.GenerateCudaSource(in plan, cudaFailures.Fail, cudaDialect,
                out var requiredIncludesCuda));
            var cppSource = cppFailures.Apply(MethodTranspiler.GenerateCudaSource(in plan, cppFailures.Fail, cppDialect,
                out var requiredIncludesCpp));
            var managedCSharpSource = MethodTranspiler.GenerateManagedCSharpSource(in plan);

            if (!CheckSuppressGs0001(methodDeclaration))
            {
                return new TranspiledKernelResult(
                    header,
                    host,
                    cudaSource,
                    cppSource,
                    managedCSharpSource,
                    ToIncludes(requiredIncludesCuda),
                    ToIncludes(requiredIncludesCpp),
                    ""
                );
            }

            cudaSource = "// gsharp:suppress GS0001\n" + cudaSource;
            cppSource = "// gsharp:suppress GS0001\n" + cppSource;

            return new TranspiledKernelResult(
                header,
                host,
                cudaSource,
                cppSource,
                managedCSharpSource,
                ToIncludes(requiredIncludesCuda),
                ToIncludes(requiredIncludesCpp),
                ""
            );
        }
        catch (Exception ex)
        {
            return new TranspiledKernelResult(
                header,
                host,
                "", "", $"/*\n{ex}\n*/\n#error \"Internal Compiler Error: Check comments for details\"\n",
                new EquatableArray<string>(), new EquatableArray<string>(), ex.ToString());
        }
    }

    private static TranspiledDeviceMethodResult? TranspileDeviceMethod(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetNode is not MethodDeclarationSyntax methodDeclaration ||
            context.TargetSymbol is not IMethodSymbol methodSymbol)
        {
            return null;
        }

        try
        {
            var cudaFailures = new FailureCollector();
            var cppFailures = new FailureCollector();
            var cudaDialect = new GSharpCudaDialect();
            var cppDialect = new StandardCppDialect();

            var plan = MethodEmissionPlan.Create(methodDeclaration, context.SemanticModel, isKernel: false);

            var cudaSignature = MethodTranspiler.GenerateCudaSignature(in plan, cudaFailures.Fail, cudaDialect);
            var cppSignature = MethodTranspiler.GenerateCudaSignature(in plan, cppFailures.Fail, cppDialect);

            var cudaBody = cudaFailures.Apply(MethodTranspiler.GenerateCudaSource(in plan, cudaFailures.Fail, cudaDialect,
                out var requiredIncludesCuda));
            var cppBody = cppFailures.Apply(MethodTranspiler.GenerateCudaSource(in plan, cppFailures.Fail, cppDialect,
                out var requiredIncludesCpp));

            // A device function that may suspend has a frame, which embeds the frames of the calls it makes
            var frameName = "";
            var frameDependencies = ImmutableArray.CreateBuilder<string>();
            var managedCSharpSource = "";
            if (plan.Kind == MethodEmissionKind.SuspendingDeviceFunction && methodDeclaration.GetBodyNode() is { } body)
            {
                frameName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.SuspendingCall.FrameTypePrefix, methodSymbol);
                foreach (var callee in SuspensionAnalysis.GetSuspendingCallees(body,
                             context.SemanticModel.Compilation))
                {
                    frameDependencies.Add(GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.SuspendingCall.FrameTypePrefix, callee));
                }

                managedCSharpSource = MethodTranspiler.GenerateManagedDeviceFunctionSource(in plan);
            }

            return new TranspiledDeviceMethodResult(
                HostClassHeader.Create(methodSymbol.ContainingType),
                methodSymbol.Name,
                cudaSignature,
                cudaBody,
                cppSignature,
                cppBody,
                ToIncludes(requiredIncludesCuda),
                ToIncludes(requiredIncludesCpp),
                "",
                frameName,
                new EquatableArray<string>(frameDependencies.ToImmutable()),
                managedCSharpSource);
        }
        catch (Exception ex)
        {
            var errorSource = $"/*\n{ex}\n*/\n#error \"Internal Compiler Error: Check comments for details\"\n";

            return new TranspiledDeviceMethodResult(
                HostClassHeader.Create(methodSymbol.ContainingType),
                methodSymbol.Name,
                "", errorSource, "", errorSource, new EquatableArray<string>(), new EquatableArray<string>(), ex.ToString());
        }
    }

    // The prelude and the device functions shared by every kernel, as C# constants for the JIT path
    private static void EmitSharedSources(SourceProductionContext context,
        ImmutableArray<TranspiledDeviceMethodResult> deviceMethods, GeneratorConfig config, bool hasKernels)
    {
        if (!hasKernels)
        {
            return;
        }

        var distinctDeviceMethods = GetDistinctDeviceMethods(deviceMethods);
        foreach (var method in distinctDeviceMethods)
        {
            ReportInternalError(context, method.InternalError);
        }

        if (!config.EmitSource)
        {
            return;
        }

        var commonDeviceSource = NativeModuleAssembler.AssembleDeviceSource(distinctDeviceMethods, config.IsCuda);
        var preludeSource = NativeModuleAssembler.GetPreludeSource(config.CreateDialect());

        using var preludeWriter = new SourceWriter();
        preludeWriter.AppendGeneratedFileHeader(enableNullable: false);
        preludeWriter.AppendLine("namespace Kassyi.GSharp.Generated");
        using (preludeWriter.Scope())
        {
            preludeWriter.AppendGeneratedCodeAttributes();
            preludeWriter.AppendLine($"internal static class {config.SharedPreludeClassName}");
            using (preludeWriter.Scope())
            {
                preludeWriter.Append("public const string Source = @\"");
                preludeWriter.AppendVerbatimLiteral(preludeSource);
                preludeWriter.AppendLine("\";");
            }
        }

        context.AddSource($"{config.SharedPreludeClassName}.g.cs",
            SourceText.From(preludeWriter.ToString(), Encoding.UTF8));

        using var deviceWriter = new SourceWriter();
        deviceWriter.AppendGeneratedFileHeader(enableNullable: false);
        deviceWriter.AppendLine("namespace Kassyi.GSharp.Generated");
        using (deviceWriter.Scope())
        {
            deviceWriter.AppendGeneratedCodeAttributes();
            deviceWriter.AppendLine($"internal static class {config.SharedDeviceCodeClassName}");
            using (deviceWriter.Scope())
            {
                deviceWriter.Append("public const string Source = @\"");
                deviceWriter.AppendVerbatimLiteral(commonDeviceSource);
                deviceWriter.AppendLine("\";");
            }
        }

        context.AddSource($"{config.SharedDeviceCodeClassName}.g.cs",
            SourceText.From(deviceWriter.ToString(), Encoding.UTF8));
    }

    // The wrapper of one kernel class. KernelClassAnalyzer reports the overloads and structs that it leaves out (GSH0059,
    // GSH0058)
    private static void EmitKernelClass(SourceProductionContext context, KernelClassModel model, HostEmitOptions options)
    {
        foreach (var kernel in model.Kernels)
        {
            ReportInternalError(context, kernel.InternalError);
        }

        var wrapperSource = WrapperEmitter.GenerateKernelExtensions(model, options.Config, options.AssemblyName);
        context.AddSource(model.Class.HintName, SourceText.From(wrapperSource, Encoding.UTF8));
    }

    // The managed fallback runs the state machines of the device functions that may suspend; they are emitted into the
    // partial class that declares each function.
    private static void EmitDeviceStateMachines(SourceProductionContext context, DeviceStateMachineGroup group,
        GeneratorConfig config, bool hasKernels)
    {
        if (!hasKernels || config.IsCuda)
        {
            return;
        }

        context.AddSource(group.Class.HintName[..^".g.cs".Length] + ".DeviceStateMachines.g.cs",
            SourceText.From(WrapperEmitter.GenerateDeviceStateMachines(group.Class, group.Sources), Encoding.UTF8));
    }

    // Keyed by the hint name, which identifies the class (#472)
    private static ImmutableArray<DeviceStateMachineGroup> GroupDeviceStateMachines(
        ImmutableArray<TranspiledDeviceMethodResult> deviceMethods)
    {
        var sourcesByClass = new Dictionary<string, ImmutableArray<string>.Builder>(StringComparer.Ordinal);
        var classOrder = ImmutableArray.CreateBuilder<HostClassHeader>();
        foreach (var method in GetDistinctDeviceMethods(deviceMethods))
        {
            if (method.ManagedCSharpSource.Length == 0)
            {
                continue;
            }

            if (!sourcesByClass.TryGetValue(method.Class.HintName, out var sources))
            {
                sources = ImmutableArray.CreateBuilder<string>();
                sourcesByClass[method.Class.HintName] = sources;
                classOrder.Add(method.Class);
            }

            sources.Add(method.ManagedCSharpSource);
        }

        var groups = ImmutableArray.CreateBuilder<DeviceStateMachineGroup>(classOrder.Count);
        classOrder
            .AsStackEnumerable()
            .Select(sourcesByClass, static (header, sources) =>
                new DeviceStateMachineGroup(header, sources[header.HintName].ToImmutable().AsEquatableArray()))
            .AddTo(groups);
        return groups.MoveToImmutable();
    }

    // The native module of the assembly (.g.cu / .g.cpp), which the build compiles ahead of time
    private static void WriteAot(SourceProductionContext context, AotTarget target,
        ImmutableArray<TranspiledKernelResult> kernelMethods, ImmutableArray<TranspiledDeviceMethodResult> deviceMethods,
        GeneratorConfig config)
    {
        if (kernelMethods.IsDefaultOrEmpty)
        {
            return;
        }

        var isCuda = config.IsCuda;
        var distinctDeviceMethods = GetDistinctDeviceMethods(deviceMethods);
        var commonDeviceSource = NativeModuleAssembler.AssembleDeviceSource(distinctDeviceMethods, isCuda);
        var preludeSource = NativeModuleAssembler.GetPreludeSource(config.CreateDialect());

        var allKernelsSource = new StringBuilder();
        foreach (var method in kernelMethods)
        {
            allKernelsSource.AppendLine(method.GetSource(isCuda));
        }

        var requiredIncludes = NativeModuleAssembler.CollectIncludes(kernelMethods, distinctDeviceMethods, isCuda);
        var bodySource = NativeModuleAssembler.AssembleBody(requiredIncludes, preludeSource, commonDeviceSource,
            allKernelsSource.ToString());
        AotArtifactWriter.Write(target, config, bodySource, context);
    }

    // The CUDA module context, which loads the fatbin and warms up every kernel's function pointer
    private static void EmitModuleContext(SourceProductionContext context, EquatableArray<KernelWarmup> warmups,
        HostEmitOptions options, bool hasKernels)
    {
        if (!hasKernels || !options.Config.IsCuda)
        {
            return;
        }

        var contextSource = CudaHostLaunchEmitter.GenerateModuleContextClass(options.ModuleAssemblyName, warmups);
        context.AddSource($"{AotHostNames.GetModuleContextName(options.ModuleAssemblyName)}.g.cs",
            SourceText.From(contextSource, Encoding.UTF8));
    }

    private static ImmutableArray<TranspiledDeviceMethodResult>.Builder GetDistinctDeviceMethods(
        ImmutableArray<TranspiledDeviceMethodResult> deviceMethods)
    {
        var distinct = ImmutableArray.CreateBuilder<TranspiledDeviceMethodResult>();
        deviceMethods
            .AsStackEnumerable()
            .AddIf(distinct, new HashSet<TranspiledDeviceMethodResult>(), static (m, seen) => seen.Add(m));
        return distinct;
    }

    // The generator reports only its own failure (GSH0048); the analyzers report the user's errors, and a construct the
    // transpiler could not emit is stated in the output as #error
    private static void ReportInternalError(SourceProductionContext context, string internalError)
    {
        if (internalError.Length > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.InternalCompilerError, Location.None, internalError));
        }
    }

    private static EquatableArray<string> ToIncludes(HashSet<string> includes)
    {
        var builder = ImmutableArray.CreateBuilder<string>(includes.Count);
        includes
            .AsStackEnumerable()
            .AddTo(builder);
        return builder.MoveToImmutable().AsEquatableArray();
    }
}
