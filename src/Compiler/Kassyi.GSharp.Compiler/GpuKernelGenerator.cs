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

    /// <summary>The tracking name of the step that renders each struct that the assembly exports on its own.</summary>
    public const string StructExportTrackingName = "GpuStructExport";

    /// <summary>The tracking name of the input of the output that writes the device code export records.</summary>
    public const string DeviceExportsTrackingName = "GpuDeviceExportsInput";

    /// <summary>
    /// The tracking name of the step that reads the device code export records of the referenced assemblies, which the
    /// native module imports from.
    /// </summary>
    public const string DeviceImportTableTrackingName = "GpuDeviceImports";

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

        /// <summary>Whether anything failed.</summary>
        public bool HasFailures => _failures.Count > 0;
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

                // Exported unless the project turns it off
                options.GlobalOptions.TryGetValue("build_property.GSharpExportDeviceCode", out var exportStr);
                var exportDeviceCode = !string.Equals(exportStr, "false", StringComparison.OrdinalIgnoreCase);

                return new GeneratorConfig(dialect, emitSource, intermediatePath, exportDeviceCode);
            });

        // The wrappers take only the assembly names from the compilation, so this value compares equal across edits and
        // the steps after it stay cached; the compilation itself is never combined with an output
        var hostOptions = context.CompilationProvider
            .Select(static (compilation, _) => (compilation.Assembly.Name, compilation.AssemblyName))
            .Combine(configProvider)
            .Select(static (input, _) => HostEmitOptions.Create(input.Right, input.Left.Item1, input.Left.Item2));

        var aotTarget = context.CompilationProvider.Select(static (compilation, _) => AotTarget.Create(compilation));

        // The records of the referenced assemblies, read from metadata; equal across edits that keep the references.
        // [WHY] GSharpDeviceImportExclude exists for the generator snapshot tests only, not for projects: a snapshot
        // compiles one kernel file of Kassyi.GSharp.Stdlib.GpuMath against that assembly itself, whose records of the
        // file's own functions must not be imported (a real build compiles the file with the rest of its assembly)
        var importExclusions = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                options.GlobalOptions.TryGetValue("build_property.GSharpDeviceImportExclude", out var excluded)
                    ? excluded ?? ""
                    : "");
        var deviceImports = context.CompilationProvider
            .Combine(importExclusions)
            .Select(static (input, _) => DeviceImportAssembler.Collect(input.Left, input.Right))
            .WithTrackingName(DeviceImportTableTrackingName);

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

        // The structs that other assemblies can name and whose device definition a consumer cannot build from metadata;
        // the structs that device code uses come with the functions
        var standaloneStructs = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is StructDeclarationSyntax or RecordDeclarationSyntax
                {
                    ClassOrStructKeyword.RawKind: (int)SyntaxKind.StructKeyword
                },
                transform: static (ctx, _) => RenderStandaloneStruct(ctx))
            .WithTrackingName(StructExportTrackingName)
            .Where(static records => !records.IsDefaultOrEmpty)
            .Collect();

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

        // The imported text is resolved once for both outputs; an edit that reaches the same records leaves it equal
        var importedCode = allKernels.Combine(allDevices).Combine(deviceImports).Combine(configProvider)
            .Select(static (input, _) => ResolveImports(input.Left.Right, input.Left.Left.Left,
                GetDistinctDeviceMethods(input.Left.Left.Right), input.Right.IsCuda));

        context.RegisterSourceOutput(
            allDevices.Combine(importedCode).Combine(configProvider).Combine(hasKernels)
                .WithTrackingName(SharedSourcesTrackingName),
            static (spc, input) => EmitSharedSources(spc, input.Left.Left.Left, input.Left.Left.Right, input.Left.Right,
                input.Right));
        context.RegisterSourceOutput(kernelClasses.Combine(hostOptions).WithTrackingName(KernelWrapperTrackingName),
            static (spc, input) => EmitKernelClass(spc, input.Left, input.Right));
        context.RegisterSourceOutput(
            stateMachines.Combine(configProvider).Combine(hasKernels).WithTrackingName(DeviceStateMachinesTrackingName),
            static (spc, input) => EmitDeviceStateMachines(spc, input.Left.Left, input.Left.Right, input.Right));
        context.RegisterSourceOutput(
            aotTarget.Combine(allKernels).Combine(allDevices).Combine(importedCode).Combine(configProvider)
                .WithTrackingName(AotArtifactTrackingName),
            static (spc, input) => WriteAot(spc, input.Left.Left.Left.Left, input.Left.Left.Left.Right,
                input.Left.Left.Right, input.Left.Right, input.Right));
        context.RegisterSourceOutput(
            warmups.Combine(hostOptions).Combine(hasKernels).WithTrackingName(ModuleContextTrackingName),
            static (spc, input) => EmitModuleContext(spc, input.Left.Left, input.Left.Right, input.Right));
        // A library without kernels exports its device functions as well. The records are gathered first, so an edit
        // that changes no record (a kernel body) leaves the output cached
        var deviceExports = allDevices.Combine(allKernels).Combine(standaloneStructs).Combine(configProvider)
            .Select(static (input, _) => CollectDeviceExports(input.Left.Left.Left, input.Left.Left.Right,
                input.Left.Right, input.Right.ExportDeviceCode));
        context.RegisterSourceOutput(
            deviceExports.Combine(configProvider).WithTrackingName(DeviceExportsTrackingName),
            static (spc, input) => EmitDeviceExports(spc, input.Left, input.Right));
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

            var cudaStructs = new StructSourceCollector();
            var cppStructs = new StructSourceCollector();
            var cudaSource = cudaFailures.Apply(MethodTranspiler.GenerateCudaSource(in plan, cudaFailures.Fail, cudaDialect,
                out var requiredIncludesCuda, cudaStructs));
            var cppSource = cppFailures.Apply(MethodTranspiler.GenerateCudaSource(in plan, cppFailures.Fail, cppDialect,
                out var requiredIncludesCpp, cppStructs));
            var managedCSharpSource = MethodTranspiler.GenerateManagedCSharpSource(in plan);
            var structExports = DeviceExportEmitter.CreateStructRecords(context.SemanticModel.Compilation, cudaStructs,
                cppStructs);

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
                    "",
                    structExports
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
                "",
                structExports
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

            var cudaStructs = new StructSourceCollector();
            var cppStructs = new StructSourceCollector();
            var cudaSource = MethodTranspiler.GenerateCudaSource(in plan, cudaFailures.Fail, cudaDialect,
                out var requiredIncludesCuda, cudaStructs);
            var cppSource = MethodTranspiler.GenerateCudaSource(in plan, cppFailures.Fail, cppDialect,
                out var requiredIncludesCpp, cppStructs);
            var cudaBody = cudaFailures.Apply(cudaSource);
            var cppBody = cppFailures.Apply(cppSource);

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

            var frames = new EquatableArray<string>(frameDependencies.ToImmutable());
            var compilation = context.SemanticModel.Compilation;
            DeviceExportRecord export = default;
            var structExports = default(EquatableArray<DeviceExportRecord>);
            // [WHY] The records are built apart from the transpilation: an exception there fails the records (with the
            // message, which a consumer states in GSH0089) and leaves the library's own text as it is
            try
            {
                export = plan.Kind == MethodEmissionKind.Iterator
                    // An iterator is expanded where it is enumerated; another assembly gets GSH0003 for it
                    ? default
                    : CreateFunctionRecord(methodSymbol, compilation,
                        ExportedText(cudaSignature, cudaSource, cudaStructs, cudaFailures.HasFailures,
                            ToIncludes(requiredIncludesCuda), compilation, isCuda: true),
                        ExportedText(cppSignature, cppSource, cppStructs, cppFailures.HasFailures,
                            ToIncludes(requiredIncludesCpp), compilation, isCuda: false),
                        frameName, frames);
                structExports = DeviceExportEmitter.CreateStructRecords(compilation, cudaStructs, cppStructs);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                export = plan.Kind == MethodEmissionKind.Iterator
                    ? default
                    : CreateFailedFunctionRecord(methodSymbol, PartFailure.Describe(exception));
                structExports = default;
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
                frames,
                managedCSharpSource,
                export,
                structExports);
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

    // The text of one dialect as the function's record stores it: the signature, and the body without the struct and
    // Dispose definitions written in front of it, which the struct records hold. The definitions of the structs that
    // no record holds stay in front of the body (DeviceExportEmitter.CreateStructRecords, #538 review B1). Empty when
    // the dialect failed (the record says so, and a consumer copies no text of it)
    private static ExportedFunctionText ExportedText(string signature, string source, StructSourceCollector structs,
        bool failed, EquatableArray<string> includes, Compilation compilation, bool isCuda)
    {
        if (failed)
        {
            return new ExportedFunctionText("", "", true, includes, default);
        }

        var prefix = 0;
        foreach (var part in structs.Definitions)
        {
            prefix += part.Text.Length;
        }

        foreach (var part in structs.Disposes)
        {
            prefix += part.Text.Length;
        }

        var kept = ImmutableArray.CreateBuilder<string>();
        var body = DeviceExportEmitter.KeepUnrecorded(structs.Definitions, compilation, isCuda, kept) +
                   DeviceExportEmitter.KeepUnrecorded(structs.Disposes, compilation, isCuda, kept) +
                   source[prefix..];
        return new ExportedFunctionText(signature, body, false, includes, kept.ToImmutable().AsEquatableArray());
    }

    private readonly record struct ExportedFunctionText(
        string Signature,
        string Body,
        bool Failed,
        EquatableArray<string> Includes,
        EquatableArray<string> KeptNames);

    // The record of a function whose records could not be built: failed in both dialects, with the message
    private static DeviceExportRecord CreateFailedFunctionRecord(IMethodSymbol method, string internalError)
    {
        var cppName = GlobalCppNames.GetFunctionName(method);
        return default(DeviceExportRecord) with
        {
            Kind = DeviceExportRecord.FunctionKind,
            Id = method.OriginalDefinition.GetDocumentationCommentId() ?? cppName,
            CppName = cppName,
            CppFullName = GlobalCppNames.TryGetFullName(cppName, out var fullName) ? fullName : "",
            FrameName = "",
            Effect = "Memory",
            CudaSignature = "",
            CudaBody = "",
            CudaDispose = "",
            CppSignature = "",
            CppBody = "",
            CppDispose = "",
            CudaFailed = true,
            CppFailed = true,
            InternalError = internalError
        };
    }

    private static DeviceExportRecord CreateFunctionRecord(IMethodSymbol method, Compilation compilation,
        ExportedFunctionText cuda, ExportedFunctionText cpp, string frameName, EquatableArray<string> frameDependencies)
    {
        var cppName = GlobalCppNames.GetFunctionName(method);
        var sites = DynamicSharedMemoryAnalysis.GetReachableSites(method, compilation);
        var siteNames = ImmutableArray.CreateBuilder<string>(sites.Length);
        foreach (var site in sites)
        {
            siteNames.Add(site.ToString("x16", CultureInfo.InvariantCulture));
        }

        return new DeviceExportRecord(
            DeviceExportRecord.FunctionKind,
            method.OriginalDefinition.GetDocumentationCommentId() ?? cppName,
            cppName,
            GlobalCppNames.TryGetFullName(cppName, out var fullName) ? fullName : "",
            default,
            default,
            default,
            DeviceExportEmitter.CollectReferences(DeviceExportEmitter.OwnNames(cppName, default, cuda.KeptNames),
                cuda.Signature, cuda.Body),
            DeviceExportEmitter.CollectReferences(DeviceExportEmitter.OwnNames(cppName, default, cpp.KeptNames),
                cpp.Signature, cpp.Body),
            frameName,
            frameDependencies,
            SuspensionAnalysis.IsSuspendingFunction(method, compilation),
            SuspensionAnalysis.ReachesBlockBarrier(method, compilation),
            ContextInferenceEngine.IsParallelDependent(method,
                new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default), compilation),
            DynamicSharedMemoryAnalysis.ReachesDynamicSharedMemory(method, compilation),
            siteNames.MoveToImmutable().AsEquatableArray(),
            DeviceExportEmitter.FormatEffect(DeviceFunctionEffects.Classify(method, compilation)),
            cuda.Signature,
            cuda.Body,
            "",
            cpp.Signature,
            cpp.Body,
            "",
            cuda.Includes,
            cpp.Includes,
            cuda.Failed,
            cpp.Failed,
            ManagedStateMachine: false,
            "");
    }

    // A struct that other assemblies can name, rendered for both dialects (StructTranspiler.IsExportedOnItsOwn), with
    // the structs that its text names; a partial struct is rendered once, from its first declaration. Each struct keeps
    // its own failures (StructSourcePart.Failed)
    private static EquatableArray<DeviceExportRecord> RenderStandaloneStruct(GeneratorSyntaxContext context)
    {
        var compilation = context.SemanticModel.Compilation;
        if (context.SemanticModel.GetDeclaredSymbol(context.Node) is not INamedTypeSymbol type ||
            !StructTranspiler.IsExportedOnItsOwn(type, compilation) ||
            !type.TryGetDeclaringSyntax<TypeDeclarationSyntax>(out var first) || first != context.Node)
        {
            return default;
        }

        var cudaFailures = new FailureCollector();
        var cppFailures = new FailureCollector();
        try
        {
            var cuda = StructTranspiler.RenderStandalone(type, context.SemanticModel, new GSharpCudaDialect(),
                cudaFailures.Fail);
            var cpp = StructTranspiler.RenderStandalone(type, context.SemanticModel, new StandardCppDialect(),
                cppFailures.Fail);
            return DeviceExportEmitter.CreateStructRecords(compilation, cuda, cpp);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // [WHY] Not swallowed: the record is failed in both dialects and keeps the message (InternalError), which a
            // consumer that uses the struct states in GSH0089. A struct exported on its own may be used by no device
            // code of the library, so its failure does not stop the library's build (GSH0048)
            var failed = new StructDialectText("", "", true, default, default, PartFailure.Describe(exception));
            return ImmutableArray.Create(DeviceExportEmitter.CreateStructRecord(type, compilation, failed, failed))
                .AsEquatableArray();
        }
    }

    // [assembly: GpuDeviceExport(...)] for every exported device function and struct (GSharpExportDeviceCode)
    private static void EmitDeviceExports(SourceProductionContext context, DeviceExportSet exports, GeneratorConfig config)
    {
        if (!config.ExportDeviceCode || (exports.Functions.IsDefaultOrEmpty && exports.Structs.IsDefaultOrEmpty))
        {
            return;
        }

        context.AddSource(DeviceExportEmitter.HintName,
            SourceText.From(DeviceExportEmitter.Generate(exports.Functions.AsImmutableArray(),
                exports.Structs.AsImmutableArray(), DeviceExportRecord.CurrentPreludeHash), Encoding.UTF8));
    }

    // The records of the device functions, of the structs that the kernels and device functions use, and of the structs
    // exported on their own; none when the project exports nothing
    private static DeviceExportSet CollectDeviceExports(ImmutableArray<TranspiledDeviceMethodResult> deviceMethods,
        ImmutableArray<TranspiledKernelResult> kernelMethods,
        ImmutableArray<EquatableArray<DeviceExportRecord>> standaloneStructs, bool exportDeviceCode)
    {
        if (!exportDeviceCode)
        {
            return default;
        }

        var functions = ImmutableArray.CreateBuilder<DeviceExportRecord>();
        var structs = ImmutableArray.CreateBuilder<DeviceExportRecord>();
        standaloneStructs
            .AsStackEnumerable()
            .SelectMany(static records => records
                .AsStackEnumerable()
            ).AddTo(structs);
        foreach (var method in GetDistinctDeviceMethods(deviceMethods))
        {
            if (method.Export.IsValid)
            {
                functions.Add(method.Export);
            }

            method.StructExports
                .AsStackEnumerable()
                .AddTo(structs);
        }

        foreach (var kernel in kernelMethods)
        {
            kernel.StructExports
                .AsStackEnumerable()
                .AddTo(structs);
        }

        return new DeviceExportSet(functions.ToImmutable().AsEquatableArray(), structs.ToImmutable().AsEquatableArray());
    }

    // The prelude and the device functions shared by every kernel, as C# constants for the JIT path
    // [WHY] The generator's own failures (GSH0048) of the device functions are reported with the shared sources of a
    // module with kernels, and also by an assembly without kernels that exports its device code: its consumers would
    // otherwise only see GSH0088 for a function that has no record
    private static void EmitSharedSources(SourceProductionContext context,
        ImmutableArray<TranspiledDeviceMethodResult> deviceMethods, ImportedDeviceCode imports, GeneratorConfig config,
        bool hasKernels)
    {
        if (!hasKernels && !config.ExportDeviceCode)
        {
            return;
        }

        var distinctDeviceMethods = GetDistinctDeviceMethods(deviceMethods);
        foreach (var method in distinctDeviceMethods)
        {
            ReportInternalError(context, method.InternalError);
        }

        if (!hasKernels)
        {
            return;
        }

        if (!config.EmitSource)
        {
            return;
        }

        // The device code of other assemblies comes first; the JIT path compiles the same text as the AOT artifact
        var commonDeviceSource = imports.Source +
                                 NativeModuleAssembler.AssembleDeviceSource(distinctDeviceMethods, config.IsCuda);
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
        ImportedDeviceCode imports, GeneratorConfig config)
    {
        if (kernelMethods.IsDefaultOrEmpty)
        {
            return;
        }

        var isCuda = config.IsCuda;
        var distinctDeviceMethods = GetDistinctDeviceMethods(deviceMethods);
        var commonDeviceSource = imports.Source + NativeModuleAssembler.AssembleDeviceSource(distinctDeviceMethods, isCuda);
        var preludeSource = NativeModuleAssembler.GetPreludeSource(config.CreateDialect());

        var allKernelsSource = new StringBuilder();
        foreach (var method in kernelMethods)
        {
            allKernelsSource.AppendLine(method.GetSource(isCuda));
        }

        var requiredIncludes = NativeModuleAssembler.CollectIncludes(kernelMethods, distinctDeviceMethods, isCuda);
        imports.Includes
            .AsStackEnumerable()
            .AddTo(requiredIncludes);
        var bodySource = NativeModuleAssembler.AssembleBody(requiredIncludes, preludeSource, commonDeviceSource,
            allKernelsSource.ToString());
        AotArtifactWriter.Write(target, config, bodySource, context, imports.FindFullName);
    }

    // The device code of other assemblies that the kernels and device functions of the module reach
    private static ImportedDeviceCode ResolveImports(EquatableArray<ImportedDeviceRecord> deviceImports,
        ImmutableArray<TranspiledKernelResult> kernelMethods,
        ImmutableArray<TranspiledDeviceMethodResult>.Builder deviceMethods, bool isCuda)
    {
        if (deviceImports.IsDefaultOrEmpty)
        {
            return new ImportedDeviceCode("", default, default);
        }

        var texts = ImmutableArray.CreateBuilder<string>(kernelMethods.Length + deviceMethods.Count * 2);
        kernelMethods
            .AsStackEnumerable()
            .Select(isCuda, static (kernel, cuda) => kernel.GetSource(cuda))
            .AddTo(texts);
        foreach (var method in deviceMethods)
        {
            texts.Add(method.GetSignature(isCuda));
            texts.Add(method.GetBody(isCuda));
        }

        return DeviceImportAssembler.Resolve(deviceImports, isCuda, texts);
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
